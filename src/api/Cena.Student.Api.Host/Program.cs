// =============================================================================
// Cena Platform — Student API Host (DB-06b)
// Student-facing REST endpoints + SignalR real-time tutoring hub.
// Migrated from Cena.Api.Host — see README.md for migration notes.
// =============================================================================

using System.Threading.RateLimiting;
using Cena.Actors.Bus;
using Cena.Actors.Configuration;
using Cena.Actors.Notifications;
using Cena.Actors.Services;
using Cena.Actors.Serving;
using Cena.Actors.Tutor;
using Cena.Api.Host.Endpoints;
using Cena.Api.Host.Hubs;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Configuration;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Seed;
using Marten;
using Microsoft.AspNetCore.RateLimiting;
using NATS.Client.Core;
using NatsEventSubscriber = Cena.Admin.Api.NatsEventSubscriber;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
// FIND-sec-004: Use 3-arg overload to access services and add PII destructuring policy
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Destructure.With<PiiDestructuringPolicy>()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
});

// ---- Configuration ----
var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);

// ---- PostgreSQL: Shared NpgsqlDataSource + Marten ----
var pgMaxPool = builder.Configuration.GetValue<int>("PostgreSQL:MaxPoolSize", 50);
var pgMinPool = builder.Configuration.GetValue<int>("PostgreSQL:MinPoolSize", 5);
builder.Services.AddCenaDataSource(builder.Configuration, builder.Environment, pgMaxPool, pgMinPool);

builder.Services.AddMarten(opts =>
{
    var pgConnectionString = CenaConnectionStrings.GetPostgres(builder.Configuration, builder.Environment);
    opts.ConfigureCenaEventStore(pgConnectionString);
}).UseNpgsqlDataSource();

// ---- Redis ----
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.Password = builder.Configuration["Redis:Password"]
        ?? Environment.GetEnvironmentVariable("REDIS_PASSWORD")
        ?? (builder.Environment.IsDevelopment() ? "cena_dev_redis" : null);
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 3;
    options.ConnectTimeout = 5000;
    return ConnectionMultiplexer.Connect(options);
});

// ---- NATS ----
var natsUrl = builder.Configuration.GetConnectionString("NATS") ?? "nats://localhost:4222";
builder.Services.AddSingleton<NATS.Client.Core.INatsConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<NATS.Client.Core.INatsConnection>>();
    // FIND-sec-003: Use centralized NATS auth resolution with dev-only fallback
    var (natsUser, natsPass) = CenaNatsOptions.GetApiAuth(builder.Configuration, builder.Environment);

    var opts = new NATS.Client.Core.NatsOpts
    {
        Url = natsUrl,
        Name = "cena-student-api",
        AuthOpts = new NATS.Client.Core.NatsAuthOpts { Username = natsUser, Password = natsPass },
    };
    logger.LogInformation("Configuring NATS connection to {NatsUrl} as {NatsUser}", natsUrl, natsUser);
    return new NATS.Client.Core.NatsConnection(opts);
});
builder.Services.AddSingleton<NatsEventSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsEventSubscriber>());

// ---- STB-07b: Notification Dispatcher ----
builder.Services.AddHostedService<NotificationDispatcher>();

// ---- HARDEN SessionEndpoints: Question Bank Service ----
builder.Services.AddScoped<IQuestionBank, QuestionBank>();

// ---- FIND-pedagogy-003: Real BKT posterior for session answer endpoint ----
// BktService is stateless, allocation-free on the hot path, and cheap to
// instantiate — singleton is fine. The student API host used to compute
// posterior mastery as PriorMastery + 0.05, which bypassed BKT entirely.
builder.Services.AddSingleton<IBktService, BktService>();

// ---- FIND-pedagogy-009: Elo difficulty calibration for 85% rule ----
// Updates question DifficultyElo after each answer using Elo formula.
// Wilson et al. (2019): "The Eighty Five Percent Rule for optimal learning."
builder.Services.AddScoped<IEloDifficultyService, EloDifficultyService>();

// ---- HARDEN TutorEndpoints: LLM Service ----
var llmApiKey = builder.Configuration["Cena:Llm:ApiKey"];
if (!string.IsNullOrEmpty(llmApiKey))
{
    builder.Services.AddScoped<ITutorLlmService, ClaudeTutorLlmService>();
    Log.Information("ClaudeTutorLlmService registered for AI tutoring");
}
else
{
    builder.Services.AddScoped<ITutorLlmService, NullTutorLlmService>();
    Log.Warning("NullTutorLlmService registered — Cena:Llm:ApiKey not configured.");
}

// FIND-arch-004: Non-streaming tutor message path. Both /messages (unary) and
// /stream (SSE) must go through real LLM. TutorMessageService wraps the same
// ITutorLlmService the /stream endpoint uses, so there is no "stub" code path.
builder.Services.AddScoped<ITutorMessageRepository, MartenTutorMessageRepository>();
builder.Services.AddScoped<ITutorMessageService, TutorMessageService>();

