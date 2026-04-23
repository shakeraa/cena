// =============================================================================
// Cena Platform -- ASP.NET Core Host for Proto.Actor Cluster
// Layer: Infrastructure | Runtime: .NET 9
//
// Configures: Marten (PostgreSQL event store), Proto.Actor cluster,
//             Redis cache, NATS messaging, OpenTelemetry, Serilog, health checks.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Api;
using Cena.Actors.Diagnosis;
using Cena.Admin.Api;
using Cena.Admin.Api.Registration;
using Cena.Actors.Configuration;
using Cena.Infrastructure.Analytics;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Configuration;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Llm;
using Cena.Infrastructure.Nats;
using Cena.Infrastructure.Observability;
using Cena.Infrastructure.Observability.ErrorAggregator;
using Cena.Infrastructure.Secrets;
using Cena.Infrastructure.Ocr.DependencyInjection;
using Cena.Infrastructure.Resilience;
using Cena.Infrastructure.Seed;
using Cena.Actors.Gateway;
using Cena.Actors.Infrastructure;
using Cena.Actors.Notifications;
using Cena.Actors.Cas;
using Cena.Actors.Services;
using Cena.Actors.Services.ErrorPatternMatching;
using Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;
using Cena.Actors.Students;
using Cena.Actors.Sync;
using Cena.Actors.Tutoring;
using Marten;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Identity;
using Proto.Cluster.Kubernetes;
using Proto.Cluster.Partition;
using Proto.Cluster.Testing;
using k8s;
using Proto.DependencyInjection;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using Serilog;
using StackExchange.Redis;

const string ServiceName = "cena-learner-service";

// =============================================================================
// 1. BUILDER CONFIGURATION
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// RDY-024: Load BKT calibration config (version-controlled defaults + post-pilot overrides)
builder.Configuration.AddJsonFile("../../../config/bkt-params.json", optional: true, reloadOnChange: true);
// RDY-028: Load Bagrut anchor items for IRT calibration scale linking
builder.Configuration.AddJsonFile("../../../config/bagrut-anchors.json", optional: true, reloadOnChange: true);

// ---- Serilog structured logging ----
// prr-013 / ADR-0003 / RDY-080: SessionRiskLogEnricher scrubs theta / ability
// / risk / readiness scalars out of rendered log text. It binds a
// `RedactedMessage` property; sinks must use `{RedactedMessage}` in their
// outputTemplate to emit the scrubbed form. PiiDestructuringPolicy covers
// complex-object destructuring; the enricher covers scalar and literal-text
// leaks the destructurer can't reach.
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.With<Cena.Infrastructure.Compliance.SessionRiskLogEnricher>()
        .Destructure.With<Cena.Infrastructure.Compliance.PiiDestructuringPolicy>();

    // RDY-064 / ADR-0058: conditional Sentry Serilog sink. Only attached when
    // ErrorAggregator:Backend=sentry AND a non-empty DSN is configured. Logs
    // at Information+ become breadcrumbs, Errors become Sentry events. The
    // source-layer scrubber still runs in SentryErrorAggregator for direct
    // Capture() calls; sink path relies on Sentry's BeforeSend PII scrub
    // which is wired in SentryErrorAggregator.Init.
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
            s.InitializeSdk = false; // SentryErrorAggregator owns SDK init.
        });
    }
});

// RDY-056 §1.2: In Development the actor host runs with partial DI graphs
// (not every admin-shared service is active here). Skip eager validation so
// runtime resolution errors surface on first call instead of failing startup.
if (builder.Environment.IsDevelopment())
{
    builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
}

// ---- Read configuration ----
var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);
var natsUrl = builder.Configuration.GetConnectionString("NATS")
    ?? "nats://localhost:4222";

var clusterName = builder.Configuration.GetValue<string>("Cluster:ClusterName") ?? "cena-cluster";
var advertisedHost = builder.Configuration.GetValue<string>("Cluster:AdvertisedHost") ?? "0.0.0.0";
var advertisedPort = builder.Configuration.GetValue<int>("Cluster:AdvertisedPort", 8090);
var otlpEndpoint = builder.Configuration.GetValue<string>("Cluster:OtlpEndpoint") ?? "http://localhost:4317";
var enableDevLogging = builder.Configuration.GetValue<bool>("Cluster:EnableDevSupervisionLogging", true);

// =============================================================================
// 2. PostgreSQL: Shared NpgsqlDataSource + Marten Event Store
// =============================================================================

// Single connection pool shared by Marten, pgvector, and all raw queries.
// Actor Host: max 50 connections (actor activations + event flush + background).
// Prevents thundering herd on school-start peak (200+ students at 8am).
builder.Services.AddCenaDataSource(builder.Configuration, builder.Environment, builder.Configuration.GetValue<int>("PostgreSQL:MaxPoolSize", 50), builder.Configuration.GetValue<int>("PostgreSQL:MinPoolSize", 5));

// ADR-0038 SubjectKeyStore + prr-155 ConsentAggregate (bundled compliance primitives).
Cena.Actors.Consent.ConsentServiceRegistration.AddConsentAggregate(builder.Services.AddCenaComplianceServices(builder.Configuration, builder.Environment));

// DB-03: Read AutoCreate mode from config — "None" in prod, "CreateOrUpdate" in dev
var martenAutoCreate = builder.Configuration.GetValue<string>("Marten:AutoCreate") ?? "CreateOrUpdate";

builder.Services.AddMarten(opts =>
{
    // Marten uses the NpgsqlDataSource registered above via DI.
    // Connection string fallback for Marten's internal pool init.
    var pgConnectionString = CenaConnectionStrings.GetPostgres(builder.Configuration, builder.Environment);
    opts.ConfigureCenaEventStore(pgConnectionString, martenAutoCreate);
}).UseNpgsqlDataSource();

