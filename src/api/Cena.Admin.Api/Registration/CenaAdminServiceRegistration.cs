// =============================================================================
// Cena Platform -- Shared Admin Service Registration (REV-016.2)
// Single source of truth for admin DI registrations and endpoint mappings.
// Both Cena.Actors.Host and Cena.Api.Host call these extension methods.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Admin.Api.Content;
using Cena.Admin.Api.Endpoints;
using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.QualityGate;
using Cena.Admin.Api.Questions;
using Cena.Admin.Api.RateLimit;
using Cena.Infrastructure.Compliance;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Registration;

public static class CenaAdminServiceRegistration
{
    /// <summary>
    /// Registers all admin API domain services (ADM-004 through ADM-016).
    /// Call from both Actor Host and Admin API Host Program.cs.
    /// </summary>
    public static IServiceCollection AddCenaAdminServices(this IServiceCollection services)
    {
        // Quality Gate service (needed by QuestionBankService).
        // RDY-034 §13: pass IDocumentStore so the gate can source FactualAccuracy
        // from the persisted CAS binding for math/physics subjects.
        services.AddSingleton<QualityGate.IQualityGateService>(sp =>
            new QualityGate.QualityGateService(
                configuration: sp.GetRequiredService<IConfiguration>(),
                logger: sp.GetRequiredService<ILogger<QualityGate.QualityGateService>>(),
                store: sp.GetService<Marten.IDocumentStore>()));

        // RDY-034 / ADR-0002: CAS ingestion gate services.
        // - MathContentDetector: boundary probe for question bodies.
        // - CasGateModeProvider:  Off | Shadow | Enforce rollout.
        // - CasVerificationGate:  runs ICasRouterService + builds binding doc.
        // The CAS engine stack (ICasRouterService + circuit breaker) is
        // registered by the host Program.cs (Admin + Actor hosts both).
        services.AddSingleton<IMathContentDetector, MathContentDetector>();
        services.AddSingleton<ICasGateModeProvider, CasGateModeProvider>();
        // RDY-038 / ADR-0002: stem-solution extractor lets the gate run
        // Equivalence checks against the stem's own expected answer, not
        // just a NormalForm parseability probe. Without this registration
        // the gate degrades every question to Unverifiable.
        services.AddSingleton<IStemSolutionExtractor, StemSolutionExtractor>();
        services.AddScoped<ICasVerificationGate, CasVerificationGate>();
        // RDY-037: single gated-write site (see CasGatedQuestionPersister)
        // — every question creation path routes through this persister.
        services.AddScoped<ICasGatedQuestionPersister, CasGatedQuestionPersister>();

        // RDY-045: Security notifier for CAS overrides (and future
        // high-impact security events). Falls back to the logs-only null
        // implementation when CENA_SECURITY_SLACK_WEBHOOK is unset so dev
        // and CI are not blocked on a webhook URL.
        services.AddHttpClient("SecurityNotifier");
        services.AddSingleton<Services.ISecurityNotifier>(sp =>
        {
            var webhook = Environment.GetEnvironmentVariable(
                Services.SlackWebhookSecurityNotifier.WebhookEnvVar);
            if (string.IsNullOrWhiteSpace(webhook))
            {
                return new Services.NullSecurityNotifier(
                    sp.GetRequiredService<ILogger<Services.NullSecurityNotifier>>());
            }
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new Services.SlackWebhookSecurityNotifier(
                httpFactory.CreateClient("SecurityNotifier"),
                webhook,
                sp.GetRequiredService<ILogger<Services.SlackWebhookSecurityNotifier>>());
        });

        // ADM-004 through ADM-016: Admin API services
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();
        services.AddScoped<IContentModerationService, ContentModerationService>();
        services.AddScoped<IFocusAnalyticsService, FocusAnalyticsService>();
        services.AddScoped<IMasteryTrackingService, MasteryTrackingService>();
        services.AddScoped<ISystemMonitoringService, SystemMonitoringService>();
        services.AddScoped<IIngestionPipelineService, IngestionPipelineService>();
        // RDY-OCR-WIREUP-C (Phase 2.3): Bagrut PDF ingestion routes through
        // the real OCR cascade (IOcrCascadeService, ADR-0033). No stubs.
        services.AddScoped<Ingestion.IBagrutPdfIngestionService, Ingestion.BagrutPdfIngestionService>();

        // RDY-019e-IMPL (Phase 1C): Curator metadata handshake — auto-extract
        // on upload, curator review + confirm via three REST endpoints,
        // confirmed state feeds OcrContextHints into the cascade.
        services.AddSingleton<Ingestion.ICuratorMetadataExtractor, Ingestion.CuratorMetadataExtractor>();
        services.AddScoped<Ingestion.ICuratorMetadataService, Ingestion.CuratorMetadataService>();

        // RDY-019c (Phase 3): Content coverage report keyed by
        // scripts/bagrut-taxonomy.json. Real Marten query + taxonomy walk.
        services.AddSingleton<Content.TaxonomyCache>(_ => Content.TaxonomyCache.LoadFromDisk());
        services.AddScoped<Content.IContentCoverageQuestionSource, Content.MartenQuestionSource>();
        services.AddScoped<Content.IContentCoverageService, Content.ContentCoverageService>();

        // RDY-059: Corpus expander source-selector (real Marten-backed).
        services.AddScoped<Questions.ICorpusSourceProvider, Questions.MartenCorpusSourceProvider>();
        services.AddScoped<IQuestionBankService, QuestionBankService>();
        // FIND-pedagogy-008: read API for the admin LO picker
        services.AddScoped<ILearningObjectiveService, LearningObjectiveService>();
        services.AddScoped<IMethodologyAnalyticsService, MethodologyAnalyticsService>();
        services.AddScoped<ICulturalContextService, CulturalContextService>();
        services.AddHostedService<CulturalContextSeeder>();
        services.AddScoped<IEventStreamService, EventStreamService>();
        services.AddScoped<IOutreachEngagementService, OutreachEngagementService>();
        services.AddSingleton<IAiGenerationService, AiGenerationService>();

        // CNT-002: Question pipeline orchestration
        services.AddScoped<IQuestionPipelineService, QuestionPipelineService>();

        // SAI Admin Services (ADM-017 through ADM-023)
        services.AddScoped<ITutoringAdminService, TutoringAdminService>();
        services.AddScoped<IExplanationCacheAdminService, ExplanationCacheAdminService>();
        services.AddScoped<IExperimentAdminService, ExperimentAdminService>();
        services.AddScoped<IEmbeddingAdminService, EmbeddingAdminService>();
        services.AddScoped<ITokenBudgetAdminService, TokenBudgetAdminService>();

        // ADM-025: Messaging admin service
        services.AddScoped<IMessagingAdminService, MessagingAdminService>();

        // ADM-026: Live session monitor (SSE background service)
        services.AddSingleton<ILiveMonitorService, LiveMonitorService>();
        services.AddHostedService(sp => (LiveMonitorService)sp.GetRequiredService<ILiveMonitorService>());

        // Ingestion Settings (cloud dirs, email, messaging channels, pipeline config)
        services.AddScoped<IIngestionSettingsService, IngestionSettingsService>();

        // Student Insights (per-student cross-cutting analytics)
        services.AddScoped<IStudentInsightsService, StudentInsightsService>();

        // Stagnation Insights (job-based causal factor analysis)
        services.AddScoped<IStagnationInsightsService, StagnationInsightsService>();

        // RATE-001: Rate limit admin dashboard
        services.AddScoped<IRateLimitAdminService, RateLimitAdminService>();

        // FIND-arch-006: GDPR compliance services (SEC-005, Articles 17 & 20).
        // Both services depend only on Marten IDocumentStore + ILogger, which
        // are registered by the host's AddMarten() call before this method
        // runs. Scoped lifetime matches the host-scoped HTTP request.
        services.AddScoped<IGdprConsentManager, GdprConsentManager>();
        services.AddScoped<IRightToErasureService, RightToErasureService>();

        return services;
    }

