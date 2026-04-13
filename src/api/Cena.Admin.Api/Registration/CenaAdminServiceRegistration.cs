// =============================================================================
// Cena Platform -- Shared Admin Service Registration (REV-016.2)
// Single source of truth for admin DI registrations and endpoint mappings.
// Both Cena.Actors.Host and Cena.Api.Host call these extension methods.
// =============================================================================

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
        // Quality Gate service (needed by QuestionBankService)
        services.AddSingleton<QualityGate.IQualityGateService>(sp =>
            new QualityGate.QualityGateService(
                configuration: sp.GetRequiredService<IConfiguration>(),
                logger: sp.GetRequiredService<ILogger<QualityGate.QualityGateService>>()));

        // ADM-004 through ADM-016: Admin API services
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();
        services.AddScoped<IContentModerationService, ContentModerationService>();
        services.AddScoped<IFocusAnalyticsService, FocusAnalyticsService>();
        services.AddScoped<IMasteryTrackingService, MasteryTrackingService>();
        services.AddScoped<ISystemMonitoringService, SystemMonitoringService>();
        services.AddScoped<IIngestionPipelineService, IngestionPipelineService>();
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
        app.MapQuestionBankEndpoints();
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