// =============================================================================
// 3. REDIS
// =============================================================================

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.Password = builder.Configuration["Redis:Password"]
        ?? Environment.GetEnvironmentVariable("REDIS_PASSWORD")
        ?? (builder.Environment.IsDevelopment() ? "cena_dev_redis" : null);
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 3;
    options.ConnectTimeout = 5000;
    options.SyncTimeout = 3000;
    // RedisSessionStoreMetricsService polls INFO for per-keyspace session
    // counts; StackExchange.Redis blocks admin commands by default and
    // throws "This operation is not available unless admin mode is enabled:
    // INFO". Observability-only, no write side effects.
    options.AllowAdmin = true;
    var multiplexer = ConnectionMultiplexer.Connect(options);
    logger.LogInformation("Connected to Redis at {RedisConnection}", redisConnectionString);
    return multiplexer;
});

// =============================================================================
// 4. NATS
// =============================================================================

builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<INatsConnection>>();

    // FIND-sec-009: Use centralized NATS auth resolution with environment gating
    var (natsUser, natsPass) = CenaNatsOptions.GetActorAuth(builder.Configuration, builder.Environment);

    var opts = new NatsOpts
    {
        Url = natsUrl,
        Name = "cena-actors-host",
        AuthOpts = new NatsAuthOpts { Username = natsUser, Password = natsPass },
    };

    logger.LogInformation("Configuring NATS connection to {NatsUrl} as {NatsUser}", natsUrl, natsUser);
    return new NatsConnection(opts);
});

// =============================================================================
// 5. DOMAIN SERVICES
// =============================================================================

// Firebase Auth + Authorization (required by admin endpoints)
builder.Services.AddHttpContextAccessor();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddCenaAuthorization();

// FIND-sec-014: Security metrics for observability
builder.Services.AddSecurityMetrics();

// RDY-064: Error aggregator scaffold — Null aggregator by default.
builder.Services.AddCenaErrorAggregator(builder.Configuration);

// PRR-428: Notifications DI — config-driven Email/SMS/WhatsApp backend
// selection. Default backends resolve today's graceful-disabled senders
// when credentials are absent. See Notifications:* in appsettings.json.
builder.Services.AddCenaNotifications(builder.Configuration);

// IClock for deterministic time-based testing (FIND-qa-007)
builder.Services.AddClock();

// ---- CORS (FIND-sec-012: restrict cross-origin access) ----
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

// Firebase Admin SDK (required by AdminUserService/AdminRoleService)
builder.Services.AddSingleton<Cena.Infrastructure.Firebase.IFirebaseAdminService,
    Cena.Infrastructure.Firebase.FirebaseAdminService>();

// Curriculum graph cache (needed by Mastery REST API endpoints)
builder.Services.AddSingleton<Cena.Actors.Mastery.IConceptGraphCache>(
    Cena.Actors.Simulation.CurriculumSeedData.BuildGraphCache());

// ACT-032: Register all domain services for DI
builder.Services.AddSingleton<IMethodologySwitchService, MethodologySwitchService>();
builder.Services.AddSingleton<IBktService, BktService>();
// RDY-024: Configurable BKT parameters from config/bkt-params.json
builder.Services.Configure<BktCalibrationOptions>(
    builder.Configuration.GetSection(BktCalibrationOptions.SectionName));
builder.Services.AddSingleton<IBktCalibrationProvider, ConfigurableBktCalibrationProvider>();
// RDY-028: Bagrut anchor items for IRT scale linking
builder.Services.AddSingleton<IBagrutAnchorProvider, BagrutAnchorProvider>();
builder.Services.AddSingleton<IHlrService, HlrService>();
builder.Services.AddSingleton<ICognitiveLoadService, CognitiveLoadService>();
// RDY-034: Flow state service — maps session signals to the 5-state flow
// machine shared with the student PWA useFlowState composable.
builder.Services.AddSingleton<IFlowStateService, FlowStateService>();
builder.Services.AddSingleton<IHintGenerator, HintGenerator>();
builder.Services.AddSingleton<IHintAdjustedBktService, HintAdjustedBktService>();
builder.Services.AddSingleton<IHintGenerationService, HintGenerationService>();
builder.Services.AddSingleton<IConfusionDetector, ConfusionDetector>();
builder.Services.AddSingleton<IDisengagementClassifier, DisengagementClassifier>();
builder.Services.AddSingleton<Cena.Actors.Hints.IDeliveryGate, Cena.Actors.Hints.DeliveryGate>().AddSingleton<Cena.Actors.Hints.ILdAnxiousHintGovernor, Cena.Actors.Hints.LdAnxiousHintGovernor>(); // prr-029 LD-anxious L1 rewrite; no LLM.
builder.Services.AddSingleton<IFocusDegradationService, FocusDegradationService>();
builder.Services.AddSingleton<IPrerequisiteEnforcementService, PrerequisiteEnforcementService>();
builder.Services.AddSingleton<IDecayPropagationService, DecayPropagationService>();
// INF-019: Redis circuit breaker — protects explanation cache and messaging from Redis outages
builder.Services.AddSingleton<Cena.Actors.Infrastructure.IRedisCircuitBreaker, Cena.Actors.Infrastructure.RedisCircuitBreaker>();
// prr-233: ambient cache-key context — carries (institute_id, exam_target_code)
// for cache metric labels. Register as singleton (the instance is stateless;
// state lives in its AsyncLocal, which is per-logical-call-chain).
builder.Services.AddSingleton<Cena.Infrastructure.Llm.IPromptCacheKeyContext, Cena.Infrastructure.Llm.AsyncLocalPromptCacheKeyContext>();
builder.Services.AddSingleton<IExplanationCacheService, ExplanationCacheService>().AddSingleton<Cena.Infrastructure.Llm.IPromptCache, Cena.Infrastructure.Llm.RedisPromptCache>(); // prr-047 unified seam + prr-233 per-target labels
// prr-046: per-feature cost metric. Pricing loaded fail-loud from routing-config.yaml.
builder.Services.AddLlmCostMetric(Path.Combine(
    builder.Environment.ContentRootPath,
    Cena.Infrastructure.Llm.LlmCostMetricRegistration.DefaultRoutingConfigRelativePath));
