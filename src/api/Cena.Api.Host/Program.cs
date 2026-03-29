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
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Configuration;
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
    configuration.ReadFrom.Configuration(context.Configuration);
});

// ---- Configuration ----
var pgConnectionString = CenaConnectionStrings.GetPostgres(builder.Configuration, builder.Environment);
var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);

// ---- Marten (PostgreSQL) — same DB as actor host ----
builder.Services.AddMarten(opts =>
{
    opts.ConfigureCenaEventStore(pgConnectionString);
});

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
    var opts = new NATS.Client.Core.NatsOpts { Url = natsUrl, Name = "cena-admin-api" };
    logger.LogInformation("Configuring NATS connection to {NatsUrl}", natsUrl);
    return new NATS.Client.Core.NatsConnection(opts);
});
builder.Services.AddSingleton<NatsEventSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsEventSubscriber>());

// ---- Firebase Auth + Authorization (BKD-001) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddCenaAuthorization();

// ---- CORS (BKD-001.4) ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5174"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
            .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With")
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

// ---- Admin Services (ADM-004 through ADM-016) ----
builder.Services.AddSingleton<IFirebaseAdminService, FirebaseAdminService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminRoleService, AdminRoleService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IContentModerationService, ContentModerationService>();
builder.Services.AddScoped<IFocusAnalyticsService, FocusAnalyticsService>();
builder.Services.AddScoped<IMasteryTrackingService, MasteryTrackingService>();
builder.Services.AddScoped<ISystemMonitoringService, SystemMonitoringService>();
builder.Services.AddScoped<IIngestionPipelineService, IngestionPipelineService>();
builder.Services.AddSingleton<Cena.Admin.Api.QualityGate.IQualityGateService>(sp =>
    new Cena.Admin.Api.QualityGate.QualityGateService(
        configuration: sp.GetRequiredService<IConfiguration>(),
        logger: sp.GetRequiredService<ILogger<Cena.Admin.Api.QualityGate.QualityGateService>>()));
builder.Services.AddSingleton<IAiGenerationService, AiGenerationService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IMethodologyAnalyticsService, MethodologyAnalyticsService>();
builder.Services.AddScoped<ICulturalContextService, CulturalContextService>();
builder.Services.AddScoped<IEventStreamService, EventStreamService>();
builder.Services.AddScoped<IOutreachEngagementService, OutreachEngagementService>();

// ---- SAI Admin Services (ADM-017 through ADM-023) ----
builder.Services.AddScoped<ITutoringAdminService, TutoringAdminService>();
builder.Services.AddScoped<IExplanationCacheAdminService, ExplanationCacheAdminService>();
builder.Services.AddScoped<IExperimentAdminService, ExperimentAdminService>();
builder.Services.AddScoped<IEmbeddingAdminService, EmbeddingAdminService>();
builder.Services.AddScoped<ITokenBudgetAdminService, TokenBudgetAdminService>();

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

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

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

// Middleware order: CORS → Auth → Revocation → RateLimiter → Endpoints
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TokenRevocationMiddleware>();
app.UseRateLimiter();

// ---- Prometheus metrics endpoint (REV-018.3) ----
app.MapPrometheusScrapingEndpoint();

// ---- Health check ----
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-admin-api" }));

// ---- Admin REST API endpoints (ADM-004 through ADM-016) ----
app.MapAdminUserEndpoints();
app.MapAdminRoleEndpoints();
app.MapAdminDashboardEndpoints();
app.MapContentModerationEndpoints();
app.MapFocusAnalyticsEndpoints();
app.MapMasteryTrackingEndpoints();
app.MapSystemMonitoringEndpoints();
app.MapIngestionPipelineEndpoints();
app.MapQuestionBankEndpoints();
app.MapMethodologyAnalyticsEndpoints();
app.MapCulturalContextEndpoints();
app.MapEventStreamEndpoints();
app.MapOutreachEngagementEndpoints();
app.MapAiGenerationEndpoints();

// ---- SAI Admin endpoints (ADM-017 through ADM-023) ----
app.MapTutoringAdminEndpoints();
app.MapExplanationCacheEndpoints();
app.MapExperimentAdminEndpoints();
app.MapEmbeddingAdminEndpoints();
app.MapTokenBudgetEndpoints();

// ---- Seed predefined roles on startup ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(async () =>
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await DatabaseSeeder.SeedAllAsync(store, appLogger, 300,
        (s, l) => SimulationEventSeeder.SeedSimulationEventsAsync(s, l),
        QuestionBankSeedData.SeedQuestionsAsync);
});

app.Run();
