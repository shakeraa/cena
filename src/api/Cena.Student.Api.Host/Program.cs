// =============================================================================
// Cena Platform — Student API Host (DB-06b)
// Student-facing REST endpoints + SignalR real-time tutoring hub.
// Migrated from Cena.Api.Host — see README.md for migration notes.
// =============================================================================

using System.Security.Claims;
using System.Threading.RateLimiting;
using Cena.Actors.Bus;
using Cena.Actors.Configuration;
using Cena.Actors.Diagnosis;
using Cena.Actors.Notifications;
using Cena.Actors.Mastery;
using Cena.Actors.Retention;
using Cena.Actors.RateLimit;
using Cena.Actors.Services;
using Cena.Actors.Serving;
using Cena.Actors.StudentPlan;
using Cena.Actors.Tutor;
using Cena.Api.Host.Endpoints;
using Cena.Api.Host.Hubs;
using Cena.Api.Host.Services;
using Cena.Student.Api.Host.Endpoints;
using Cena.Infrastructure.Ai;
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
using Cena.Infrastructure.Moderation;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.DependencyInjection;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.Runners;
using Cena.Actors.Cas;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Cena.Infrastructure.Seed;
using Marten;
using Polly;
using Microsoft.AspNetCore.RateLimiting;
using NATS.Client.Core;
using NatsEventSubscriber = Cena.Admin.Api.NatsEventSubscriber;
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