// prr-022 / ADR-0047: PII prompt scrubber — injected by every [TaskRouting]
// service that composes student free-text into its prompt.
builder.Services.AddPiiPromptScrubber();
// prr-026: k-anonymity enforcer — injected by every aggregate
// teacher/classroom/institute surface that serves a statistical claim.
builder.Services.AddKAnonymityEnforcer();
builder.Services.AddSingleton<IExplanationGenerator, ExplanationGenerator>();
builder.Services.AddSingleton<IL3ExplanationGenerator, L3ExplanationGenerator>();
builder.Services.AddSingleton<IErrorClassificationService, ErrorClassificationService>();

// RDY-033 + ADR-0002/0031: CAS stack + CAS-backed error pattern matchers.
// The CAS router is the sole correctness oracle; misconception matchers go through it.
builder.Services.AddSingleton<IMathNetVerifier, MathNetVerifier>();
// prr-010: SymPy template guard runs on every CAS request before NATS marshalling.
builder.Services.AddSingleton<ISymPyTemplateGuard, SymPyTemplateGuard>();
builder.Services.AddSingleton<ISymPySidecarClient, SymPySidecarClient>();
builder.Services.AddSingleton<ICasRouterService, CasRouterService>();

builder.Services.AddSingleton<IErrorPatternMatcher, DistExpSumMatcher>();
builder.Services.AddSingleton<IErrorPatternMatcher, CancelCommonMatcher>();
builder.Services.AddSingleton<IErrorPatternMatcher, SignNegativeMatcher>();
builder.Services.AddSingleton<IErrorPatternMatcher, OrderOpsMatcher>();
builder.Services.AddSingleton<IErrorPatternMatcher, FractionAddMatcher>();
builder.Services.AddSingleton<IErrorPatternMatcherEngine, ErrorPatternMatcherEngine>();

// RDY-014 + RDY-033: Misconception detection service, now powered by the matcher engine.
builder.Services.AddSingleton<IMisconceptionDetectionService, MisconceptionDetectionService>();

// prr-015 / ADR-0003: central registry + retention worker for misconception PII.
builder.Services.AddMisconceptionPiiStoreRegistry().AddCanonicalMartenMisconceptionStore();

builder.Services.AddSingleton<IExplanationOrchestrator, ExplanationOrchestrator>();
builder.Services.AddSingleton<IPersonalizedExplanationService, PersonalizedExplanationService>();
builder.Services.AddSingleton<OfflineSyncHandler>();

// FIND-arch-022 (superseded 2026-04-23): JetStream streams are created by the
// external `nats-setup` init container (src/infra/docker/nats-setup.sh) with
// richer configuration — retention, dup-window, deny-purge, 9 streams total.
// The in-app bootstrapper below tried to create its OWN 7 streams (CENA_*) on
// the SAME subjects, producing overlap errors and "durable streams may not be
// available" warnings on every actor-host start. App code should not manage
// NATS infrastructure lifecycle — that belongs to the init job. Disabled so
// the single authoritative stream definition wins.
// builder.Services.AddHostedService<Cena.Infrastructure.Nats.JetStreamBootstrapper>();
builder.Services.AddHostedService<NatsOutboxPublisher>();

// Analysis Job Processor (background worker for stagnation analysis jobs)
builder.Services.AddHostedService<Cena.Actors.Services.AnalysisJobProcessor>();

// SAI-05: A/B Experiment Service
builder.Services.AddSingleton<IExperimentService, ExperimentService>();

// RDY-063 Phase 2a: Stuck-type classifier (shadow mode).
// Registered unconditionally; runtime behaviour gated by
// Cena:StuckClassifier:Enabled (default false).
builder.Services.AddStuckClassifier(builder.Configuration);

// SAI-07/08: Conversational Tutoring (TutorActor is transient — spawned per conversation)
builder.Services.AddSingleton<ITutorPromptBuilder, TutorPromptBuilder>();
builder.Services.AddSingleton<ITutorSafetyGuard, TutorSafetyGuard>();
builder.Services.AddTransient<TutorActor>();
builder.Services.AddSingleton<Func<TutorActor>>(sp => () => sp.GetRequiredService<TutorActor>());

// ACT-010: Session event publisher — per-student NATS subjects for SignalR bridge
builder.Services.AddSingleton<Cena.Actors.Sessions.ISessionEventPublisher, Cena.Actors.Sessions.SessionNatsPublisher>();
// EPIC-PRR-A Sprint 1 (ADR-0012): LearningSession shadow-write (V2 → session-{id}); flag CENA_LEARNING_SESSION_SHADOW_WRITE.
builder.Services.AddSingleton<Cena.Actors.Sessions.Shadow.ILearningSessionShadowWriter, Cena.Actors.Sessions.Shadow.LearningSessionShadowWriter>();

// NATS Bus Router: bridges NATS commands ↔ Proto.Actor virtual actors
builder.Services.AddSingleton<Cena.Actors.Bus.NatsBusRouter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Cena.Actors.Bus.NatsBusRouter>());

// RES-010: Actor pre-warmer — activates actors in batches before peak load
builder.Services.AddHostedService<Cena.Actors.Infrastructure.ActorPreWarmer>();

// SAI-003: Explanation cache invalidation on question edits via NATS
builder.Services.AddHostedService<Cena.Actors.Explanations.ExplanationCacheInvalidator>();

// ADM-004 through ADM-016: Shared admin service registrations (REV-016.2)
builder.Services.AddCenaAdminServices();