    /// <summary>
    /// Maps all shared admin REST API endpoint groups (ADM-004 through ADM-023).
    /// Call from both Actor Host and Admin API Host Program.cs after auth middleware.
    /// </summary>
    public static IEndpointRouteBuilder MapCenaAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAdminDashboardEndpoints();
        app.MapAdminUserEndpoints();
        app.MapAdminRoleEndpoints();
        app.MapContentModerationEndpoints();
        app.MapFocusAnalyticsEndpoints();
        app.MapMasteryTrackingEndpoints();
        app.MapSystemMonitoringEndpoints();
        app.MapIngestionPipelineEndpoints();
        // RDY-019e-IMPL (Phase 1C): CuratorMetadata handshake endpoints
        // under /api/admin/ingestion/pipeline/{id}/metadata (GET/PATCH/DELETE).
        app.MapCuratorMetadataEndpoints();

        // RDY-057 (Phase 3): POST /api/admin/ingestion/bagrut — SuperAdmin-only
        // PDF ingest trigger that routes to BagrutPdfIngestionService.
        app.MapBagrutIngestEndpoints();

        // RDY-058: POST /api/admin/questions/{id}/generate-similar — one-click
        // variant generation from an existing question. Routes through
        // AiGenerationService.BatchGenerateAsync so every candidate passes the
        // CAS gate + QualityGate.
        app.MapGenerateSimilarEndpoints();

