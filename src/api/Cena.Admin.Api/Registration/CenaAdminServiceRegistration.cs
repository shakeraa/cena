// =============================================================================
// Cena Platform -- Shared Admin Service Registration (REV-016.2)
// Single source of truth for admin DI registrations and endpoint mappings.
// Both Cena.Actors.Host and Cena.Api.Host call these extension methods.
// =============================================================================

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
        services.AddScoped<IMethodologyAnalyticsService, MethodologyAnalyticsService>();
        services.AddScoped<ICulturalContextService, CulturalContextService>();
        services.AddScoped<IEventStreamService, EventStreamService>();
        services.AddScoped<IOutreachEngagementService, OutreachEngagementService>();
        services.AddSingleton<IAiGenerationService, AiGenerationService>();

        // SAI Admin Services (ADM-017 through ADM-023)
        services.AddScoped<ITutoringAdminService, TutoringAdminService>();
        services.AddScoped<IExplanationCacheAdminService, ExplanationCacheAdminService>();
        services.AddScoped<IExperimentAdminService, ExperimentAdminService>();
        services.AddScoped<IEmbeddingAdminService, EmbeddingAdminService>();
        services.AddScoped<ITokenBudgetAdminService, TokenBudgetAdminService>();

        // ADM-025: Messaging admin service
        services.AddScoped<IMessagingAdminService, MessagingAdminService>();

        // Student Insights (per-student cross-cutting analytics)
        services.AddScoped<IStudentInsightsService, StudentInsightsService>();

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
        app.MapMethodologyAnalyticsEndpoints();
        app.MapCulturalContextEndpoints();
        app.MapEventStreamEndpoints();
        app.MapOutreachEngagementEndpoints();
        app.MapAiGenerationEndpoints();

        // SAI Admin endpoints (ADM-017 through ADM-023)
        app.MapTutoringAdminEndpoints();
        app.MapExplanationCacheEndpoints();
        app.MapExperimentAdminEndpoints();
        app.MapEmbeddingAdminEndpoints();
        app.MapTokenBudgetEndpoints();

        // ADM-025: Messaging admin endpoints
        app.MapMessagingAdminEndpoints();

        // Student Insights endpoints
        app.MapStudentInsightsEndpoints();

        return app;
    }
}