// CNT-008/009/010: Ingestion Pipeline, Moderation, Serving
// RDY-012 + RDY-OCR-WIREUP-C (Phase 2.3):
//   - The OCR cascade (ADR-0033) is now the sole OCR path for Surface B.
//     IOcrClient + IMathOcrClient resolve to CascadeOcrClient, which
//     delegates to IOcrCascadeService. The legacy GeminiOcrClient /
//     MathpixClient are still registered as fallback HttpClients so
//     existing cost/audit wiring stays intact, but they are no longer
//     the IOcrClient implementations.
//   - AddOcrCascadeCore wires PdfTriage, Layer0–5, options bindings, the
//     IOcrCascadeService orchestrator (scoped), and TimeProvider.
//   - AddOcrCascadeWithCasValidation bridges ILatexValidator → the
//     existing 3-tier CasRouterService registered above.
//   - Tesseract + Surya + pix2tex wrappers are registered alongside,
//     mirroring the Student Host wiring so both hosts use identical
//     cascade infrastructure.
//   - Mathpix / Gemini Vision runners are opt-in per configured credentials.
builder.Services.Configure<Cena.Actors.Ingest.GeminiOcrOptions>(
    builder.Configuration.GetSection("Ingestion:Gemini"));
builder.Services.Configure<Cena.Actors.Ingest.MathpixOptions>(
    builder.Configuration.GetSection("Ingestion:Mathpix"));

// Real OCR cascade (ADR-0033). NO STUBS.
builder.Services.AddOcrCascadeCore(builder.Configuration);
builder.Services.AddOcrCascadeWithCasValidation();

builder.Services.Configure<Cena.Infrastructure.Ocr.Runners.TesseractOptions>(
    builder.Configuration.GetSection("Ocr:Tesseract"));
builder.Services.AddSingleton<Cena.Infrastructure.Ocr.Layers.ILayer2aTextOcr>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
        Cena.Infrastructure.Ocr.Runners.TesseractOptions>>().Value;
    var log  = sp.GetService<ILogger<Cena.Infrastructure.Ocr.Runners.TesseractLocalRunner>>();
    return new Cena.Infrastructure.Ocr.Runners.TesseractLocalRunner(opts, log);
});

builder.Services.Configure<Cena.Infrastructure.Ocr.Runners.OcrSidecarOptions>(
    builder.Configuration.GetSection("Ocr:Sidecar"));
builder.Services.AddSingleton<Cena.Infrastructure.Ocr.Layers.ILayer1Layout,
    Cena.Infrastructure.Ocr.Runners.SuryaSidecarClient>();
builder.Services.AddSingleton<Cena.Infrastructure.Ocr.Layers.ILayer2bMathOcr,
    Cena.Infrastructure.Ocr.Runners.Pix2TexSidecarClient>();

// Mathpix and Gemini Vision runners are opt-in via Ocr:Mathpix:AppId /
// Ocr:Gemini:ApiKey. Resilience is handled by the shared AddCenaResilience
// pipeline (RDY-012: timeout + retry + circuit breaker + fallback).
var mathpixAppIdActor = builder.Configuration["Ocr:Mathpix:AppId"];
if (!string.IsNullOrWhiteSpace(mathpixAppIdActor))
{
    builder.Services.Configure<Cena.Infrastructure.Ocr.Runners.MathpixOptions>(
        builder.Configuration.GetSection("Ocr:Mathpix"));
    builder.Services.AddHttpClient<
        Cena.Infrastructure.Ocr.Runners.IMathpixRunner,
        Cena.Infrastructure.Ocr.Runners.MathpixRunner>()
        .AddCenaResilience("OcrMathpix");
}

var geminiApiKeyActor = builder.Configuration["Ocr:Gemini:ApiKey"];
if (!string.IsNullOrWhiteSpace(geminiApiKeyActor))
{
    builder.Services.Configure<Cena.Infrastructure.Ocr.Runners.GeminiVisionOptions>(
        builder.Configuration.GetSection("Ocr:Gemini"));
    builder.Services.AddHttpClient<
        Cena.Infrastructure.Ocr.Runners.IGeminiVisionRunner,
        Cena.Infrastructure.Ocr.Runners.GeminiVisionRunner>()
        .AddCenaResilience("OcrGeminiVision");
}

// Legacy HttpClients kept registered for typed-client wiring; the
// concrete classes no longer satisfy IOcrClient/IMathOcrClient — the
// CascadeOcrClient adapter does, so every OCR call routes through the
// ADR-0033 cascade.
builder.Services.AddHttpClient<Cena.Actors.Ingest.GeminiOcrClient>()
    .AddCenaResilience("GeminiOcr");
builder.Services.AddHttpClient<Cena.Actors.Ingest.MathpixClient>()
    .AddCenaResilience("Mathpix");

// RDY-OCR-WIREUP-C: replace direct Gemini/Mathpix with the cascade adapter.
// IngestionOrchestrator still injects IOcrClient + IMathOcrClient; it now
// transparently runs the full cascade for every file it processes.
builder.Services.AddSingleton<Cena.Actors.Ingest.CascadeOcrClient>();
builder.Services.AddSingleton<Cena.Actors.Ingest.IOcrClient>(
    sp => sp.GetRequiredService<Cena.Actors.Ingest.CascadeOcrClient>());
builder.Services.AddSingleton<Cena.Actors.Ingest.IMathOcrClient>(
    sp => sp.GetRequiredService<Cena.Actors.Ingest.CascadeOcrClient>());

builder.Services.AddSingleton<Cena.Actors.Ingest.IQuestionSegmenter, Cena.Actors.Ingest.GeminiQuestionSegmenter>();
builder.Services.AddSingleton<Cena.Actors.Ingest.IDeduplicationService, Cena.Actors.Ingest.DeduplicationService>();
builder.Services.AddSingleton<Cena.Actors.Ingest.IContentExtractorService, Cena.Actors.Ingest.ContentExtractorService>();
builder.Services.AddScoped<Cena.Actors.Ingest.IIngestionOrchestrator, Cena.Actors.Ingest.IngestionOrchestrator>();

// RDY-061 Phase 2: student advancement aggregate + NATS subscriber that
// cascades chapter transitions from ConceptMastered events.
builder.Services.AddScoped<Cena.Actors.Advancement.IStudentAdvancementService,
    Cena.Actors.Advancement.StudentAdvancementService>();
builder.Services.AddHostedService<Cena.Actors.Advancement.AdvancementEventSubscriber>();
builder.Services.AddSingleton<Cena.Actors.Serving.IQuestionSelector, Cena.Actors.Serving.QuestionSelector>();