        // RDY-059: POST /api/admin/questions/expand-corpus — batch corpus
        // expander (SuperAdminOnly, dry-run by default).
        app.MapCorpusExpanderEndpoints();

        // RDY-019c (Phase 3): GET /api/v1/admin/content/coverage
        app.MapContentCoverageEndpoints();
        app.MapQuestionBankEndpoints();
        // RDY-036: CAS operator surfaces — override (super-admin only) +
        // backfill (admin only). Wired here so both Actor.Host and Admin.Host
        // pick them up via the shared registration.
        app.MapCasOverrideEndpoint();
        app.MapCasBackfillEndpoint();
        // FIND-pedagogy-008: learning-objective picker (read-only)
        app.MapLearningObjectiveEndpoints();
        app.MapMethodologyAnalyticsEndpoints();
        app.MapCulturalContextEndpoints();
        app.MapEventStreamEndpoints();
        app.MapOutreachEngagementEndpoints();
        app.MapAiGenerationEndpoints();
        app.MapQuestionPipelineEndpoints();

        // SAI Admin endpoints (ADM-017 through ADM-023)
        app.MapTutoringAdminEndpoints();
        app.MapExplanationCacheEndpoints();
        app.MapExperimentAdminEndpoints();
        app.MapEmbeddingAdminEndpoints();
        app.MapTokenBudgetEndpoints();

        // ADM-025: Messaging admin endpoints
        app.MapMessagingAdminEndpoints();

        // ADM-026: Live monitor endpoints
        app.MapLiveMonitorEndpoints();

        // Ingestion Settings endpoints
        app.MapIngestionSettingsEndpoints();

        // Student Insights endpoints
        app.MapStudentInsightsEndpoints();

        // Stagnation Insights endpoints
        app.MapStagnationInsightsEndpoints();

        // RATE-001: Rate limit admin dashboard
        app.MapRateLimitAdminEndpoints();

        // FIND-arch-006: GDPR admin endpoints (SEC-005, Articles 17 & 20).
        // Previously defined in GdprEndpoints.cs but never wired — all six
        // consent / export / erasure routes were unreachable.
        app.MapGdprEndpoints();

        return app;
    }
}
