// =============================================================================
// Cena Platform — Admin API Host (DB-06b)
// Admin-facing REST endpoints for content management, moderation, analytics.
// Migrated from Cena.Api.Host — see README.md for migration notes.
// =============================================================================

using System.Threading.RateLimiting;
using Cena.Actors.Bus;
using Cena.Actors.Configuration;
using Cena.Admin.Api;
using Cena.Admin.Api.Registration;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Configuration;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Firebase;
using Marten;
using Microsoft.AspNetCore.RateLimiting;
using NATS.Client.Core;
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
        Name = "cena-admin-api",
        AuthOpts = new NATS.Client.Core.NatsAuthOpts { Username = natsUser, Password = natsPass },
    };
    logger.LogInformation("Configuring NATS connection to {NatsUrl} as {NatsUser}", natsUrl, natsUser);
    return new NATS.Client.Core.NatsConnection(opts);
});
builder.Services.AddSingleton<NatsEventSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsEventSubscriber>());

// ---- Admin Services ----
builder.Services.AddSingleton<IFirebaseAdminService, FirebaseAdminService>();
builder.Services.AddCenaAdminServices();

// ---- Firebase Auth + Authorization ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddCenaAuthorization();

// ---- CORS ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5175" };

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

// ---- Health checks ----
builder.Services.AddHealthChecks();

// ---- OpenTelemetry ----
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
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-admin-api" }));
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// ---- Admin REST API endpoints (migrated from Cena.Api.Host) ----
app.MapCenaAdminEndpoints();

// ---- Classroom endpoints (STB-00b) ----
app.MapClassroomEndpoints();

// ---- Content management endpoints ----
app.MapContentEndpoints();

// ---- FERPA Compliance endpoints (FIND-arch-008) ----
app.MapComplianceEndpoints();

// ---- Root endpoint ----
app.MapGet("/", () => Results.Ok(new 
{ 
    service = "Cena Admin API Host", 
    status = "healthy (DB-06b)",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();