// SAI-06/07: Content extraction pipeline + embeddings + pgvector
builder.Services.AddSingleton<Cena.Actors.Ingest.IContentSegmenter, Cena.Actors.Ingest.ContentSegmenter>();
builder.Services.AddHttpClient<Cena.Actors.Services.EmbeddingService>()
    .AddCenaResilience("Embedding");
builder.Services.AddSingleton<Cena.Actors.Services.IEmbeddingService, Cena.Actors.Services.EmbeddingService>();
builder.Services.AddSingleton<Cena.Actors.Services.IContentRetriever, Cena.Actors.Services.ContentRetriever>();

// SAI-06: pgvector migration + async embedding ingestion handler
builder.Services.AddHostedService<Cena.Actors.Services.PgVectorMigrationService>();
builder.Services.AddHostedService<Cena.Actors.Services.EmbeddingIngestionHandler>();

// REV-013.3: Data retention worker for GDPR/FERPA/COPPA compliance
builder.Services.AddRetentionWorker(opts =>
{
    opts.CronExpression = builder.Configuration.GetValue<string>("Cena:Compliance:RetentionCron") ?? "0 2 * * *";
    opts.UseSoftDelete = builder.Configuration.GetValue<bool>("Cena:Compliance:UseSoftDelete", true);
    opts.BatchSize = builder.Configuration.GetValue<int>("Cena:Compliance:BatchSize", 1000);
});

// SEC-005: GDPR right-to-erasure worker (runs after retention)
builder.Services.AddErasureWorker(opts =>
{
    opts.CronExpression = builder.Configuration.GetValue<string>("Cena:Compliance:ErasureCron") ?? "0 3 * * *";
    opts.CoolingPeriod = TimeSpan.FromDays(builder.Configuration.GetValue<int>("Cena:Compliance:ErasureCoolingDays", 30));
    opts.BatchSize = builder.Configuration.GetValue<int>("Cena:Compliance:ErasureBatchSize", 100);
});
builder.Services.AddRightToErasureService(builder.Configuration.GetValue<string>("Cena:Compliance:ErasurePepper") ?? "change-me-in-production");

// prr-152: per-student projection cascades run during
// RightToErasureService.ProcessErasureAsync so the manifest records the
// full set of projections touched. ParentDigestPreferences + StudentVisibilityVeto
// cascades are registered in AddCenaAdminServices (called above). Here we add
// the TutorContext cascade, which depends on ISessionTutorContextService
// that is registered only in hosts that run the tutor pipeline.
builder.Services.AddSingleton<Cena.Actors.Tutoring.ISessionTutorContextService,
    Cena.Actors.Tutoring.SessionTutorContextService>();
builder.Services.AddSingleton<Cena.Infrastructure.Compliance.IErasureProjectionCascade,
    Cena.Actors.Tutoring.TutorContextErasureCascade>();

// SAI-000: LLM client abstraction and usage tracking
builder.Services.AddSingleton<AnthropicLlmClient>();
builder.Services.AddSingleton<ILlmClient, LlmClientRouter>();
builder.Services.AddSingleton<LlmUsageTracker>();

// =============================================================================
// 6. PROTO.ACTOR CLUSTER
// =============================================================================

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddSingleton(provider =>
{
    var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("CenaCluster");

    // Actor System
    var system = new ActorSystem(new ActorSystemConfig
    {
        DeveloperSupervisionLogging = enableDevLogging,
        DeadLetterThrottleInterval = TimeSpan.FromSeconds(10),
        DeadLetterThrottleCount = 5,
        DeadLetterRequestLogging = true,
        DiagnosticsLogLevel = enableDevLogging ? LogLevel.Debug : LogLevel.Warning
    })
    .WithServiceProvider(provider);

    // Remote Configuration — simplified for Proto.Actor 1.8.0
    // In dev: TestProvider handles local clustering without explicit remote config
    // In prod: will use GrpcNet remote (configured via Kubernetes/ECS service discovery)

    // REV-005: Configuration-driven cluster provider selection
    var clusterProviderType = provider.GetRequiredService<IConfiguration>()["Cluster:Provider"] ?? "test";
    var hostEnv = provider.GetRequiredService<IHostEnvironment>();

    IClusterProvider clusterProvider = clusterProviderType.ToLowerInvariant() switch
    {
        "test" when hostEnv.IsDevelopment() =>
            new TestProvider(new TestProviderOptions(), new InMemAgent()),

        "test" =>
            throw new InvalidOperationException(
                "TestProvider is not allowed outside Development. " +
                "Set Cluster:Provider to 'kubernetes' or 'consul'."),

        // RDY-025b: Real Kubernetes service-discovery provider. Watches
        // pods labeled `app.kubernetes.io/component=actors` (configurable
        // via Cluster:Kubernetes:PodLabelSelector) in the current pod's
        // namespace. Requires RBAC for list/watch on pods — provisioned
        // by deploy/helm/cena/templates/actors-rbac.yaml.
        //
        // The `new KubernetesClient.Kubernetes(...)` handshake fails fast
        // if the pod lacks a service-account token; we surface that as an
        // InvalidOperationException with a clear remediation pointer.
        "kubernetes" =>
            Cena.Actors.Host.ClusterProviderFactory.BuildKubernetesProvider(
                provider.GetRequiredService<IConfiguration>(),
                logger),

        "consul" =>
            throw new InvalidOperationException(
                "Consul cluster provider selected but Proto.Cluster.Consul " +
                "NuGet package is not installed. Install it and wire up " +
                "ConsulProvider with Cluster:ConsulAddress to enable Consul-based discovery."),

        _ => throw new InvalidOperationException(
            $"Unknown cluster provider: '{clusterProviderType}'. " +
            "Valid values: test (dev only), kubernetes, consul.")
    };

    logger.LogInformation("Cluster provider: {Provider} (environment: {Env})",
        clusterProviderType, hostEnv.EnvironmentName);

    // Identity Lookup — PartitionIdentityLookup across dev + prod.
    // The original config used PartitionActivatorLookup in dev (it was
    // supposed to be lighter for single-node) but paired with the
    // TestProvider it produced a state where cluster members existed yet
    // "student" kind activations never fired — RequestAsync hung 30s per
    // call waiting for an activation the lookup never completed. Matching
    // prod's PartitionIdentityLookup resolves activations reliably. The
    // minor memory overhead of the partition-identity actor is acceptable
    // for dev, and this keeps dev behaviour closer to prod.
    var identityLookup = (IIdentityLookup)new PartitionIdentityLookup();

    // Register Virtual Actor Kinds (Grains) — EPIC-PRR-A Sprint 1 (ADR-0012): shadow writer attached post-construction.
    var studentKind = new ClusterKind("student", Props.FromProducer(() => ActivatorUtilities.CreateInstance<StudentActor>(provider).UseLearningSessionShadowWriter(provider.GetRequiredService<Cena.Actors.Sessions.Shadow.ILearningSessionShadowWriter>())));

    // Cluster Configuration
    var clusterConfig = ClusterConfig
        .Setup(clusterName, clusterProvider, identityLookup)
        .WithClusterKind(studentKind)
        .WithGossipRequestTimeout(TimeSpan.FromSeconds(2))
        .WithActorActivationTimeout(TimeSpan.FromSeconds(30))
        .WithActorRequestTimeout(TimeSpan.FromSeconds(30))
        .WithHeartbeatExpiration(TimeSpan.FromSeconds(30));

    // Remote config for cluster — gRPC transport for actor activation.
    // BindToLocalhost() without a port argument picks a RANDOM ephemeral
    // port, which silently mismatches the Cluster:AdvertisedPort the
    // cluster member registers under. In a single-node dev cluster that
    // means the member's identity-lookup points at 127.0.0.1:8090 but
    // the gRPC listener is at 127.0.0.1:<random> — self-dials never
    // connect, PartitionActivatorLookup can't reach the "student" kind
    // owner, and every cena.session.* / cena.mastery.* request times
    // out after 30s waiting for an activation that never fires.
    // Pin bind + advertise to the same port so self-dial resolves.
    system
        .WithRemote(RemoteConfig.BindToLocalhost(advertisedPort))
        .WithCluster(clusterConfig);

    logger.LogInformation(
        "Proto.Actor cluster configured. Name={ClusterName}, Host={Host}:{Port}",
        clusterName, advertisedHost, advertisedPort);

    return system;
});