// RDY-056 §1.1: Warm Marten schema before hosted services start (see Admin Host).
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
        warmLogger.LogError(ex, "[MARTEN_SCHEMA_WARM] failed — host will still start");
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
    });
    
    // ---- Configuration ----
    var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);
    
    // ---- PostgreSQL: Shared NpgsqlDataSource + Marten ----
    var pgMaxPool = builder.Configuration.GetValue<int>("PostgreSQL:MaxPoolSize", 50);
    var pgMinPool = builder.Configuration.GetValue<int>("PostgreSQL:MinPoolSize", 5);
    builder.Services.AddCenaDataSource(builder.Configuration, builder.Environment, pgMaxPool, pgMinPool);

    // ADR-0038 key store + prr-155 ConsentAggregate (bundled compliance services).
    Cena.Actors.Consent.ConsentServiceRegistration.AddConsentAggregate(builder.Services.AddCenaComplianceServices(builder.Configuration, builder.Environment));

    // prr-148: StudentPlan bounded context (new, per NoNewStudentActorStateTest).
    builder.Services.AddStudentPlanServices();

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
    
    // ---- AI Token Budget (FIND-sec-015) ----
    builder.Services.AddAiTokenBudget();
    
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
    
    // ---- PWA-BE-002: Web Push dispatch + rate limiting ----
    builder.Services.AddSingleton<IPushNotificationRateLimiter, PushNotificationRateLimiter>();
    // RDY-056 §1.3: WebPushDispatchService depends on IWebPushClient.
    // Singleton: WebPushClient holds one PushServiceClient + VAPID auth.
    builder.Services.AddSingleton<IWebPushClient, WebPushClient>();
    builder.Services.AddScoped<IWebPushDispatchService, WebPushDispatchService>();
    builder.Services.AddHostedService<PushNotificationTriggerService>();
    
    // ---- RATE-001: Distributed rate limiting + cost circuit breaker ----
    builder.Services.AddSingleton<IRateLimitService, RedisRateLimitService>();
    builder.Services.AddSingleton<ICostBudgetService, RedisCostBudgetService>();
    builder.Services.AddSingleton<ICostCircuitBreaker, RedisCostCircuitBreaker>();
    
    // ---- HARDEN SessionEndpoints: Question Bank Service ----
    // ---- FIND-pedagogy-016: Adaptive Question Pool for REST session seeding ----
    // AdaptiveQuestionPool needs IQuestionSelector (stateless, singleton-safe) and
    // IDocumentStore (already registered above). MartenQuestionPool is loaded
    // per-session in SessionEndpoints because it is parameterized by subjects.
    builder.Services.AddSingleton<IQuestionSelector, QuestionSelector>();
    builder.Services.AddScoped<IAdaptiveQuestionPool, AdaptiveQuestionPool>();
    
    builder.Services.AddScoped<IQuestionBank, QuestionBank>();
    
    // ---- SAI-002: Hint Generation (stateless pure function) ----
    builder.Services.AddSingleton<IHintGenerator, HintGenerator>();

    // ---- RDY-063 Phase 2a: Stuck-type classifier (shadow mode) ----
    // Registered unconditionally; the feature flag
    // Cena:StuckClassifier:Enabled (default false) gates runtime
    // behaviour. When disabled, HintStuckShadowService returns
    // Task.CompletedTask immediately — zero-cost no-op.
    builder.Services.AddStuckClassifier(builder.Configuration);
    
    // ---- MST-011: Scaffolding Service (stateless pure function wrapper) ----
    builder.Services.AddSingleton<IScaffoldingService, ScaffoldingServiceWrapper>();

    // ---- prr-149: AdaptiveScheduler live caller at session start ----
    // Every POST /api/sessions/start asks ISessionPlanGenerator for a
    // SessionPlanSnapshot, persists it via ISessionPlanWriter (session-
    // scoped event + read doc), and notifies the student via SignalR.
    // Failure is observability-only — the session continues.
    //
    // Bridge to prr-148: StudentPlanConfigBridgeService reads the raw
    // student-set deadline+budget via IStudentPlanInputsService
    // (registered by AddStudentPlanServices) and folds in scheduler
    // defaults for any null fields.
    builder.Services.AddStudentPlanServices();
    builder.Services.AddSingleton<
        Cena.Actors.Sessions.IStudentPlanConfigService,
        Cena.Actors.Sessions.StudentPlanConfigBridgeService>();
    builder.Services.AddScoped<
        Cena.Actors.Sessions.ISessionAbilityEstimateProvider,
        StudentProfileAbilityEstimateProvider>();
    builder.Services.AddSingleton<
        Cena.Actors.Sessions.ITopicPrerequisiteGraphProvider>(
            _ => Cena.Actors.Sessions.EmptyTopicPrerequisiteGraphProvider.Instance);
    builder.Services.AddScoped<
        Cena.Actors.Sessions.ISessionPlanGenerator,
        Cena.Actors.Sessions.SessionPlanGenerator>();
    builder.Services.AddScoped<
        Cena.Actors.Sessions.ISessionPlanWriter,
        Cena.Actors.Sessions.SessionPlanWriter>();
    builder.Services.AddSingleton<
        Cena.Actors.Sessions.ISessionPlanNotifier,
        Cena.Api.Host.Hubs.SignalRSessionPlanNotifier>();

    // ---- PRR-151 R-22: AccommodationProfile service (session-render wiring) ----
    // Wires the Accommodations bounded context into the session pipeline.
    // Before this registration, AccommodationProfileAssignedV1 events were
    // persisted but no session-time code consulted them — a Ministry-
    // reportable compliance defect ("we grant TTS accommodation for
    // dyslexic students in the consent log, but TTS never turns on in
    // practice"). The service folds the latest event on the student
    // stream into an AccommodationProfile so SessionEndpoints can set
    // the TTS / extended-time / distraction-reduced flags on the
    // SessionQuestionDto. See
    // src/actors/Cena.Actors/Accommodations/IAccommodationProfileService.cs.
    builder.Services.AddSingleton<
        Cena.Actors.Accommodations.IAccommodationProfileService,
        Cena.Actors.Accommodations.MartenAccommodationProfileService>();

    // prr-029: LD-anxious hint governor (L1 worked-step template; no LLM).
    builder.Services.AddSingleton<Cena.Actors.Hints.ILdAnxiousHintGovernor, Cena.Actors.Hints.LdAnxiousHintGovernor>();

    // prr-203: hint-ladder orchestrator (L1 template / L2 Haiku / L3 Sonnet)
    // per ADR-0045. L1 is no-LLM (template); L2 and L3 carry [TaskRouting]
    // on their respective generators. Consumed by HintLadderEndpoint —
    // the new POST .../hint/next route.
    Cena.Actors.Hints.HintLadderRegistration.AddHintLadder(builder.Services);

    // prr-204: SessionTutorContextService — session-scoped tutor context cache
    // that the Sidekick drawer + hint-ladder consumers read via
    // GET /api/v1/sessions/{sid}/tutor-context. Redis session-TTL cache
    // backed by a live Marten fallback; strictly session-scoped per ADR-0003
    // (misconception tag lives here, never on the student profile). The
    // architecture test NoTutorContextPersistenceTest locks the scope
    // boundary at test-time.
    builder.Services.AddSingleton<
        Cena.Actors.Tutoring.ISessionTutorContextService,
        Cena.Actors.Tutoring.SessionTutorContextService>();
    // prr-152: TutorContext cache invalidation for the erasure cascade.
    // Registered alongside the service so the Student host's erasure
    // worker picks up the cascade via IEnumerable<IErasureProjectionCascade>.
    builder.Services.AddSingleton<Cena.Infrastructure.Compliance.IErasureProjectionCascade,
        Cena.Actors.Tutoring.TutorContextErasureCascade>();

    // ---- RDY-034: Flow state service (consumes ICognitiveLoadService) ----
    // Registered in both actor + student hosts so the assessment endpoint
    // and any in-process actor consumer resolve the same implementation.
    builder.Services.AddSingleton<ICognitiveLoadService, CognitiveLoadService>();
    builder.Services.AddSingleton<IFlowStateService, FlowStateService>();

    // ---- prr-154: If-then implementation-intentions planner (F2) ----
    // Stateless domain service; singleton is fine. Consumers: session-plan
    // endpoints + a later student-facing UI task.
    builder.Services.AddSingleton<
        Cena.Actors.Pedagogy.IIfThenPlanner,
        Cena.Actors.Pedagogy.IfThenPlanner>();

    // ---- prr-159: Peer-confused signal service (F5) ----
    // Session-scoped anonymous "I'm confused too" aggregator with
    // k-anonymity floor. Scoped because it uses IDocumentStore.LightweightSession
    // per emission and we want one logger scope per request.
    builder.Services.AddScoped<
        Cena.Actors.Sessions.IPeerConfusedSignalService,
        Cena.Actors.Sessions.PeerConfusedSignalService>();

    // ---- FIND-pedagogy-003: Real BKT posterior for session answer endpoint ----
    // BktService is stateless, allocation-free on the hot path, and cheap to
    // instantiate — singleton is fine. The student API host used to compute
    // posterior mastery as PriorMastery + 0.05, which bypassed BKT entirely.
    builder.Services.AddSingleton<IBktService, BktService>();

    // ---- prr-228: Per-target diagnostic engine (ADR-0050, EPIC-PRR-F) ----
    // The engine folds per-target diagnostic responses into skill-keyed
    // BKT priors (StudentId, ExamTargetCode, SkillCode). Singleton for
    // allocation efficiency; internal state is only the injected tracker.
    //
    // Register IBktStateTracker + ISkillKeyedMasteryStore via the shared
    // prr-222 wiring (idempotent TryAdd — no-op if another layer already
    // wired it).
    // prr-229: IClock required by ExamTargetRetentionWorker's age calcs.
    // Student API host didn't previously register one; actor-host does via AddClock().
    Cena.Actors.Infrastructure.ClockRegistration.AddClock(builder.Services);
    builder.Services.AddExamTargetRetentionServices();
    builder.Services.AddSingleton<Cena.Actors.Diagnosis.PerTarget.PerTargetDiagnosticEngine>();
    
    // ---- FIND-pedagogy-009: Elo difficulty calibration for 85% rule ----
    // Updates question DifficultyElo after each answer using Elo formula.
    // Wilson et al. (2019): "The Eighty Five Percent Rule for optimal learning."
    builder.Services.AddScoped<IEloDifficultyService, EloDifficultyService>();
    
    // ---- RDY-033b / RDY-033c: Error classification + CAS-backed pattern matching ----
    //
    // SessionEndpoints.POST /answer injects IErrorClassificationService and
    // IMisconceptionDetectionService via [FromServices]. The Actor Host
    // registers these for its own pipeline; the Student API Host runs in a
    // separate DI container, so the same bindings are required here, or the
    // /answer endpoint throws "Unable to resolve service" at runtime.
    //
    // RDY-033c — ErrorClassificationService's ctor takes ILlmClient. Without
    // the two registrations below, DI could not construct it and we'd still
    // throw at runtime despite the RDY-033b service binding. Mirrors the
    // Actor Host's SAI-000 registrations. AnthropicLlmClient takes only
    // IConfiguration (built-in) and logs a warning if the API key is missing
    // so DI resolution always succeeds — runtime failure is bounded to the
    // LLM call itself, which ClassifyAsync already catches.
    //
    // CAS-grounded matchers (ADR-0031) back the misconception detector: each
    // buggy rule is an IErrorPatternMatcher that goes through ICasRouterService
    // (ADR-0002). Matchers short-circuit by subject, so registering the full
    // set here has near-zero cost on non-math/physics paths.
    builder.Services.AddSingleton<Cena.Actors.Gateway.AnthropicLlmClient>();
    builder.Services.AddSingleton<Cena.Actors.Gateway.ILlmClient, Cena.Actors.Gateway.LlmClientRouter>();

    // prr-046: per-feature LLM cost metric. Fail-loud pricing from routing-config.yaml.
    builder.Services.AddLlmCostMetric(Path.Combine(
        builder.Environment.ContentRootPath,
        Cena.Infrastructure.Llm.LlmCostMetricRegistration.DefaultRoutingConfigRelativePath));
    // prr-022 / ADR-0047: PII prompt scrubber — injected by every [TaskRouting]
    // service that composes student free-text into its prompt.
    builder.Services.AddPiiPromptScrubber();
    // prr-026: k-anonymity enforcer — injected by every aggregate
    // teacher/classroom/institute surface that serves a statistical claim.
    builder.Services.AddKAnonymityEnforcer();

    builder.Services.AddSingleton<IErrorClassificationService, ErrorClassificationService>();

    builder.Services.AddSingleton<Cena.Actors.Cas.IMathNetVerifier, Cena.Actors.Cas.MathNetVerifier>();
    // prr-010: SymPy template guard runs on every CAS request before NATS marshalling.
    builder.Services.AddSingleton<Cena.Actors.Cas.ISymPyTemplateGuard, Cena.Actors.Cas.SymPyTemplateGuard>(); builder.Services.AddSingleton<Cena.Actors.Cas.ISymPySidecarClient, Cena.Actors.Cas.SymPySidecarClient>();
    builder.Services.AddSingleton<Cena.Actors.Cas.ICasRouterService, Cena.Actors.Cas.CasRouterService>();

    builder.Services.AddSingleton<Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcher,
        Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers.DistExpSumMatcher>();
    builder.Services.AddSingleton<Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcher,
        Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers.CancelCommonMatcher>();
    builder.Services.AddSingleton<Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcher,
        Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers.SignNegativeMatcher>();
    builder.Services.AddSingleton<Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcher,
        Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers.OrderOpsMatcher>();
    builder.Services.AddSingleton<Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcher,
        Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers.FractionAddMatcher>();
    builder.Services.AddSingleton<Cena.Actors.Services.ErrorPatternMatching.IErrorPatternMatcherEngine,
        Cena.Actors.Services.ErrorPatternMatching.ErrorPatternMatcherEngine>();

    builder.Services.AddSingleton<IMisconceptionDetectionService, MisconceptionDetectionService>();

    // prr-015 / ADR-0003: misconception PII registry + canonical Marten store.
    builder.Services.AddMisconceptionPiiStoreRegistry().AddCanonicalMartenMisconceptionStore();

    // ---- FIND-privacy-003: GDPR self-service compliance services ----
    // Students must be able to exercise data rights (consent, export, erasure, DSAR)
    // without going through an admin. GDPR Art 12-22, COPPA 312.6, Israel PPL 13.
    builder.Services.AddScoped<IGdprConsentManager, GdprConsentManager>();
    builder.Services.AddScoped<IRightToErasureService, RightToErasureService>();

    // ---- HARDEN TutorEndpoints: LLM Service ----
    // prr-012: Socratic call budget + static hint fallback + daily tutor time cap.
    Cena.Actors.Tutor.TutorServiceRegistration.AddTutorCostCaps(builder.Services);
    if (!string.IsNullOrEmpty(builder.Configuration["Cena:Llm:ApiKey"]))
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
    
    // FIND-privacy-008: PII scrubbing + safeguarding classification pipeline.
    // These run on every student message BEFORE the Anthropic API call.
    builder.Services.AddSingleton<ITutorPromptScrubber, TutorPromptScrubber>();
    builder.Services.AddSingleton<ISafeguardingClassifier, SafeguardingClassifier>();
    builder.Services.AddScoped<ISafeguardingEscalation, SafeguardingEscalation>();
    
    // ---- RDY-001: Content Moderation (CSAM + AI Safety) ----
    builder.Services.AddHttpClient<IPhotoDnaClient, PhotoDnaClient>(client =>
    {
        var timeout = builder.Configuration.GetValue<int>("Moderation:PhotoDna:TimeoutSeconds", 10);
        client.Timeout = TimeSpan.FromSeconds(timeout);
    });
    
    builder.Services.AddHttpClient<IContentSafetyClient, ContentSafetyClient>(client =>
    {
        var timeout = builder.Configuration.GetValue<int>("Moderation:ContentSafety:TimeoutSeconds", 15);
        client.Timeout = TimeSpan.FromSeconds(timeout);
    })
    .AddPolicyHandler(Policy
        .Handle<HttpRequestException>()
        .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)));
    
    builder.Services.AddSingleton<IIncidentReportService, IncidentReportService>().AddSingleton<IContentModerationPipeline, ContentModerationPipeline>();

    // ---- OCR cascade (ADR-0033, RDY-OCR-PORT) ----
    // Real implementations across the board. NO STUBS.
    //   - AddOcrCascadeCore wires: PdfTriage, Layer0, Layer2c, Layer3/4/5,
    //     ConfidenceGateOptions, Layer0PreprocessOptions, FigureStorageOptions,
    //     TimeProvider, IOcrCascadeService (scoped).
    //   - AddOcrCascadeWithCasValidation bridges ILatexValidator → the existing
    //     3-tier CasRouterService registered above (line ~238).
    //   - Wrapper layers registered per deps:
    //       Layer 2a Tesseract  — local binary (always available in our envs)
    //       Layer 1 Surya gRPC  — sidecar channel
    //       Layer 2b Pix2Tex    — sidecar channel
    //       Mathpix / Gemini    — opt-in per configured credentials
    builder.Services.AddOcrCascadeCore(builder.Configuration);
    builder.Services.AddOcrCascadeWithCasValidation();

    builder.Services.Configure<TesseractOptions>(builder.Configuration.GetSection("Ocr:Tesseract"));
    builder.Services.AddSingleton<ILayer2aTextOcr>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<TesseractOptions>>().Value;
        var log  = sp.GetService<ILogger<TesseractLocalRunner>>();
        return new TesseractLocalRunner(opts, log);
    });

    builder.Services.Configure<OcrSidecarOptions>(builder.Configuration.GetSection("Ocr:Sidecar"));
    builder.Services.AddSingleton<ILayer1Layout, SuryaSidecarClient>();
    builder.Services.AddSingleton<ILayer2bMathOcr, Pix2TexSidecarClient>();

    var mathpixAppId = builder.Configuration["Ocr:Mathpix:AppId"];
    if (!string.IsNullOrWhiteSpace(mathpixAppId))
    {
        builder.Services.Configure<MathpixOptions>(builder.Configuration.GetSection("Ocr:Mathpix"));
        builder.Services.AddHttpClient<IMathpixRunner, MathpixRunner>()
            .AddPolicyHandler(Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(3, a => TimeSpan.FromMilliseconds(250 * Math.Pow(2, a))))
            .AddPolicyHandler(Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)));
    }

    var geminiApiKey = builder.Configuration["Ocr:Gemini:ApiKey"];
    if (!string.IsNullOrWhiteSpace(geminiApiKey))
    {
        builder.Services.Configure<GeminiVisionOptions>(builder.Configuration.GetSection("Ocr:Gemini"));
        builder.Services.AddHttpClient<IGeminiVisionRunner, GeminiVisionRunner>()
            .AddPolicyHandler(Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(3, a => TimeSpan.FromMilliseconds(250 * Math.Pow(2, a))))
            .AddPolicyHandler(Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)));
    }

    // ---- Firebase Auth + Authorization ----
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddFirebaseAuth(builder.Configuration);
    Cena.Student.Api.Host.Auth.StudentApiExtrasRegistration.AddStudentApiExtras(builder.Services.AddCenaAuthorization()); // prr-001 + prr-011
    
    // FIND-sec-014: Security metrics for observability
    builder.Services.AddSecurityMetrics();

    // RDY-064: Error aggregator scaffold — Null aggregator by default.
    builder.Services.AddCenaErrorAggregator(builder.Configuration);

    // RDY-075 Phase 1B: Marten-backed offline sync ledger (60-day TTL
    // enforced by the retention worker).
    builder.Services.AddSingleton<Cena.Actors.Sessions.IOfflineSyncLedger,
        Cena.Actors.Sessions.MartenOfflineSyncLedger>();

    // RDY-071 Phase 1B: mastery-trajectory provider. Phase 1B ships
    // the NullMasteryTrajectoryProvider fallback so the endpoint
    // surface is exercisable without the Marten-backed projection
    // (Phase 1C).
    builder.Services.AddSingleton<IMasteryTrajectoryProvider,
        NullMasteryTrajectoryProvider>();

    // FIND-ux-006b: the student host needs the Firebase Admin SDK wrapper to
    // back the anonymous POST /api/auth/password-reset endpoint. The admin host
    // already registers this as a singleton; mirror that here so the student
    // self-service forgot-password flow has a real implementation on both sides.
    builder.Services.AddSingleton<IFirebaseAdminService, FirebaseAdminService>(); Cena.Student.Api.Host.Catalog.CatalogServiceRegistration.AddCenaExamCatalog(builder.Services, builder.Configuration, builder.Environment); // prr-220 ADR-0050

    // PRR-243: wire the catalog-backed שאלון validator (replaces the
    // permissive AllowAll registered by AddStudentPlanServices). Must run
    // AFTER AddCenaExamCatalog so IExamCatalogService is resolvable.
    // Use Replace (not Add) because AddStudentPlanServices used TryAdd,
    // and Replace ensures the catalog-backed implementation wins.
    builder.Services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
        Cena.Actors.StudentPlan.IQuestionPaperCatalogValidator,
        Cena.Student.Api.Host.Catalog.CatalogBackedQuestionPaperCatalogValidator>());

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
    // FIND-data-020: Partitioned rate limiting per user + tenant-level outer limiter
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
        // RATE-001: General API: 60 req/min per user (partitioned by user id)
        options.AddPolicy("api", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
            
            return RateLimitPartition.GetFixedWindowLimiter(
                userId,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    
        // RATE-001: Photo uploads: 10 per hour per student
        options.AddPolicy("photo", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
    
            return RateLimitPartition.GetFixedWindowLimiter(
                $"photo:{userId}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
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
    
        // Tutor LLM: 10 messages/min per student (partitioned by student id)
        options.AddPolicy("tutor", httpContext =>
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
    
        // FIND-privacy-003: GDPR data export — 1 request per hour per student
        options.AddPolicy("gdpr-export", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
    
            return RateLimitPartition.GetFixedWindowLimiter(
                $"gdpr-export:{userId}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    
        // FIND-privacy-003: GDPR erasure + DSAR — 1 request per day per student
        options.AddPolicy("gdpr-erasure", httpContext =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "anonymous";
    
            return RateLimitPartition.GetFixedWindowLimiter(
                $"gdpr-erasure:{userId}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromDays(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    
        // FIND-sec-015: Global tutor rate limit (not per-user) — 1000 msg/min across all users
        var globalTutorLimit = builder.Configuration.GetValue<int>("Cena:LlmBudget:GlobalTutorPerMinute", 1000);
        options.AddPolicy("tutor-global", _ =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                "tutor-global",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = globalTutorLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    
        // FIND-sec-015: Per-tenant tutor rate limit — 200 msg/min per school
        var tenantTutorLimit = builder.Configuration.GetValue<int>("Cena:LlmBudget:TenantTutorPerMinute", 200);
        options.AddPolicy("tutor-tenant", httpContext =>
        {
            var schoolId = httpContext.User.FindFirstValue("school_id") ?? "no-school";
            
            return RateLimitPartition.GetSlidingWindowLimiter(
                $"tutor-tenant:{schoolId}",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = tenantTutorLimit,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6, // 10-second segments for smoother limiting
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
            // RDY-OCR-OBSERVABILITY (Phase 4): OCR cascade metrics
            .AddMeter(Cena.Infrastructure.Ocr.Observability.OcrMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
            .AddPrometheusExporter());
    
    // =============================================================================
    // BUILD & PIPELINE
    // =============================================================================
    
    
    // RDY-009: Disable DI validation on build when generating OpenAPI artifacts
    // or in Development (partial DI graphs across hosts are expected in dev).
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
            Title = "Cena Student API",
            Version = "v1",
            Description = "Student-facing REST endpoints for the Cena adaptive learning platform."
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
    
    // Middleware order: CORS → Auth → Cookie → Revocation → Consent → FERPA Audit → RateLimiter → Endpoints
    app.UseCors();
    app.UseAuthentication();
    Cena.Student.Api.Host.Auth.StudentApiExtrasRegistration.UseStudentApiAuthPipeline(app); // prr-011
    app.UseAuthorization();
    app.UseMiddleware<TokenRevocationMiddleware>();
    app.UseConsentEnforcement(); // FIND-privacy-007: Consent gates data processing
    app.UseMiddleware<StudentDataAuditMiddleware>();
    app.UseMiddleware<RateLimitDegradationMiddleware>(); // RATE-001: distributed limits + degradation
    app.UseRateLimiter();
    
    // ---- Swagger / OpenAPI (RDY-009) ----
    app.UseSwagger();
    if (!app.Environment.IsProduction())
    {
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Cena Student API v1");
        });
    }
    
    // ---- Prometheus metrics endpoint ----
    app.MapPrometheusScrapingEndpoint();
    
    // ---- Health check ----
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-student-api" }));
    app.MapHealthChecks("/health/live"); app.MapHealthChecks("/health/ready");

    // ---- Student-facing REST endpoints (migrated from Cena.Api.Host) ----
    
    // Anonymous auth recovery endpoints (FIND-ux-006b) — password reset only
    app.MapAuthEndpoints(); app.MapCatalogEndpoints(); // prr-011 Session exchange wired via UseStudentApiAuthPipeline(); prr-220 catalog ADR-0050.

    // Me/Profile endpoints (STB-00, STB-00b)
    app.MapMeEndpoints();
    app.MapSelfAssessmentEndpoints();

    // prr-218/prr-234: legacy /api/me/study-plan removed; /api/me/exam-targets supersedes it per ADR-0050.
    app.MapExamTargetEndpoints();        // prr-218: multi-target /api/me/exam-targets per ADR-0050
    app.MapExamTargetQuestionPaperEndpoints(); // prr-243: שאלון post-hoc PATCH endpoints per ADR-0050 §1
    app.MapExamTargetParentVisibilityEndpoint(); // prr-230: POST /api/me/exam-targets/{id}/visibility
    app.MapDiagnosticEndpoints();        // RDY-023 + prr-228: legacy + per-target diagnostic blocks

    // Session Lifecycle endpoints (STB-01, STB-01b, STB-01c)
    app.MapSessionEndpoints();

    // prr-203: hint-ladder endpoint — POST /api/sessions/{sid}/question/{qid}/hint/next
    // per ADR-0045. Server-authoritative rung advancement; distinct route
    // from the inline /hint endpoint so the ladder ratchet does not couple
    // to the deprecated single-hint path.
    app.MapHintLadderEndpoint();

    // prr-204: tutor context endpoint — GET /api/v1/sessions/{sid}/tutor-context
    // per ADR-0003. Session-scoped Redis cache + live Marten fallback; the
    // Sidekick drawer + hint-ladder consumers read the current session's
    // tutor context (counts, rung, misconception tag, attempt phase, budget,
    // accommodations). Tenant-scoped — cross-tenant or cross-student reads
    // return 403 with a SIEM audit entry.
    app.MapTutorContextEndpoint();

    // prr-149: GET /api/session/{sessionId}/plan (scheduler plan read)
    app.MapSessionPlanEndpoints();

    // RDY-075 Phase 1B: offline PWA reconnect sync — accepts batched
    // offline answer events with ItemVersionFreeze guards.
    app.MapOfflineSyncEndpoints();

    // RDY-071 Phase 1B: mastery-trajectory read endpoint (bucket-only,
    // never numeric Bagrut prediction).
    app.MapTrajectoryEndpoints();

    // RDY-034: Flow state assessment endpoint — computes state + action
    // from session signals supplied by the caller. Frontend parity with
    // src/student/full-version/src/composables/useFlowState.ts.
    app.MapFlowStateEndpoints();

    // prr-159: Anonymous peer-confused signal endpoint. Session-scoped,
    // k-anon ≥3, never identifies emitting students.
    app.MapPeerConfusedSignalEndpoints();

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
    
    // GDPR Self-Service endpoints (FIND-privacy-003)
    // Student-facing consent, export, erasure, and DSAR endpoints
    app.MapMeGdprEndpoints();
    
    // Granular Consent Management endpoints (SEC-006)
    // Per-purpose consent control with defaults and bulk operations.
    // prr-052: student parent-visibility view + veto also mapped below.
    app.MapConsentEndpoints();
    app.MapParentVisibilityEndpoints();

    // prr-123: Dual-version privacy policy. Public read — no auth required;
    // the app shell renders the current policy during pre-consent onboarding.
    app.MapLegalEndpoints();

    // RATE-001: Rate limit dashboard (real-time spend + status)
    app.MapRateLimitDashboardEndpoints();
    
    // ---- RDY-001: Photo endpoints (gated by CENA_IMAGE_UPLOAD_ENABLED) ----
    app.MapPhotoUploadEndpoints();
    app.MapPhotoCaptureEndpoints();
    
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
        Environment.SetEnvironmentVariable("Kestrel__Endpoints__Http__Url", "http://127.0.0.1:0");
        var app = Program.BuildApp(Array.Empty<string>());
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }
}
