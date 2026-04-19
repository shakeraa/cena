// =============================================================================
// Cena Platform — Admin API Host (DB-06b)
// Admin-facing REST endpoints for content management, moderation, analytics.
// Migrated from Cena.Api.Host — see README.md for migration notes.
// =============================================================================

using System.Security.Claims;
using System.Threading.RateLimiting;
using Cena.Actors.Bus;
using Cena.Actors.Configuration;
using Cena.Actors.Diagnosis;
using Cena.Admin.Api;
using Cena.Admin.Api.Host.Hubs;
using Cena.Admin.Api.Registration;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Configuration;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Observability;
using Marten;
using Microsoft.AspNetCore.RateLimiting;
using NATS.Client.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

using Microsoft.OpenApi.Models;
using Cena.Api.Contracts.Common;

var app = Program.BuildApp(args);

// DB-03: Fail fast on schema drift in non-Development environments.
// If AutoCreate is "None" and the DB schema does not match Marten config,
// AssertDatabaseMatchesConfigurationAsync throws with a detailed diff.
// The host process crashes, Kubernetes restarts, logs show the mismatch.
if (!app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<Marten.IDocumentStore>();
    try
    {
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "DB-03: Schema drift detected! Database does not match Marten configuration. Run the migrator first.");
        throw; // Crash the host — Kubernetes will restart, logs show the diff
    }
}


// RDY-056 §1.1: Warm Marten schema BEFORE hosted services start so seeders
// (CulturalContextSeeder et al.) don't all race on Weasel's TimedLock.
// In Development with AutoCreate=CreateOrUpdate, concurrent first-touch
// queries time out at the schema-ensure lock; warming up-front serialises
// the DDL and lets IHostedService.StartAsync run against a ready schema.
if (app.Environment.IsDevelopment())
{
    using var warmScope = app.Services.CreateScope();
    var warmLogger = warmScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var warmStore = warmScope.ServiceProvider.GetRequiredService<Marten.IDocumentStore>();
    try
    {
        warmLogger.LogInformation("[MARTEN_SCHEMA_WARM] applying configured changes...");
        await warmStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        warmLogger.LogInformation("[MARTEN_SCHEMA_READY] schema warm complete");
    }
    catch (Exception ex)
    {
        warmLogger.LogError(ex, "[MARTEN_SCHEMA_WARM] failed — host will still start; seeders may retry");
    }
}

app.Lifetime.ApplicationStarted.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    // RDY-009: Skip seeding when generating OpenAPI artifacts
    if (Environment.GetEnvironmentVariable("CENA_SKIP_SEED") == "1")
    {
        logger.LogInformation("Skipping seed for OpenAPI generation");
        return;
    }
    
    try
    {
        // Seed database (roles, users, classrooms, social data, etc.)
        var store = app.Services.GetRequiredService<IDocumentStore>();
        await CenaHostBootstrap.InitializeAsync(store, logger);
        
        // Initialize Firebase Admin SDK and sync admin claims
        // This will throw if Firebase credentials are misconfigured (fail-fast)
        var firebaseService = app.Services.GetRequiredService<IFirebaseAdminService>();
        await CenaHostBootstrap.InitializeFirebaseAsync(firebaseService, logger);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Admin API Host startup failed — triggering graceful shutdown");
        // Trigger graceful shutdown so orchestrators know the host is unhealthy
        app.Lifetime.StopApplication();
    }
});

app.Run();

