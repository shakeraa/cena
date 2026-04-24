// =============================================================================
// Cena Platform — Admin API Host (DB-06b)
// Admin-facing REST endpoints for content management, moderation, analytics.
// Migrated from Cena.Api.Host — see README.md for migration notes.
// =============================================================================

using System.Security.Claims;
using System.Threading.RateLimiting;
using Cena.Actors.Assessment.Rubric;
using Cena.Actors.Bus;
using Cena.Actors.Configuration;
using Cena.Actors.Diagnosis;
using Cena.Actors.Infrastructure.Privacy;
using Cena.Actors.Notifications;
using Cena.Actors.Retention;
using Cena.Actors.StudentPlan;
using Cena.Actors.Teacher.ScheduleOverride;
using Cena.Admin.Api;
using Cena.Admin.Api.Features.Teacher;
using Cena.Admin.Api.Host.Hubs;
using Cena.Admin.Api.Registration;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Analytics;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Configuration;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Llm;
using Cena.Infrastructure.Observability;
using Cena.Infrastructure.Observability.ErrorAggregator;
using Marten;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // PRR-438: swagger-gen boot must not collide with a running Docker
        // admin-api container on port 5052. Override the Kestrel:Endpoints:*
        // config keys (which win over UseUrls) to bind an ephemeral loopback
        // port, then belt-and-braces UseUrls the same.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:0",
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
    }
    
    // ---- Serilog ----
    // FIND-sec-004: Use 3-arg overload to access services and add PII destructuring policy.
    // prr-013 / ADR-0003 / RDY-080: SessionRiskLogEnricher scrubs theta / ability /
    // risk / readiness scalars; outputTemplate renders {RedactedMessage} in place of
    // {Message:lj} so sinks emit the scrubbed form.
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.With<SessionRiskLogEnricher>()
            .Destructure.With<PiiDestructuringPolicy>()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {RedactedMessage}{NewLine}{Exception}");

        // RDY-064 / ADR-0058: conditional Sentry Serilog sink. See actor-host
        // Program.cs for the shared rationale. InitializeSdk=false because
        // SentryErrorAggregator owns the single canonical Init call.
        var dsn = context.Configuration["ErrorAggregator:Dsn"];
        var backend = context.Configuration["ErrorAggregator:Backend"];
        if (!string.IsNullOrWhiteSpace(dsn)
            && string.Equals(backend, "sentry", StringComparison.OrdinalIgnoreCase))
        {
            configuration.WriteTo.Sentry(s =>
            {
                s.Dsn = dsn;
                s.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
                s.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
                s.InitializeSdk = false;
            });
        }
    });
    
    // ---- Configuration ----
    var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);
    
    // ---- PostgreSQL: Shared NpgsqlDataSource + Marten ----
    var pgMaxPool = builder.Configuration.GetValue<int>("PostgreSQL:MaxPoolSize", 30);
    var pgMinPool = builder.Configuration.GetValue<int>("PostgreSQL:MinPoolSize", 3);
    builder.Services.AddCenaDataSource(builder.Configuration, builder.Environment, pgMaxPool, pgMinPool);

    // ADR-0038 / prr-003b: Subject key store for crypto-shredding. Must be
    // registered AFTER AddCenaDataSource so the Postgres backing (selected
    // in non-Development environments) resolves NpgsqlDataSource from DI.
    // Also installs the dev-fallback health-check that refuses prod boot
    // when CENA_PII_ROOT_KEY_BASE64 is not set.
    builder.Services.AddSubjectKeyStore(builder.Configuration, builder.Environment);

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
        // Bump from 3s default. Shared multiplexer queues bursts of 100+
        // async ops under load (rate-limit, circuit-breaker, session
        // metrics); 3s is too tight and causes false RedisTimeoutException.
        options.SyncTimeout = 10000;
        options.AsyncTimeout = 10000;
        // RedisSessionStoreMetricsService polls INFO for per-keyspace session
        // counts; StackExchange.Redis blocks admin commands by default.
        options.AllowAdmin = true;
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
    // prr-010: SymPy template guard runs on every CAS request before NATS marshalling.
    builder.Services.AddSingleton<Cena.Actors.Cas.ISymPyTemplateGuard, Cena.Actors.Cas.SymPyTemplateGuard>();
    builder.Services.AddSingleton<Cena.Actors.Cas.ISymPySidecarClient, Cena.Actors.Cas.SymPySidecarClient>();
    builder.Services.AddSingleton<Cena.Actors.Cas.ICasRouterService, Cena.Actors.Cas.CasRouterService>();

    // RDY-061: syllabus advancement — read-side + teacher-override writes.
    builder.Services.AddScoped<Cena.Actors.Advancement.IStudentAdvancementService,
        Cena.Actors.Advancement.StudentAdvancementService>();

    // prr-150: teacher/mentor schedule-override bounded context. Registers
    // ITeacherOverrideStore (in-memory Phase 1), IStudentInstituteLookup
    // (in-memory default; admin host may replace with a Marten-backed
    // EnrollmentDocument lookup in a follow-up), TeacherOverrideCommands,
    // and the IOverrideAwareSchedulerInputsBridge that applies overrides
    // on top of prr-149's SchedulerInputs.
    builder.Services.AddTeacherOverrideServices();

    // prr-218 + prr-219: StudentPlan multi-target aggregate + migration
    // safety net. Registers the in-memory aggregate store (which also
    // satisfies IMigrationMarkerStore), command handler, readers, and the
    // migration service. Feature flag defaults off so deployment is safe.
    builder.Services.AddStudentPlanServices();

    // prr-218 production binding: replace the in-memory aggregate store
    // default with the Marten-backed MartenStudentPlanAggregateStore and
    // register every StudentPlan event type on the configured
    // StoreOptions. Per memory "No stubs — production grade" (2026-04-11),
    // the in-memory store is test-only; production (Admin.Api.Host)
    // persists the StudentPlanAggregate event stream via Marten so teacher
    // classroom assignments (prr-236) and admin consent exports survive a
    // process restart.
    builder.Services.AddStudentPlanMarten();

    // prr-222 production binding: same Marten replacement for the
    // skill-keyed mastery store. The Admin Host reads mastery rows to
    // power the teacher dashboard and admin audit exports; persistence is
    // required so those surfaces don't read empty after a deploy.
    builder.Services.AddExamTargetRetentionServices();
    builder.Services.AddSkillKeyedMasteryMarten();

    // prr-229 production binding: Marten replacement for the retention
    // extension store so admin audit exports see the accurate 60-month
    // opt-in state per student (ADR-0050 §6).
    builder.Services.AddExamTargetRetentionExtensionMarten();

    // prr-229 production binding: Marten replacement for the archived
    // exam-target source so ExamTargetRetentionWorker can actually find
    // archived targets to shred. The in-memory source is an empty
    // ConcurrentDictionary in production (no Append caller exists),
    // which silently turns the ADR-0050 §6 24-month retention window
    // into a no-op. The Marten source reads canonical archive state
    // from the StudentPlan event log and persists shred markers so
    // sweeps progress monotonically across restarts.
    builder.Services.AddArchivedExamTargetSourceMarten();

    // prr-009 / EPIC-PRR-C / EPIC-PRR-M production binding: replace the
    // in-memory parent-child binding store with MartenParentChildBindingStore
    // so parent authorization grants survive a process restart. Per memory
    // "No stubs — production grade" (2026-04-11) + the ParentChildBinding.cs
    // doc comment noting "the JWT parent_of claim is an advisory cache;
    // this store is the authoritative source of truth" — an in-memory
    // source of truth that forgets every pod restart is a compliance gap.
    Cena.Actors.Parent.ParentServiceRegistration.AddParentChildBindingMarten(
        builder.Services);

    // prr-155 / ADR-0042 production binding: Marten replacement for the
    // consent aggregate event store. Consent is the compliance audit
    // trail — losing it on pod restart is unrecoverable, and ADR-0038
    // event-sourced RTBF assumes the stream is durable so crypto-shreds
    // can land idempotently against real events.
    Cena.Actors.Consent.ConsentServiceRegistration.AddConsentAggregateMarten(
        builder.Services);

    // prr-150 production binding: Marten replacement for the teacher-
    // override aggregate store. Without this, every motivation-profile
    // override, budget adjustment, and pinned topic silently reverts on
    // every deploy — a teacher-trust erosion at scale.
    Cena.Actors.Teacher.ScheduleOverride.TeacherOverrideServiceRegistration
        .AddTeacherOverrideMarten(builder.Services);

    // prr-236: Classroom-assigned target teacher UI — Marten-backed roster
    // lookup that feeds the classroom-target fan-out service. The service
    // itself is registered by AddStudentPlanServices above; only the
    // Marten-backed IClassroomRosterLookup is host-specific.
    builder.Services.AddSingleton<
        Cena.Actors.StudentPlan.IClassroomRosterLookup,
        Cena.Admin.Api.Host.Endpoints.MartenClassroomRosterLookup>();

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

    // PRR-393 + PRR-391: admin-side PhotoDiagnostic surface. PRR-393 only
    // needed the dispute-metrics read path (dispute repository + metrics
    // service + TimeProvider). PRR-391 adds the one-click credit flow,
    // which needs the full credit-ledger + dispute service + quota gate
    // graph — so we call AddPhotoDiagnosticMarten here. AddSubscriptions-
    // Marten must precede it because the quota gate depends on
    // IStudentEntitlementResolver + IPerTierCapEnforcer which live in the
    // subscriptions bounded context. TryAdd-guarded throughout so the
    // dispute repository + metrics service the PRR-393 endpoint consumes
    // are registered transitively by AddPhotoDiagnosticMarten's shared
    // services block (MartenDiagnosticDisputeRepository, MartenDispute-
    // MetricsService).
    builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
    Cena.Actors.Subscriptions.SubscriptionServiceRegistration
        .AddSubscriptionsMarten(builder.Services);
    Cena.Actors.Diagnosis.PhotoDiagnostic.PhotoDiagnosticServiceRegistration
        .AddPhotoDiagnosticMarten(builder.Services);

    // prr-033: Ministry Bagrut rubric DSL + version pinning (ADR-0055).
    // Loads contracts/rubric/*.yml on boot; fails fast on malformed rubrics.
    builder.Services.AddCenaRubricVersionPinning(
        builder.Configuration, builder.Environment);

    // prr-035: Sub-processor registry. Loads contracts/privacy/sub-processors.yml
    // on boot; fails fast if any entry is missing DPA/SSO/residency/purpose.
    builder.Services.AddCenaSubProcessorRegistry(
        builder.Configuration, builder.Environment);

    // prr-046: per-feature LLM cost metric (fail-loud pricing from routing-config.yaml).
    // AiGenerationService + QualityGateService + AiFigureGenerator depend on ILlmCostMetric.
    builder.Services.AddLlmCostMetric(Path.Combine(
        builder.Environment.ContentRootPath,
        Cena.Infrastructure.Llm.LlmCostMetricRegistration.DefaultRoutingConfigRelativePath));

    // prr-026: k-anonymity enforcer — injected by every aggregate
    // teacher/classroom/institute surface that serves a statistical claim.
    builder.Services.AddKAnonymityEnforcer();

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

    // RDY-064: Error aggregator scaffold — registers Null aggregator by
    // default. Switching to Sentry / AppInsights is blocked on the RDY-064 ADR.
    builder.Services.AddCenaErrorAggregator(builder.Configuration);

    // PRR-428: Notifications DI — config-driven Email/SMS/WhatsApp backend
    // selection. See Notifications:* in appsettings.json.
    builder.Services.AddCenaNotifications(builder.Configuration);

    // PRR-437: Meta WhatsApp inbound delivery-status webhook. Registers the
    // HMAC-SHA256 signature verifier (iff MetaCloud:AppSecret is populated)
    // + an in-memory dedup store for Meta's retried POSTs. The endpoint
    // itself (MapMetaWhatsAppWebhook below) is wired unconditionally; it
    // returns 404 when !IsWebhookReady so probes on a mis-configured host
    // don't pretend to be wired.
    {
        var metaSection = builder.Configuration.GetSection(
            Cena.Actors.ParentDigest.MetaCloudWhatsAppOptions.SectionName);
        var appSecret = metaSection["AppSecret"];
        if (!string.IsNullOrWhiteSpace(appSecret))
        {
            builder.Services.AddSingleton<
                Cena.Actors.ParentDigest.IMetaWebhookSignatureVerifier>(
                _ => new Cena.Actors.ParentDigest.MetaWebhookSignatureVerifier(appSecret));
        }
        builder.Services.TryAddSingleton<
            Cena.Admin.Api.Host.Endpoints.IMetaWebhookDedupStore,
            Cena.Admin.Api.Host.Endpoints.InMemoryMetaWebhookDedupStore>();
    }

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

        // prr-021: Roster import rate limit — 5/hour per tenant (configurable
        // via Cena:RosterImport:ImportsPerHourPerTenant). Partitioned by
        // school_id so one school cannot starve another of quota; SUPER_ADMIN
        // runs without a school_id get their own "(super_admin)" bucket.
        var rosterPermit = builder.Configuration
            .GetValue<int?>("Cena:RosterImport:ImportsPerHourPerTenant") ?? 5;
        options.AddPolicy("admin-roster-import", httpContext =>
        {
            var tenantId = httpContext.User.FindFirstValue("school_id")
                ?? "(super_admin)";
            return RateLimitPartition.GetFixedWindowLimiter(
                tenantId,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rosterPermit,
                    Window = TimeSpan.FromHours(1),
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

    // prr-021: Bind RosterImportOptions so both the sanitizer defaults and
    // the rate limiter pick up deployment-specific overrides.
    builder.Services.Configure<Cena.Infrastructure.Security.RosterImportOptions>(
        builder.Configuration.GetSection(Cena.Infrastructure.Security.RosterImportOptions.SectionName));
    
    // ---- Health checks ----
    builder.Services.AddHealthChecks();
    
    // ---- OpenTelemetry ----
    var otlpEndpoint = builder.Configuration.GetValue<string>("Cluster:OtlpEndpoint")
        ?? "http://localhost:4317";
    
    // RDY-064 / ADR-0058 §3: release correlation. service.version shares the
    // CENA_GIT_SHA string that Sentry uses for release tags.
    var otelServiceVersion = builder.Configuration["ErrorAggregator:Release"]
        ?? builder.Configuration["Cluster:ServiceVersion"]
        ?? "unknown";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName: "cena-admin-api",
                serviceVersion: otelServiceVersion,
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

    // PRR-437: Meta WhatsApp inbound webhook (GET handshake + POST
    // delivery-status callback). Lives on the admin host adjacent to
    // the Stripe-webhook pattern; anonymous-auth (signature-verified)
    // per Meta's protocol — Meta doesn't carry our auth tokens.
    Cena.Admin.Api.Host.Endpoints.MetaWhatsAppWebhookEndpoint.MapMetaWhatsAppWebhook(app);

    // RDY-058: /api/admin/me/* — admin self-service account management
    // (profile, sign-out-everywhere, sign-in history, GDPR self-delete).
    Cena.Admin.Api.AdminMeEndpoints.MapAdminMeEndpoints(app);

    // RDY-061: syllabus + student advancement endpoints
    Cena.Admin.Api.Syllabus.SyllabusEndpoints.MapSyllabusEndpoints(app);

    // RDY-063 Phase 2a: stuck-type diagnostics (admin aggregate reads)
    Cena.Admin.Api.Diagnostics.StuckDiagnosticsEndpoints.MapStuckDiagnosticsEndpoints(app);

    // RDY-057b: teacher classroom-level roll-up over onboarding self-assessment
    Cena.Admin.Api.SelfAssessmentRollup.SelfAssessmentRollupEndpoints
        .MapSelfAssessmentRollupEndpoints(app);

    // prr-150: teacher/mentor schedule-override POST endpoints
    //   POST /api/admin/teacher/override/pin-topic
    //   POST /api/admin/teacher/override/budget
    //   POST /api/admin/teacher/override/motivation
    // Tenant invariant enforced inside TeacherOverrideCommands (ADR-0001);
    // AdminActionAuditMiddleware captures every POST under /api/admin/**.
    app.MapScheduleOverrideEndpoints();

    // RDY-060: admin SignalR hub + health probe
    app.MapCenaAdminHub();
    
    // ---- Classroom endpoints (STB-00b) ----
    app.MapClassroomEndpoints();

    // prr-219: StudentPlan multi-target migration safety net
    //   POST /api/admin/institutes/{tenantId}/migrate-student-plan
    Cena.Admin.Api.Host.Endpoints.StudentPlanMigrationEndpoints
        .MapStudentPlanMigrationEndpoints(app);

    // prr-236: Classroom-assigned target teacher endpoint
    //   POST /api/admin/institutes/{instituteId}/classrooms/{classroomId}/assigned-targets
    Cena.Admin.Api.Host.Endpoints.ClassroomTargetEndpoints
        .MapClassroomTargetEndpoints(app);

    // PRR-391: one-click credit on confirmed photo-diagnostic dispute
    //   POST /api/admin/diagnostic-disputes/{disputeId}/credit
    Cena.Admin.Api.Host.Endpoints.DiagnosticCreditEndpoints
        .MapDiagnosticCreditEndpoints(app);
    
    // ---- Content management endpoints ----
    app.MapContentEndpoints();
    
    // ---- FERPA Compliance endpoints (FIND-arch-008) ----
    app.MapComplianceEndpoints();

    // ---- Test-only: e2e-flow LLM recorder query endpoint ----
    // Gated by `Cena:Testing:LlmRecorderEnabled` + Development + localhost.
    // See Cena.Admin.Api/LlmRecorderEndpoints.cs for the triple-gate rationale.
    app.MapLlmRecorderTestEndpoints(builder.Configuration, app.Environment);

    // prr-035: Sub-processor registry (read-only admin surface)
    //   GET /api/admin/privacy/sub-processors
    //   GET /api/admin/privacy/sub-processors/parent
    app.MapPrivacyEndpoints();

    // PRR-393: dispute-metrics read surface for the admin observability
    // dashboard. Admin-only auth policy; backed by the pure
    // DisputeRateAggregator + Marten-backed IDiagnosticDisputeRepository.
    //   GET /api/admin/dispute-metrics?window={7d|30d}
    Cena.Admin.Api.Host.Endpoints.DisputeMetricsEndpoints
        .MapDisputeMetricsEndpoints(app);

    // PRR-330: admin unit-economics history endpoint. Returns the last N
    // weekly rollup snapshots written by UnitEconomicsRollupWorker for
    // the admin dashboard's trend-line chart. The single-window (current-
    // week) view lives on the Student API host at
    // GET /api/admin/unit-economics; this history endpoint complements it.
    //   GET /api/admin/unit-economics/history?weeks=12
    Cena.Admin.Api.Host.Endpoints.UnitEconomicsAdminEndpoints
        .MapUnitEconomicsAdminEndpoints(app);

    // PRR-344: alpha-user migration operator endpoints.
    //   POST /api/admin/alpha-migration/seed     — overwrite the seed list
    //   GET  /api/admin/alpha-migration/status   — size + granted + pending
    //   POST /api/admin/alpha-migration/run-now  — fire the worker off-cron
    // Operators use these to promote pre-paywall alpha users into the
    // 60-day Premium grace window (ADR-0057 alpha-migration). The
    // StudentEntitlementResolver on every host honours the resulting
    // AlphaGraceMarker rows via IAlphaGraceMarkerReader (wired by
    // AddSubscriptionsMarten).
    Cena.Admin.Api.Host.Endpoints.AlphaMigrationEndpoints
        .MapAlphaMigrationEndpoints(app);

    // PRR-304 bank-transfer admin reconciliation: list Pending + confirm
    // payment received -> transition subscription to Active.
    Cena.Admin.Api.Host.Endpoints.BankTransferAdminEndpoints
        .MapBankTransferAdminEndpoints(app);

    // PRR-390 diagnostic-audit detail: support agent reads a single
    // disputed diagnostic's envelope metadata. Photo/CAS-chain/narration
    // fields are null until upstream capture writers land.
    Cena.Admin.Api.Host.Endpoints.DiagnosticAuditEndpoints
        .MapDiagnosticAuditEndpoints(app);

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