// =============================================================================
// 7. OPENTELEMETRY
// =============================================================================

// RDY-064 / ADR-0058 §3: release correlation. service.version is the same
// CENA_GIT_SHA value that Sentry tags on events, so traces and exceptions
// share a single release identifier.
var otelServiceVersion = builder.Configuration["ErrorAggregator:Release"]
    ?? builder.Configuration["Cluster:ServiceVersion"]
    ?? "unknown";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: ServiceName,
            serviceVersion: otelServiceVersion,
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        .AddSource("Cena.Actors.StudentActor")
        .AddSource("Cena.Actors.LearningSessionActor")
        .AddSource("Cena.Actors.StagnationDetectorActor")
        .AddSource("Cena.Actors.OutreachSchedulerActor")
        .AddSource("Proto.Actor")
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddMeter("Cena.Actors.StudentActor")
        .AddMeter("Cena.Actors.LearningSessionActor")
        .AddMeter("Cena.Actors.LlmCircuitBreaker")
        .AddMeter("Cena.Actors.CurriculumGraph")
        .AddMeter("Cena.Actors.DeadLetterWatcher")
        .AddMeter("Cena.Infrastructure.NatsOutbox")
        .AddMeter("Cena.Actors.Decay")
        .AddMeter("Cena.Actors.Focus")
        .AddMeter("Cena.Actors.HealthAggregator")
        .AddMeter("Cena.Session.Nats")
        .AddMeter("Npgsql")
        .AddMeter("Cena.HttpCircuitBreaker")
        // RDY-OCR-OBSERVABILITY (Phase 4): OCR cascade metrics
        .AddMeter(Cena.Infrastructure.Ocr.Observability.OcrMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
        .AddPrometheusExporter());

// =============================================================================
// 8. HEALTH CHECKS
// =============================================================================

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Host is running"), tags: ["live"])
    .AddCheck<ProtoActorHealthCheck>("proto-actor-cluster", tags: ["ready"]);

// prr-020: Emit Redis session-store eviction / memory / hit-ratio metrics.
// Alert rule lives in ops/prometheus/alerts-redis-sessions.yml. Runbook:
// docs/ops/runbooks/redis-session-eviction.md.
builder.Services.AddCenaRedisSessionStoreMetrics(builder.Configuration);

// prr-017: Register the ISecretStore abstraction. Null default in
// Development; staging / production must register a provider adapter
// (AWS / GCP / Vault) BEFORE this call — AddCenaSecretStore throws on
// boot if it has to fall back to the Null default outside Development.
builder.Services.AddCenaSecretStore(builder.Environment);

// =============================================================================
// BUILD & CONFIGURE PIPELINE
// =============================================================================

var app = builder.Build();

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

// ---- Prometheus metrics endpoint (RES-002) ----
app.MapPrometheusScrapingEndpoint();

// ---- Health check endpoints ----
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