public partial class Program
{
    public static WebApplication BuildApp(string[] args)
    {
    var builder = WebApplication.CreateBuilder(args);
    if (Environment.GetEnvironmentVariable("CENA_OPENAPI_GEN") == "1")
    {
        builder.WebHost.UseUrls("http://127.0.0.1:0");
    }
    
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
    
    // DB-03: Read AutoCreate mode from config — "None" in prod, "CreateOrUpdate" in dev
    var martenAutoCreate = builder.Configuration.GetValue<string>("Marten:AutoCreate") ?? "CreateOrUpdate";
    
    builder.Services.AddMarten(opts =>
    {
        var pgConnectionString = CenaConnectionStrings.GetPostgres(builder.Configuration, builder.Environment);
        opts.ConfigureCenaEventStore(pgConnectionString, martenAutoCreate);
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

    // RDY-034 / ADR-0002: CAS engine stack is required by the ingestion gate.
    // CasRouterService depends on MathNet + SymPy sidecar + ICostCircuitBreaker.
    // Mirrors the Student.Api.Host registrations.
    builder.Services.AddSingleton<Cena.Actors.RateLimit.ICostCircuitBreaker,
        Cena.Actors.RateLimit.RedisCostCircuitBreaker>();
    builder.Services.AddSingleton<Cena.Actors.RateLimit.ICostBudgetService,
        Cena.Actors.RateLimit.RedisCostBudgetService>();
    builder.Services.AddSingleton<Cena.Actors.Cas.IMathNetVerifier, Cena.Actors.Cas.MathNetVerifier>();
    builder.Services.AddSingleton<Cena.Actors.Cas.ISymPySidecarClient, Cena.Actors.Cas.SymPySidecarClient>();
    builder.Services.AddSingleton<Cena.Actors.Cas.ICasRouterService, Cena.Actors.Cas.CasRouterService>();

    // RDY-061: syllabus advancement — read-side + teacher-override writes.
    builder.Services.AddScoped<Cena.Actors.Advancement.IStudentAdvancementService,
        Cena.Actors.Advancement.StudentAdvancementService>();

    // RDY-056 §4 / Phase 5: OCR cascade wiring. Admin-only consumers take
    // IOcrCascadeService as an OPTIONAL (`? = null`) dependency; registering
    // OcrCascadeService here without the pluggable runner layers
    // (ILayer1Layout / ILayer2aTextOcr / ILayer2bMathOcr) would cause every
    // consumer to blow up at construction with "Unable to resolve
    // ILayer1Layout". The runners require either Surya + pix2tex sidecars
    // or Gemini / Mathpix API keys. Until at least one runner is wired in
    // appsettings, leave the cascade UNREGISTERED so `? = null` consumers
    // (CuratorMetadataExtractor, curator metadata service) fall back
    // cleanly. The non-optional consumer (BagrutPdfIngestionService) will
    // throw only when someone actually POSTs a PDF — which is the honest
    // signal that OCR isn't configured for this environment.
    //
    // To enable: uncomment both lines + wire at least one runner, e.g.
    //   builder.Services.Configure<GeminiVisionOptions>(
    //     builder.Configuration.GetSection("Ocr:Gemini"));
    //   builder.Services.AddSingleton<ILayer1Layout, SuryaLayer1Layout>();
    //   builder.Services.AddSingleton<ILayer2aTextOcr, TesseractLocalRunner>();
    //   builder.Services.AddSingleton<ILayer2bMathOcr, Pix2TexLayer2bMathOcr>();
    // See tasks/readiness/RDY-056-dev-stack-boot.md §"Still pending".
    //
    // Cena.Infrastructure.Ocr.DependencyInjection.OcrServiceCollectionExtensions
    //     .AddOcrCascadeCore(builder.Services, builder.Configuration);
    // builder.Services.AddSingleton<Cena.Infrastructure.Ocr.Cas.ILatexValidator,
    //     Cena.Actors.Cas.CasRouterLatexValidator>();

    builder.Services.AddCenaAdminServices();

    // RDY-063 Phase 2a: stuck-type classifier services (for admin
    // diagnostics read endpoints). Behaviour-gated by
    // Cena:StuckClassifier:Enabled; when off, the repository still
    // functions (returns empty distributions) and the admin pages
    // show "no data yet" without errors.
    builder.Services.AddStuckClassifier(builder.Configuration);

    // RDY-036: CAS startup probe — fails fast in Enforce mode if the CAS
    // engine stack is unreachable. Hosted service runs once at boot.
    builder.Services.AddHostedService<Cena.Admin.Api.Startup.CasBindingStartupCheck>();

    // RDY-040 / RDY-036 §5: Binding-coverage startup check — refuses to
    // serve traffic when published math/physics questions outnumber
    // Verified CAS bindings. Engine liveness (above) and data coverage
    // (this) are distinct failure modes; both must pass in Enforce.
    builder.Services.AddHostedService<Cena.Admin.Api.Startup.CasBindingCoverageStartupCheck>();

    // ---- Firebase Auth + Authorization ----
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddFirebaseAuth(builder.Configuration);
    builder.Services.AddCenaAuthorization();

    // RDY-060 — Admin SignalR hub + NATS bridge + Redis backplane.
    // Token-extraction chain MUST be added AFTER AddFirebaseAuth so it
    // can wrap the existing JwtBearerEvents.OnMessageReceived handler.
    builder.Services.AddCenaAdminSignalR(builder.Configuration);
    builder.Services.AddAdminSignalRTokenExtraction();
    
    // FIND-sec-014: Security metrics for observability
    builder.Services.AddSecurityMetrics();
    
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
    // FIND-data-020: Partitioned rate limiting per user + tenant-level outer limiter
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
        // General API: 100 req/min per user (partitioned by user id)
        options.AddPolicy("api", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
            
            return RateLimitPartition.GetFixedWindowLimiter(
                userId,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    
        // AI generation: 10 req/min per user with tenant-level outer limiter
        // Inner: per-user limit, Outer: per-school limit to prevent one classroom from starving others
        options.AddPolicy("ai", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
            var schoolId = httpContext.User.FindFirstValue("school_id") ?? "no-school";
            
            // Composite partition: user-specific with school as outer limit
            var partitionKey = $"{schoolId}:{userId}";
            
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    
        // Destructive operations: 2 req/min per user (partitioned by user id)
        options.AddPolicy("destructive", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
            
            return RateLimitPartition.GetFixedWindowLimiter(
                userId,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2,
                    Window = TimeSpan.FromMinutes(1),
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
    
    
    // RDY-009: Disable DI validation on build when generating OpenAPI artifacts.
    // Also disabled in Development so the app starts with partial DI graphs
    // (Admin Host doesn't instantiate every actor-host-only service); runtime
    // resolution errors surface on first call rather than blocking startup.
    if (Environment.GetEnvironmentVariable("CENA_OPENAPI_GEN") == "1"
        || builder.Environment.IsDevelopment())
    {
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
    }
    
    // ---- OpenAPI / Swagger (RDY-009) ----
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Cena Admin API",
            Version = "v1",
            Description = "Admin-facing REST endpoints for the Cena adaptive learning platform."
        });
    
        // Document canonical error shape
        options.MapType<CenaError>(() => new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["code"] = new OpenApiSchema { Type = "string" },
                ["message"] = new OpenApiSchema { Type = "string" },
                ["category"] = new OpenApiSchema { Type = "string" },
                ["details"] = new OpenApiSchema { Type = "object", AdditionalProperties = new OpenApiSchema { Type = "object" } },
                ["correlationId"] = new OpenApiSchema { Type = "string" }
            }
        });
    });
    
    // RDY-009: Remove hosted services during OpenAPI generation so missing dependencies don't block startup
    if (Environment.GetEnvironmentVariable("CENA_OPENAPI_GEN") == "1")
    {
        var hostedServices = builder.Services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        foreach (var svc in hostedServices)
        {
            builder.Services.Remove(svc);
        }
    }
    
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
    
    // Middleware order:
    //   CORS → Auth → Revocation → FERPA read audit → Admin write audit → RateLimiter → Endpoints
    // RDY-029 sub-task 5: AdminActionAuditMiddleware captures every
    // POST/PUT/PATCH/DELETE on /api/admin/* into AuditEventDocument +
    // a [AUDIT] structured log (shipped to Loki/ELK via the Serilog sink
    // configured in appsettings.Production.json).
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<TokenRevocationMiddleware>();
    app.UseMiddleware<StudentDataAuditMiddleware>();
    app.UseMiddleware<AdminActionAuditMiddleware>();
    app.UseRateLimiter();
    
    // ---- Swagger / OpenAPI (RDY-009) ----
    app.UseSwagger();
    if (!app.Environment.IsProduction())
    {
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Cena Admin API v1");
        });
    }
    
