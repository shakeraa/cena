// =============================================================================
// Cena Platform -- Admin API Endpoints (ADM-006 through ADM-014)
// Consolidated endpoint registration for remaining admin features
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class AdminApiEndpoints
{
    public static IEndpointRouteBuilder MapFocusAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/focus")
            .WithTags("Focus Analytics")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/overview", async (string? classId, IFocusAnalyticsService service) =>
        {
            var overview = await service.GetOverviewAsync(classId);
            return Results.Ok(overview);
        }).WithName("GetFocusOverview");

        group.MapGet("/students/{studentId}", async (string studentId, IFocusAnalyticsService service) =>
        {
            var detail = await service.GetStudentFocusAsync(studentId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetStudentFocus");

        group.MapGet("/classes/{classId}", async (string classId, IFocusAnalyticsService service) =>
        {
            var detail = await service.GetClassFocusAsync(classId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetClassFocus");

        group.MapGet("/degradation-curve", async (IFocusAnalyticsService service) =>
        {
            var curve = await service.GetDegradationCurveAsync();
            return Results.Ok(curve);
        }).WithName("GetFocusDegradationCurve");

        group.MapGet("/experiments", async (IFocusAnalyticsService service) =>
        {
            var experiments = await service.GetExperimentsAsync();
            return Results.Ok(experiments);
        }).WithName("GetFocusExperiments");

        group.MapGet("/alerts", async (IFocusAnalyticsService service) =>
        {
            var alerts = await service.GetStudentsNeedingAttentionAsync();
            return Results.Ok(alerts);
        }).WithName("GetFocusAlerts");

        group.MapGet("/students/{studentId}/timeline", async (string studentId, string? period, IFocusAnalyticsService service) =>
        {
            var timeline = await service.GetStudentTimelineAsync(studentId, period ?? "7d");
            return Results.Ok(timeline);
        }).WithName("GetStudentFocusTimeline");

        group.MapGet("/classes/{classId}/heatmap", async (string classId, IFocusAnalyticsService service) =>
        {
            var heatmap = await service.GetClassHeatmapAsync(classId);
            return Results.Ok(heatmap);
        }).WithName("GetClassFocusHeatmap");

        return app;
    }

    public static IEndpointRouteBuilder MapMasteryTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/mastery")
            .WithTags("Mastery Tracking")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/overview", async (string? classId, IMasteryTrackingService service) =>
        {
            var overview = await service.GetOverviewAsync(classId);
            return Results.Ok(overview);
        }).WithName("GetMasteryOverview");

        group.MapGet("/students/{studentId}", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetStudentMastery");

        group.MapGet("/students/{studentId}/knowledge-map", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            return detail != null ? Results.Ok(detail.KnowledgeMap) : Results.NotFound();
        }).WithName("GetStudentKnowledgeMap");

        group.MapGet("/classes/{classId}", async (string classId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetClassMasteryAsync(classId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetClassMastery");

        group.MapGet("/at-risk", async (IMasteryTrackingService service) =>
        {
            var atRisk = await service.GetAtRiskStudentsAsync();
            return Results.Ok(atRisk);
        }).WithName("GetAtRiskStudents");

        return app;
    }

    public static IEndpointRouteBuilder MapSystemMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/system")
            .WithTags("System Monitoring")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        group.MapGet("/health", async (ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();
            return Results.Ok(health);
        }).WithName("GetSystemHealth");

        group.MapGet("/metrics", async (ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();
            return Results.Ok(health.ErrorRates);
        }).WithName("GetSystemMetrics");

        group.MapGet("/actors", async (ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();
            return Results.Ok(health.ActorSystems);
        }).WithName("GetActorSystemStatus");

        group.MapGet("/settings", async (ISystemMonitoringService service) =>
        {
            var settings = await service.GetSettingsAsync();
            return Results.Ok(settings);
        }).WithName("GetPlatformSettings");

        group.MapPut("/settings", async (UpdateSettingsRequest request, HttpContext httpContext, ISystemMonitoringService service) =>
        {
            var userId = httpContext.User.Identity?.Name ?? "unknown";
            var success = await service.UpdateSettingsAsync(request, userId);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("UpdatePlatformSettings");

        group.MapPost("/audit-log/query", async (AuditLogFilterRequest request, int? page, int? pageSize, ISystemMonitoringService service) =>
        {
            var result = await service.GetAuditLogAsync(request, page ?? 1, pageSize ?? 20);
            return Results.Ok(result);
        }).WithName("QueryAuditLog");

        return app;
    }

    public static IEndpointRouteBuilder MapIngestionPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion")
            .WithTags("Ingestion Pipeline")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/pipeline-status", async (IIngestionPipelineService service) =>
        {
            var status = await service.GetPipelineStatusAsync();
            return Results.Ok(status);
        }).WithName("GetPipelineStatus");

        group.MapGet("/items", async (string? stage, int? page, int? pageSize, IIngestionPipelineService service) =>
        {
            var status = await service.GetPipelineStatusAsync();
            var items = stage != null
                ? status.Stages.FirstOrDefault(s => s.StageId == stage)?.Items ?? new List<PipelineItem>()
                : status.Stages.SelectMany(s => s.Items).ToList();
            return Results.Ok(new { Items = items, Total = items.Count });
        }).WithName("GetPipelineItems");

        group.MapGet("/items/{id}/detail", async (string id, IIngestionPipelineService service) =>
        {
            var detail = await service.GetItemDetailAsync(id);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetPipelineItemDetail");

        group.MapPost("/items/{id}/retry", async (string id, IIngestionPipelineService service) =>
        {
            var success = await service.RetryItemAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RetryPipelineItem");

        group.MapPost("/items/{id}/reject", async (string id, RejectPipelineItemRequest request, IIngestionPipelineService service) =>
        {
            var success = await service.RejectPipelineItemAsync(id, request.Reason);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RejectPipelineItem");

        group.MapGet("/stats", async (IIngestionPipelineService service) =>
        {
            var stats = await service.GetStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetPipelineStats");

        group.MapPost("/items/{id}/move-to-review", async (string id, IIngestionPipelineService service) =>
        {
            var result = await service.MoveToReviewAsync(id);
            return Results.Ok(result);
        }).WithName("MoveItemToReview");

        group.MapPost("/upload", async (HttpRequest request, IIngestionPipelineService service) =>
        {
            var result = await service.UploadFromRequestAsync(request);
            return Results.Ok(result);
        }).WithName("UploadPipelineFile").DisableAntiforgery();

        return app;
    }

    public static IEndpointRouteBuilder MapQuestionBankEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/questions")
            .WithTags("Question Bank")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/", async (
            string? subject,
            int? bloomsLevel,
            float? minDifficulty,
            float? maxDifficulty,
            string? status,
            string? language,
            string? conceptId,
            string? q,
            int? page,
            int? itemsPerPage,
            string? sortBy,
            string? orderBy,
            IQuestionBankService service) =>
        {
            var result = await service.GetQuestionsAsync(
                subject, bloomsLevel, minDifficulty, maxDifficulty, status, language, conceptId, q,
                page ?? 1, itemsPerPage ?? 20, sortBy ?? "qualityScore", orderBy ?? "desc");
            return Results.Ok(result);
        }).WithName("GetQuestions");

        group.MapGet("/{id}", async (string id, IQuestionBankService service) =>
        {
            var question = await service.GetQuestionAsync(id);
            return question != null ? Results.Ok(question) : Results.NotFound();
        }).WithName("GetQuestion");

        group.MapPut("/{id}", async (string id, UpdateBankQuestionRequest request, IQuestionBankService service) =>
        {
            var question = await service.UpdateQuestionAsync(id, request);
            return question != null ? Results.Ok(question) : Results.NotFound();
        }).WithName("UpdateQuestion");

        group.MapPost("/{id}/deprecate", async (string id, DeprecateBankQuestionRequest request, IQuestionBankService service) =>
        {
            var success = await service.DeprecateQuestionAsync(id, request);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("DeprecateQuestion");

        group.MapGet("/filters", async (IQuestionBankService service) =>
        {
            var filters = await service.GetFiltersAsync();
            return Results.Ok(filters);
        }).WithName("GetQuestionFilters");

        group.MapGet("/concepts", async (string q, IQuestionBankService service) =>
        {
            var matches = await service.AutocompleteConceptsAsync(q);
            return Results.Ok(matches);
        }).WithName("AutocompleteConcepts");

        group.MapGet("/{id}/performance", async (string id, IQuestionBankService service) =>
        {
            var perf = await service.GetPerformanceAsync(id);
            return perf != null ? Results.Ok(perf) : Results.NotFound();
        }).WithName("GetQuestionPerformance");

        group.MapPost("/{id}/approve", async (string id, IQuestionBankService service) =>
        {
            var success = await service.ApproveAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("ApproveQuestion");

        return app;
    }

    public static IEndpointRouteBuilder MapMethodologyAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/pedagogy")
            .WithTags("Methodology Analytics")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/methodology-effectiveness", async (IMethodologyAnalyticsService service) =>
        {
            var effectiveness = await service.GetEffectivenessAsync();
            return Results.Ok(effectiveness);
        }).WithName("GetMethodologyEffectiveness");

        group.MapGet("/stagnation-trend", async (IMethodologyAnalyticsService service) =>
        {
            var effectiveness = await service.GetEffectivenessAsync();
            return Results.Ok(effectiveness.StagnationTrend);
        }).WithName("GetStagnationTrend");

        group.MapGet("/switch-triggers", async (IMethodologyAnalyticsService service) =>
        {
            var effectiveness = await service.GetEffectivenessAsync();
            return Results.Ok(effectiveness.SwitchTriggers);
        }).WithName("GetSwitchTriggers");

        group.MapGet("/mentor-resistant", async (IMethodologyAnalyticsService service) =>
        {
            var monitor = await service.GetStagnationMonitorAsync();
            return Results.Ok(monitor.MentorResistantConcepts);
        }).WithName("GetMentorResistantConcepts");

        group.MapGet("/stagnation-monitor", async (IMethodologyAnalyticsService service) =>
        {
            var monitor = await service.GetStagnationMonitorAsync();
            return Results.Ok(monitor);
        }).WithName("GetStagnationMonitor");

        group.MapGet("/mcm-graph", async (IMethodologyAnalyticsService service) =>
        {
            var graph = await service.GetMcmGraphAsync();
            return Results.Ok(graph);
        }).WithName("GetMcmGraph");

        group.MapPut("/mcm-graph/edge", async (UpdateMcmEdgeRequest request, IMethodologyAnalyticsService service) =>
        {
            var success = await service.UpdateMcmEdgeAsync(request.Source, request.Target, request.Confidence);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("UpdateMcmEdge");

        group.MapPost("/mcm-graph/edge", async (UpdateMcmEdgeRequest request, IMethodologyAnalyticsService service) =>
        {
            var success = await service.UpdateMcmEdgeAsync(request.Source, request.Target, request.Confidence);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("CreateMcmEdge");

        return app;
    }

    public static IEndpointRouteBuilder MapCulturalContextEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/cultural")
            .WithTags("Cultural Context")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        group.MapGet("/distribution", async (ICulturalContextService service) =>
        {
            var distribution = await service.GetDistributionAsync();
            return Results.Ok(distribution);
        }).WithName("GetCulturalDistribution");

        group.MapGet("/resilience-comparison", async (ICulturalContextService service) =>
        {
            var distribution = await service.GetDistributionAsync();
            return Results.Ok(distribution.ResilienceByGroup);
        }).WithName("GetResilienceComparison");

        group.MapGet("/methodology-by-context", async (ICulturalContextService service) =>
        {
            var distribution = await service.GetDistributionAsync();
            return Results.Ok(distribution.MethodologyEffectiveness);
        }).WithName("GetMethodologyByContext");

        group.MapGet("/equity-alerts", async (ICulturalContextService service) =>
        {
            var alerts = await service.GetEquityAlertsAsync();
            return Results.Ok(alerts);
        }).WithName("GetEquityAlerts");

        return app;
    }

    public static IEndpointRouteBuilder MapEventStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/events")
            .WithTags("Event Stream")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        group.MapGet("/recent", async (int? count, string? continuationToken, IEventStreamService service) =>
        {
            var events = await service.GetRecentEventsAsync(count ?? 50, continuationToken);
            return Results.Ok(events);
        }).WithName("GetRecentEvents");

        group.MapGet("/rates", async (IEventStreamService service) =>
        {
            var rates = await service.GetEventRatesAsync();
            return Results.Ok(rates);
        }).WithName("GetEventRates");

        group.MapGet("/dead-letters", async (int? page, int? pageSize, IEventStreamService service) =>
        {
            var dlq = await service.GetDeadLetterQueueAsync(page ?? 1, pageSize ?? 20);
            return Results.Ok(dlq);
        }).WithName("GetDeadLetterQueue");

        group.MapGet("/dead-letters/{id}", async (string id, IEventStreamService service) =>
        {
            var detail = await service.GetDeadLetterDetailAsync(id);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetDeadLetterDetail");

        group.MapPost("/dead-letters/{id}/retry", async (string id, IEventStreamService service) =>
        {
            var result = await service.RetryMessageAsync(id);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        }).WithName("RetryDeadLetter");

        group.MapPost("/dead-letters/bulk-retry", async (BulkRetryRequest request, IEventStreamService service) =>
        {
            var result = await service.BulkRetryAsync(request.MessageIds);
            return Results.Ok(result);
        }).WithName("BulkRetryDeadLetters");

        group.MapGet("/dlq-alert", async (IEventStreamService service) =>
        {
            var alert = await service.CheckDlqDepthAsync();
            return Results.Ok(alert);
        }).WithName("GetDlqAlert");

        group.MapPost("/dead-letters/{id}/discard", async (string id, IEventStreamService service) =>
        {
            var success = await service.DiscardDeadLetterAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("DiscardDeadLetter");

        return app;
    }

    public static IEndpointRouteBuilder MapOutreachEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/outreach")
            .WithTags("Outreach & Engagement")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/summary", async (IOutreachEngagementService service) =>
        {
            var summary = await service.GetSummaryAsync();
            return Results.Ok(summary);
        }).WithName("GetOutreachSummary");

        group.MapGet("/by-channel", async (IOutreachEngagementService service) =>
        {
            var summary = await service.GetSummaryAsync();
            return Results.Ok(summary.ByChannel);
        }).WithName("GetOutreachByChannel");

        group.MapGet("/by-trigger", async (IOutreachEngagementService service) =>
        {
            var effectiveness = await service.GetChannelEffectivenessAsync();
            return Results.Ok(effectiveness.VolumeByTrigger);
        }).WithName("GetOutreachByTrigger");

        group.MapGet("/re-engagement-rate", async (IOutreachEngagementService service) =>
        {
            var summary = await service.GetSummaryAsync();
            return Results.Ok(summary.ReEngagementRate);
        }).WithName("GetReEngagementRate");

        group.MapGet("/students/{studentId}/history", async (string studentId, IOutreachEngagementService service) =>
        {
            var history = await service.GetStudentHistoryAsync(studentId);
            return history != null ? Results.Ok(history) : Results.NotFound();
        }).WithName("GetStudentOutreachHistory");

        group.MapGet("/budget-alert", async (IOutreachEngagementService service) =>
        {
            var alert = await service.GetBudgetAlertAsync();
            return Results.Ok(alert);
        }).WithName("GetBudgetAlert");

        return app;
    }
}
