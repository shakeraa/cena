// =============================================================================
// Cena Platform -- Admin API Host
// Separate process for admin REST endpoints (BKD-001/002/003/004).
// Connects to same PostgreSQL/Redis/Firebase as Actor Host but runs
// independently — no Proto.Actor cluster membership in this process.
// =============================================================================

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Cena.Actors.Configuration;
using Cena.Admin.Api;
using Cena.Admin.Api.Registration;
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
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- REV-011.3: Kestrel upload limits (50MB global max) ----
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB
});

// ---- Serilog ----
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Destructure.With<Cena.Infrastructure.Compliance.PiiDestructuringPolicy>();
});

// ---- Configuration ----
var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);

// ---- PostgreSQL: Shared NpgsqlDataSource + Marten ----
// API Host: max 30 connections (admin queries + pgvector search).
var pgMaxPool = builder.Configuration.GetValue<int>("PostgreSQL:MaxPoolSize", 30);
var pgMinPool = builder.Configuration.GetValue<int>("PostgreSQL:MinPoolSize", 3);
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

// ---- NATS (event bus for real-time dashboard) ----
var natsUrl = builder.Configuration.GetConnectionString("NATS") ?? "nats://localhost:4222";
builder.Services.AddSingleton<NATS.Client.Core.INatsConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<NATS.Client.Core.INatsConnection>>();

    // REV-002: NATS authentication — admin-api user (subscribe-only)
    var natsUser = builder.Configuration["Nats:User"] ?? "admin-api";
    var natsPass = builder.Configuration["Nats:Password"]
        ?? Environment.GetEnvironmentVariable("NATS_API_PASSWORD")
        ?? "dev_api_pass";

    var opts = new NATS.Client.Core.NatsOpts
    {
        Url = natsUrl,
        Name = "cena-admin-api",
        AuthOpts = new NATS.Client.Core.NatsAuthOpts { Username = natsUser, Password = natsPass },
    };
    logger.LogInformation("Configuring NATS connection to {NatsUrl} as {NatsUser}", natsUrl, natsUser);
    return new NATS.Client.Core.NatsConnection(opts);
});
builder.Services.AddSingleton<NatsEventSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsEventSubscriber>());

// ---- Firebase Auth + Authorization (BKD-001) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddCenaAuthorization();

// ---- SignalR Real-Time Hub (SES-001) ----
builder.Services.AddCenaSignalR();
builder.Services.AddSignalRTokenExtraction();

// ---- CORS (BKD-001.4) ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5174"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
            .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With",
                "X-SignalR-User-Agent") // SES-001: SignalR negotiation header
            .AllowCredentials();
    });
});

// ---- REV-011.1: Rate limiting ----
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

    // Destructive operations: 2 req/min per user
    options.AddFixedWindowLimiter("destructive", opt =>
    {
        opt.PermitLimit = 2;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
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

// ---- Admin Services (ADM-004 through ADM-023, REV-016.2) ----
builder.Services.AddSingleton<IFirebaseAdminService, FirebaseAdminService>();
builder.Services.AddCenaAdminServices();

// =============================================================================
// OPENTELEMETRY (REV-018.3)
// =============================================================================

var otlpEndpoint = builder.Configuration.GetValue<string>("Cluster:OtlpEndpoint")
    ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "cena-admin-api",
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

// ---- ERR-001.4: Correlation ID middleware (first — sets CorrelationContext for all downstream) ----
app.UseMiddleware<CorrelationIdMiddleware>();

// ---- ERR-001.2: Global exception handler (structured CenaError JSON, no stack trace leaks) ----
app.UseMiddleware<GlobalExceptionMiddleware>();

// ---- DATA-010: Concurrency conflict handler (Marten ConcurrencyException → 409) ----
app.UseMiddleware<Cena.Infrastructure.EventStore.ConcurrencyConflictMiddleware>();

// ---- REV-004: Security response headers ----
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["X-XSS-Protection"] = "0"; // Disabled per OWASP (modern browsers handle CSP)
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

// ---- Prometheus metrics endpoint (REV-018.3) ----
app.MapPrometheusScrapingEndpoint();

// ---- Health check ----
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-admin-api" }));

// ---- Admin REST API endpoints (ADM-004 through ADM-023, REV-016.2) ----
app.MapCenaAdminEndpoints();

// ---- Session Lifecycle REST endpoints (SES-002) ----
app.MapSessionEndpoints();
app.MapStudentAnalyticsEndpoints();

// ---- Me/Profile REST endpoints (STB-00, STB-00b) ----
app.MapMeEndpoints();

// ---- Classroom REST endpoints (STB-00b) ----
app.MapClassroomEndpoints();

// ---- Gamification REST endpoints (STB-03) ----
app.MapGamificationEndpoints();

// ---- Tutor REST endpoints (STB-04) ----
app.MapTutorEndpoints();

// ---- Challenges REST endpoints (STB-05) ----
app.MapChallengesEndpoints();

// ---- Social REST endpoints (STB-06) ----
app.MapSocialEndpoints();

// ---- Notifications REST endpoints (STB-07) ----
app.MapNotificationsEndpoints();

// ---- Knowledge/Content REST endpoints (STB-08) ----
app.MapKnowledgeEndpoints();

// ---- SignalR Hub (SES-001) ----
app.MapCenaHub();

// ---- REV-013: FERPA Compliance endpoints ----
app.MapComplianceEndpoints();

// ---- Seed predefined roles on startup ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(async () =>
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await DatabaseSeeder.SeedAllAsync(store, appLogger, 300,
        (s, l) => SimulationEventSeeder.SeedSimulationEventsAsync(s, l),
        QuestionBankSeedData.SeedQuestionsAsync);

    // Ensure Firebase Admin SDK is initialized, then sync claims for demo users
    _ = app.Services.GetRequiredService<IFirebaseAdminService>();
    await FirebaseClaimsSeeder.SyncAdminClaimsAsync(appLogger);
});

app.Run();