    // ---- Prometheus metrics endpoint ----
    app.MapPrometheusScrapingEndpoint();
    
    // ---- Health check ----
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-admin-api" }));
    app.MapHealthChecks("/health/live");
    app.MapHealthChecks("/health/ready");
    
    // ---- Admin REST API endpoints (migrated from Cena.Api.Host) ----
    app.MapCenaAdminEndpoints();

    // RDY-058: /api/admin/me/* — admin self-service account management
    // (profile, sign-out-everywhere, sign-in history, GDPR self-delete).
    Cena.Admin.Api.AdminMeEndpoints.MapAdminMeEndpoints(app);

    // RDY-061: syllabus + student advancement endpoints
    Cena.Admin.Api.Syllabus.SyllabusEndpoints.MapSyllabusEndpoints(app);

    // RDY-063 Phase 2a: stuck-type diagnostics (admin aggregate reads)
    Cena.Admin.Api.Diagnostics.StuckDiagnosticsEndpoints.MapStuckDiagnosticsEndpoints(app);

    // RDY-060: admin SignalR hub + health probe
    app.MapCenaAdminHub();
    
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
    
    // FIND-sec-007: Application started hook — seed database + init Firebase (fail-fast)
        return app;
    }
}

public class SwaggerHostFactory
{
    public static IHost CreateHost()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("CENA_OPENAPI_GEN", "1");
        Environment.SetEnvironmentVariable("CENA_SKIP_SEED", "1");
        Environment.SetEnvironmentVariable("Firebase__ProjectId", "cena-openapi-gen");
        var app = Program.BuildApp(Array.Empty<string>());
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }
}