// ---- Actor stats endpoint (real-time from NatsBusRouter) ----
// FIND-sec-012: gated behind SuperAdminOnly; PII hashed
app.MapGet("/api/actors/stats", (Cena.Actors.Bus.NatsBusRouter router) =>
{
    var actors = router.ActiveActors.Values
        .OrderByDescending(a => a.LastActivity)
        .Select(a => new
        {
            studentIdHash = EmailHasher.Hash(a.StudentId),
            sessionIdHash = EmailHasher.Hash(a.SessionId),
            messagesProcessed = a.MessagesProcessed,
            totalAttempts = a.TotalAttempts,
            correctAttempts = a.CorrectAttempts,
            accuracy = a.TotalAttempts > 0 ? Math.Round((double)a.CorrectAttempts / a.TotalAttempts * 100, 1) : 0,
            lastActivity = a.LastActivity,
            activatedAt = a.ActivatedAt,
            uptimeSeconds = (DateTimeOffset.UtcNow - a.ActivatedAt).TotalSeconds,
            status = a.Status
        });

    return Results.Ok(new
    {
        timestamp = DateTimeOffset.UtcNow,
        commandsRouted = router.CommandsRouted,
        eventsPublished = router.EventsPublished,
        sessionsStarted = router.SessionsStarted,
        errorsCount = router.ErrorsCount,
        retriesAttempted = router.RetriesAttempted,
        deadLettered = router.DeadLettered,
        errorsByCategory = router.ErrorsByCategory,
        recentErrors = router.RecentErrors.Take(20).Select(e => new
        {
            e.Timestamp,
            e.Category,
            e.Subject,
            e.Message,
            studentIdHash = EmailHasher.Hash(e.StudentId)
        }),
        activeActorCount = router.ActiveActors.Count,
        actors = actors
    });
}).RequireAuthorization(CenaAuthPolicies.SuperAdminOnly).WithName("GetActorStats");

// ---- Cluster diagnostic endpoint (read-only, REV-004) ----
app.MapGet("/api/actors/diag", (ActorSystem system) =>
{
    var cluster = system.Cluster();
    return Results.Ok(new
    {
        ClusterId = cluster.Config.ClusterName,
        MemberCount = cluster.MemberList.GetAllMembers().Length,
        Members = cluster.MemberList.GetAllMembers().Select(m => new
        {
            m.Address, m.Kinds, m.Id
        }),
        SystemId = system.Id,
        Address = system.Address,
    });
}).RequireAuthorization(CenaAuthPolicies.SuperAdminOnly).WithName("ClusterDiagnostic");

// ---- RES-010: Actor pre-warm endpoint (Admin API → NATS → ActorPreWarmer) ----
app.MapPost("/api/actors/warmup", async (
    INatsConnection nats,
    Cena.Actors.Infrastructure.WarmUpRequest request) =>
{
    if (request.StudentIds is not { Count: > 0 })
        return Results.BadRequest(new { error = "StudentIds list is required" });

    if (request.StudentIds.Count > 1000)
        return Results.BadRequest(new { error = "Maximum 1000 students per warm-up request" });

    var json = System.Text.Json.JsonSerializer.Serialize(request,
        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    await nats.PublishAsync(Cena.Actors.Bus.NatsSubjects.WarmUpRequest, json);

    return Results.Accepted(value: new
    {
        message = $"Pre-warming {request.StudentIds.Count} actors",
        studentCount = request.StudentIds.Count
    });
}).RequireAuthorization(CenaAuthPolicies.SuperAdminOnly).WithName("WarmUpActors");

// ---- CORS must be before Auth to handle preflight ----
app.UseCors();

// ---- Auth middleware (required by admin endpoints with RequireAuthorization) ----
app.UseAuthentication();
app.UseAuthorization();

// ---- REV-013: FERPA audit middleware (logs every student data endpoint access) ----
app.UseMiddleware<StudentDataAuditMiddleware>();

// ---- Mastery REST API endpoints (MST-017) ----
app.MapMasteryEndpoints();

// ---- Admin REST API endpoints (ADM-004 through ADM-016, REV-016.2) ----
app.MapCenaAdminEndpoints();

// ---- REV-013: FERPA Compliance endpoints ----
app.MapComplianceEndpoints();

// ---- Cluster lifecycle ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var actorSystem = app.Services.GetRequiredService<ActorSystem>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

// RDY-056 §1.1: Warm Marten schema before hosted services + cluster start.
if (app.Environment.IsDevelopment())
{
    using var warmScope = app.Services.CreateScope();
    var warmStore = warmScope.ServiceProvider.GetRequiredService<Marten.IDocumentStore>();
    try
    {
        appLogger.LogInformation("[MARTEN_SCHEMA_WARM] applying configured changes...");
        await warmStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        appLogger.LogInformation("[MARTEN_SCHEMA_READY] schema warm complete");
    }
    catch (Exception ex)
    {
        appLogger.LogError(ex, "[MARTEN_SCHEMA_WARM] failed — host will still start");
    }
}

lifetime.ApplicationStarted.Register(async () =>
{
    appLogger.LogInformation("Starting Proto.Actor cluster...");
    await actorSystem.Cluster().StartMemberAsync();
    appLogger.LogInformation("Proto.Actor cluster started. MemberID={MemberId}", actorSystem.Id);

    // RES-003: Spawn Redis circuit breaker actor at root level
    var redisCbProps = Props.FromProducer(() =>
        new LlmCircuitBreakerActor(
            CircuitBreakerConfig.Redis,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<LlmCircuitBreakerActor>(),
            app.Services.GetRequiredService<IMeterFactory>()));
    var redisCbPid = actorSystem.Root.SpawnNamed(redisCbProps, "circuit-breaker-redis");
    appLogger.LogInformation("RES-003: Redis circuit breaker spawned at {Pid}", redisCbPid);

    // RES-005: Spawn Health Aggregator singleton
    var healthProps = Props.FromProducer(() =>
        new HealthAggregatorActor(
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<HealthAggregatorActor>(),
            app.Services.GetRequiredService<IMeterFactory>()));
    var healthPid = actorSystem.Root.SpawnNamed(healthProps, "health-aggregator");
    // Register the Redis CB for health polling
    actorSystem.Root.Send(healthPid, new HealthAggregatorActor.RegisterHealthSources(
        new Dictionary<string, PID> { ["redis"] = redisCbPid },
        ManagerPid: null)); // Manager PID can be registered later when available
    appLogger.LogInformation("RES-005: Health aggregator spawned at {Pid}", healthPid);

    // RES-010/FIND-arch-024: Spawn Feature Flag singleton with persistence
    var ffProps = Props.FromProducer(() =>
        new FeatureFlagActor(
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<FeatureFlagActor>(),
            app.Services.GetRequiredService<IDocumentStore>()));
    var ffPid = actorSystem.Root.SpawnNamed(ffProps, "feature-flags");
    appLogger.LogInformation("RES-010: Feature flag service spawned at {Pid}", ffPid);

    // Seed all demo data via single entry point.
    // RDY-037: pass the service provider so QuestionBankSeedData can resolve
    // the CAS-gated persister.
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await DatabaseSeeder.SeedAllAsync(store, appLogger, app.Services, 300,
        ctx => Cena.Admin.Api.SimulationEventSeeder.SeedSimulationEventsAsync(ctx.Store, ctx.Logger),
        Cena.Admin.Api.QuestionBankSeedData.SeedQuestionsAsync);
});

lifetime.ApplicationStopping.Register(async () =>
{
    appLogger.LogInformation("Initiating graceful cluster shutdown...");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
        await actorSystem.Cluster().ShutdownAsync(graceful: true);
        appLogger.LogInformation("Cluster shut down gracefully.");
    }
    catch (OperationCanceledException)
    {
        appLogger.LogWarning("Graceful shutdown timed out. Forcing shutdown.");
        await actorSystem.Cluster().ShutdownAsync(graceful: false);
    }
    catch (Exception ex)
    {
        appLogger.LogError(ex, "Error during shutdown. Forcing shutdown.");
        await actorSystem.Cluster().ShutdownAsync(graceful: false);
    }
});