// ---- Firebase Auth + Authorization ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddCenaAuthorization();

// FIND-ux-006b: the student host needs the Firebase Admin SDK wrapper to
// back the anonymous POST /api/auth/password-reset endpoint. The admin host
// already registers this as a singleton; mirror that here so the student
// self-service forgot-password flow has a real implementation on both sides.
builder.Services.AddSingleton<IFirebaseAdminService, FirebaseAdminService>();

// ---- SignalR Real-Time Hub ----
builder.Services.AddCenaSignalR();
builder.Services.AddSignalRTokenExtraction();

// ---- CORS ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5174" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
            .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With",
                "X-SignalR-User-Agent")
            .AllowCredentials();
    });
});

// ---- Rate limiting ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // General API: 100 req/min per user
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // AI generation: 10 req/min per user (cost protection)
    options.AddFixedWindowLimiter("ai", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Tutor LLM: 10 messages/min per student
    options.AddFixedWindowLimiter("tutor", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // FIND-ux-006b: password-reset is anonymous and must be partitioned by
    // remote IP so a single attacker cannot drain a shared quota for every
    // other visitor. 5 requests / 5 minutes per IP is aligned with common
    // abuse-prevention guidance for unauthed account recovery endpoints.
    options.AddPolicy("password-reset", httpContext =>
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        var partitionKey = !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded. Please try again later.",
            retryAfterSeconds = 60
        });
    };
});

// ---- Health checks ----
builder.Services.AddHealthChecks();

// ---- OpenTelemetry ----
var otlpEndpoint = builder.Configuration.GetValue<string>("Cluster:OtlpEndpoint")
    ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "cena-student-api",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
        .AddPrometheusExporter());

// =============================================================================
// BUILD & PIPELINE
// =============================================================================

var app = builder.Build();

// ---- Correlation ID middleware ----
app.UseMiddleware<CorrelationIdMiddleware>();

// ---- Global exception handler ----
app.UseMiddleware<GlobalExceptionMiddleware>();

// ---- Concurrency conflict handler ----
app.UseMiddleware<Cena.Infrastructure.EventStore.ConcurrencyConflictMiddleware>();

// ---- Security response headers ----
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["X-XSS-Protection"] = "0";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none';";

    if (!context.Request.Path.StartsWithSegments("/health"))
    {
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    await next();
});

// Middleware order: CORS → Auth → Revocation → FERPA Audit → RateLimiter → Endpoints
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TokenRevocationMiddleware>();
app.UseMiddleware<StudentDataAuditMiddleware>();
app.UseRateLimiter();

// ---- Prometheus metrics endpoint ----
app.MapPrometheusScrapingEndpoint();

// ---- Health check ----
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-student-api" }));
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// ---- Student-facing REST endpoints (migrated from Cena.Api.Host) ----

// Anonymous auth recovery endpoints (FIND-ux-006b) — password reset only
app.MapAuthEndpoints();

// Me/Profile endpoints (STB-00, STB-00b)
app.MapMeEndpoints();

// Session Lifecycle endpoints (STB-01, STB-01b, STB-01c)
app.MapSessionEndpoints();

// Plan/Recommendation endpoints (STB-02)
app.MapPlanEndpoints();

// Gamification endpoints (STB-03, STB-03b, STB-03c)
app.MapGamificationEndpoints();

// Tutor endpoints (STB-04, STB-04b)
app.MapTutorEndpoints();

// Challenges endpoints (STB-05, STB-05b)
app.MapChallengesEndpoints();

// Social endpoints (STB-06, STB-06b)
app.MapSocialEndpoints();

// Notifications endpoints (STB-07, STB-07b, STB-07c)
app.MapNotificationsEndpoints();

// Knowledge/Content endpoints (STB-08)
app.MapKnowledgeEndpoints();

// Student Analytics endpoints (STB-09)
app.MapStudentAnalyticsEndpoints();

// ---- SignalR Hub ----
app.MapCenaHub();

// ---- Root endpoint ----
app.MapGet("/", () => Results.Ok(new 
{ 
    service = "Cena Student API Host", 
    status = "healthy (DB-06b)",
    timestamp = DateTimeOffset.UtcNow
}));

// FIND-sec-007: Application started hook — seed database (no Firebase admin claims sync)
app.Lifetime.ApplicationStarted.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Seed database (roles, users, classrooms, social data, etc.)
        // Student host seeds the same data but does NOT sync Firebase admin claims
        var store = app.Services.GetRequiredService<IDocumentStore>();
        await CenaHostBootstrap.InitializeAsync(store, logger);
        
        // Note: Firebase claims sync is admin-host only — student host should not
        // provision admin users. Firebase Auth for students is handled at registration time.
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Student API Host startup failed — triggering graceful shutdown");
        // Trigger graceful shutdown so orchestrators know the host is unhealthy
        app.Lifetime.StopApplication();
    }
});

app.Run();