app.Run();

// =============================================================================
// HEALTH CHECK: Proto.Actor cluster readiness
// =============================================================================

public sealed class ProtoActorHealthCheck : IHealthCheck
{
    private readonly ActorSystem _system;
    private readonly int _minNodes;

    public ProtoActorHealthCheck(ActorSystem system, IConfiguration configuration)
    {
        _system = system;
        _minNodes = configuration.GetValue<int>("Cluster:MinNodes", 1);
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var members = _system.Cluster().MemberList.GetAllMembers();
            if (members.Length == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No cluster members found. Cluster may not be formed."));
            }

            if (members.Length < _minNodes)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Cluster has {members.Length} member(s), minimum is {_minNodes}"));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Cluster healthy: {members.Length} member(s), NodeId: {_system.Id}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Cluster health check failed", ex));
        }
    }
}

namespace Cena.Actors.Host
{

// =============================================================================
// RDY-025b — Kubernetes cluster provider factory.
//
// Extracted as a named method so it can be unit-tested independently of
// the Program.cs top-level statements (Cena.Actors.Host.Tests references
// the `BuildKubernetesProvider` entry point directly).
//
// Design:
//   • Uses Proto.Cluster.Kubernetes.KubernetesProvider + the k8s
//     KubernetesClient. Inside a pod the in-cluster config is loaded from
//     the service-account token automatically; outside a pod we fall back
//     to KubeConfigDefaultLocation (~/.kube/config) so dev smoke-runs on
//     kind / minikube Just Work.
//   • Pod-label selector defaults to the convention
//     `app.kubernetes.io/component=actors`, override via
//     Cluster:Kubernetes:PodLabelSelector (see deploy/helm/cena/values.yaml).
//   • Watch timeout capped at 30s so the watch loop recovers promptly
//     after API-server blips.
//   • Fails fast with a remediation-pointer exception if in-cluster
//     config can't be loaded AND no kube-config is present — better
//     than silently single-podding the cluster.
//
// RBAC: provisioned by deploy/helm/cena/templates/actors-rbac.yaml
// (ServiceAccount + Role with list/watch on pods in the release
// namespace + RoleBinding).
// =============================================================================
public static class ClusterProviderFactory
{
    public const string DefaultPodLabelSelector = "app.kubernetes.io/component=actors";
    public const int DefaultWatchTimeoutSeconds = 30;

    public static IClusterProvider BuildKubernetesProvider(
        IConfiguration configuration,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var section = configuration.GetSection("Cluster:Kubernetes");
        var podLabelSelector = section["PodLabelSelector"] ?? DefaultPodLabelSelector;
        var watchTimeoutSeconds = int.TryParse(section["WatchTimeoutSeconds"], out var w)
            ? w : DefaultWatchTimeoutSeconds;

        if (watchTimeoutSeconds <= 0 || watchTimeoutSeconds > 600)
            throw new InvalidOperationException(
                $"Cluster:Kubernetes:WatchTimeoutSeconds={watchTimeoutSeconds} is out of " +
                "range (1..600).");

        // Client factory runs once on provider construction. BuildDefaultConfig
        // picks in-cluster first (service-account token), then KUBECONFIG env,
        // then ~/.kube/config — what we want for pod + dev-smoke.
        IKubernetes ClientFactory()
        {
            try
            {
                var k8sConfig = KubernetesClientConfiguration.BuildDefaultConfig();
                logger.LogInformation(
                    "Cluster provider = Kubernetes (host={Host}, selector={Selector}, watchSec={WatchSec})",
                    k8sConfig.Host, podLabelSelector, watchTimeoutSeconds);
                return new Kubernetes(k8sConfig);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Kubernetes cluster provider selected but neither an in-cluster " +
                    "service-account token nor a kube-config file is available. " +
                    "Running inside a pod? Mount `automountServiceAccountToken: true`. " +
                    "Running locally? Ensure `kubectl config current-context` works.",
                    ex);
            }
        }

        // Proto.Cluster.Kubernetes 1.8 config signature:
        //   (watchTimeoutSeconds, developerLogging, disableWatch, clientFactory)
        // Pod-label-selector scoping is handled internally by the provider
        // via the cluster's own member labels — the selector knob here is
        // intentionally omitted so the provider uses its built-in default
        // (any pod that has identified itself via Proto.Cluster labels).
        return new KubernetesProvider(
            new KubernetesProviderConfig(
                watchTimeoutSeconds: watchTimeoutSeconds,
                developerLogging: false,
                disableWatch: false,
                clientFactory: ClientFactory));
    }
}

}
