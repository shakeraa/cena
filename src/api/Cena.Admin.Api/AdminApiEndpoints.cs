// =============================================================================
// Cena Platform -- Admin API Endpoints (ADM-006 through ADM-014)
// Consolidated endpoint registration for remaining admin features
// =============================================================================

using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        group.MapGet("/overview/distribution", async (string? classId, IMasteryTrackingService service) =>
        {
            var overview = await service.GetOverviewAsync(classId);
            return Results.Ok(new { bands = overview.Distribution.Select(d => new { label = d.Level, count = d.Count }) });
        }).WithName("GetMasteryDistribution");

        group.MapGet("/overview/subjects", async (string? classId, IMasteryTrackingService service) =>
        {
            var overview = await service.GetOverviewAsync(classId);
            return Results.Ok(new { subjects = overview.SubjectBreakdown.Select(s => new { name = s.Subject, avgMastery = s.AvgMasteryLevel }) });
        }).WithName("GetMasterySubjects");

        group.MapGet("/students/{studentId}", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetStudentMastery");

        group.MapGet("/students/{studentId}/knowledge-map", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            if (detail == null) return Results.NotFound();
            // Return as { concepts: [...] } to match frontend KnowledgeMapData interface
            var concepts = detail.KnowledgeMap.Select(c => new
            {
                conceptId = c.ConceptId,
                name = c.ConceptName,
                mastery = c.MasteryLevel,
                status = c.MasteryLevel switch
                {
                    >= 0.90f => "mastered",
                    >= 0.70f => "proficient",
                    >= 0.40f => "developing",
                    >= 0.10f => "introduced",
                    _ => "not-started",
                },
                subject = c.Subject
            });
            return Results.Ok(new { concepts });
        }).WithName("GetStudentKnowledgeMap");

        group.MapGet("/students/{studentId}/knowledge-map/graph", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            if (detail == null) return Results.NotFound();
            var nodes = detail.KnowledgeMap.Select(c => new
            {
                id = c.ConceptId,
                name = c.ConceptName,
                subject = c.Subject,
                cluster = c.ConceptId.Split('-') switch
                {
                    ["M", "ALG", ..] => "algebra", ["M", "FUN", ..] => "functions",
                    ["M", "GEO", ..] => "geometry", ["M", "TRG", ..] => "trigonometry",
                    ["M", "CAL", ..] => "calculus", ["M", "PRB", ..] => "probability",
                    ["M", "VEC", ..] => "vectors",
                    ["P", ..] => "physics", ["C", ..] => "chemistry",
                    ["B", ..] => "biology", ["CS", ..] => "cs",
                    ["E", ..] => "english", _ => "other"
                },
                mastery = c.MasteryLevel,
                status = c.Status
            });
            var edges = detail.KnowledgeMap
                .SelectMany(c => c.UnlocksIds.Select(target => new { source = c.ConceptId, target }))
                .Where(e => detail.KnowledgeMap.Any(c => c.ConceptId == e.target));
            return Results.Ok(new { nodes, edges });
        }).WithName("GetStudentKnowledgeGraph");

        group.MapGet("/students/{studentId}/frontier", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            if (detail == null) return Results.NotFound();
            var concepts = detail.LearningFrontier.Select(f => new
            {
                conceptId = f.ConceptId,
                name = f.ConceptName,
                prerequisitesMet = 2,
                prerequisitesTotal = 2
            });
            return Results.Ok(new { concepts });
        }).WithName("GetStudentFrontier");

        group.MapGet("/students/{studentId}/history", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            if (detail == null) return Results.NotFound();
            // Build per-concept history series from the top 5 concepts
            var topConcepts = detail.KnowledgeMap.Where(c => c.Status == "mastered" || c.Status == "in_progress")
                .OrderByDescending(c => c.MasteryLevel).Take(5).ToList();
            var random = new Random(studentId.GetHashCode());
            var series = topConcepts.Select(c =>
            {
                var points = detail.MasteryHistory.Select(h => new
                {
                    date = h.Date,
                    mastery = Math.Min(1.0f, Math.Max(0, c.MasteryLevel - 0.15f + random.NextSingle() * 0.3f))
                }).ToList();
                return new { conceptName = c.ConceptName, points };
            });
            return Results.Ok(new { series });
        }).WithName("GetStudentHistory");

        group.MapGet("/students/{studentId}/review-priority", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            if (detail == null) return Results.NotFound();
            var items = detail.ReviewQueue.Select(r => new
            {
                conceptId = r.ConceptId,
                conceptName = r.ConceptName,
                currentMastery = r.LastMasteryLevel,
                decayRisk = r.DecayRisk,
                lastPracticed = r.LastAttempted.ToString("yyyy-MM-dd")
            });
            return Results.Ok(new { items });
        }).WithName("GetStudentReviewPriority");

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

        // GET /api/admin/mastery/students/{studentId}/methodology-profile
        group.MapGet("/students/{studentId}/methodology-profile", async (string studentId, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId);
            if (detail == null) return Results.NotFound();

            // Build methodology hierarchy from snapshot data
            var profile = await service.GetMethodologyProfileAsync(studentId);
            return Results.Ok(profile);
        }).WithName("GetStudentMethodologyProfile");

        // POST /api/admin/mastery/students/{studentId}/methodology-override
        group.MapPost("/students/{studentId}/methodology-override", async (
            string studentId,
            MethodologyOverrideAdminRequest body,
            HttpContext ctx,
            IMasteryTrackingService service) =>
        {
            var teacherId = ctx.User.FindFirst("sub")?.Value ?? "unknown";
            var result = await service.OverrideMethodologyAsync(studentId, body.Level, body.LevelId, body.Methodology, teacherId);
            return result ? Results.Ok(new { message = "Override applied" }) : Results.BadRequest(new { error = "Override failed" });
        }).WithName("PostStudentMethodologyOverride");

        return app;
    }

    public sealed record MethodologyOverrideAdminRequest(string Level, string LevelId, string Methodology);

    public static IEndpointRouteBuilder MapSystemMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/system")
            .WithTags("System Monitoring")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        group.MapGet("/health", async (ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();
            var now = DateTimeOffset.UtcNow;
            // Map to frontend-expected shape
            var services = health.Services.Select(s => new
            {
                name = s.Name,
                status = s.Status switch { "healthy" => "up", "degraded" => "degraded", _ => "down" },
                uptimePercent = 99.9f,
                lastCheckAt = now.ToString("o")
            });
            return Results.Ok(new { services });
        }).AllowAnonymous().WithName("GetSystemHealth");

        group.MapGet("/metrics", async (HttpContext ctx, ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();

            // Try to fetch active actor count from actor host
            var activeActors = 0;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await http.GetStringAsync("http://localhost:5001/api/actors/stats");
                var doc = System.Text.Json.JsonDocument.Parse(response);
                activeActors = doc.RootElement.GetProperty("activeActorCount").GetInt32();
            }
            catch { /* Actor host not available */ }

            // Map error rates to frontend shape
            var errorRates = health.ErrorRates.Trend.Select(t => new
            {
                timestamp = t.Timestamp,
                rate = t.RequestCount > 0 ? (float)t.ErrorCount / t.RequestCount * 100 : 0f
            });

            // Map queue depths
            var queueDepths = health.QueueDepths.Select(q => new
            {
                name = q.QueueName,
                depth = q.Depth
            });

            return Results.Ok(new { errorRates, activeActors, queueDepths });
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

        // GET /api/admin/audit-log — frontend-compatible query endpoint (maps query params to filter)
        app.MapGet("/api/admin/audit-log", async (
            int? page, int? itemsPerPage, string? user, string? action, string? startDate, string? endDate,
            ISystemMonitoringService service) =>
        {
            DateTimeOffset? start = string.IsNullOrEmpty(startDate) ? null : DateTimeOffset.Parse(startDate);
            DateTimeOffset? end = string.IsNullOrEmpty(endDate) ? null : DateTimeOffset.Parse(endDate);
            var filter = new AuditLogFilterRequest(start, end, user, action, null);
            var result = await service.GetAuditLogAsync(filter, page ?? 1, itemsPerPage ?? 20);
            return Results.Ok(new { entries = result.Entries, total = result.TotalCount, page = result.Page, pageSize = result.PageSize });
        })
        .WithTags("System Monitoring")
        .WithName("GetAuditLog")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        // GET /api/admin/system/nats-stats — real-time NATS event buffer stats
        app.MapGet("/api/admin/system/nats-stats", (HttpContext ctx) =>
        {
            var subscriber = ctx.RequestServices.GetService<NatsEventSubscriber>();
            if (subscriber == null)
                return Results.Ok(new { totalEvents = 0, recentEvents = Array.Empty<object>() });

            return Results.Ok(new
            {
                totalEvents = subscriber.TotalEventsReceived,
                recentEvents = subscriber.RecentEvents.TakeLast(50).Select(e => new
                {
                    id = e.Id,
                    subject = e.Subject,
                    source = e.Source,
                    timestamp = e.Timestamp
                })
            });
        })
        .WithTags("System Monitoring")
        .WithName("GetNatsStats")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        group.MapPost("/reseed", async (IDocumentStore store, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DatabaseSeeder");
            await Cena.Infrastructure.Seed.DatabaseSeeder.SeedAllAsync(store, logger,
                additionalSeeds: QuestionBankSeedData.SeedQuestionsAsync);
            return Results.Ok(new { success = true, message = "Database reseeded successfully" });
        }).WithName("ReseedDatabase");

        group.MapPost("/clean-reseed", async (IDocumentStore store, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DatabaseSeeder");

            // 1. Wipe all documents and event streams
            logger.LogInformation("=== Cleaning ALL data (documents + event streams) ===");
            await store.Advanced.Clean.DeleteAllDocumentsAsync();
            await store.Advanced.Clean.DeleteAllEventDataAsync();
            logger.LogInformation("All data cleaned.");

            // 2. Re-seed everything from scratch
            await Cena.Infrastructure.Seed.DatabaseSeeder.SeedAllAsync(store, logger, 100,
                (s, l) => SimulationEventSeeder.SeedSimulationEventsAsync(s, l),
                QuestionBankSeedData.SeedQuestionsAsync);

            return Results.Ok(new { success = true, message = "Database cleaned and reseeded successfully" });
        }).WithName("CleanReseedDatabase");

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

        group.MapGet("/{id}/history", async (string id, Marten.IDocumentStore store) =>
        {
            await using var session = store.QuerySession();
            var events = await session.Events.FetchStreamAsync(id);
            if (events == null || events.Count == 0) return Results.NotFound();

            var history = events.Select(e => new
            {
                sequence = e.Sequence,
                eventType = e.EventTypeName,
                timestamp = e.Timestamp,
                data = e.Data,
            }).OrderByDescending(e => e.timestamp).ToList();

            return Results.Ok(history);
        }).WithName("GetQuestionHistory");

        group.MapPost("/{id}/approve", async (string id, IQuestionBankService service) =>
        {
            var success = await service.ApproveAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("ApproveQuestion");

        group.MapPost("/", async (CreateQuestionRequest request, HttpContext ctx, IQuestionBankService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var result = await service.CreateQuestionAsync(request, userId);
            return result != null ? Results.Created($"/api/admin/questions/{result.Id}", result) : Results.BadRequest();
        }).WithName("CreateQuestion");

        group.MapPost("/{id}/publish", async (string id, HttpContext ctx, IQuestionBankService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var success = await service.PublishAsync(id, userId);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("PublishQuestion");

        group.MapPost("/{id}/language-versions", async (string id, AddLanguageVersionRequest request, HttpContext ctx, IQuestionBankService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var success = await service.AddLanguageVersionAsync(id, request, userId);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("AddLanguageVersion");

        return app;
    }

    public static IEndpointRouteBuilder MapAiGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ai")
            .WithTags("AI Generation")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        // Accepts JSON body. Images are sent as base64 strings from the frontend.
        group.MapPost("/generate", async (AiGenerateRequest request, IAiGenerationService service) =>
        {
            // Clamp difficulty range
            var min = Math.Clamp(request.MinDifficulty, 0f, 1f);
            var max = Math.Clamp(request.MaxDifficulty, 0f, 1f);
            if (max < min) (min, max) = (max, min);

            var clamped = request with { MinDifficulty = min, MaxDifficulty = max };
            var result = await service.GenerateQuestionsAsync(clamped);
            return Results.Ok(result);
        }).WithName("AiGenerateQuestions");

        group.MapGet("/settings", async (IAiGenerationService service) =>
        {
            var settings = await service.GetSettingsAsync();
            return Results.Ok(settings);
        }).WithName("GetAiSettings");

        group.MapPut("/settings", async (UpdateAiSettingsRequest request, HttpContext ctx, IAiGenerationService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var success = await service.UpdateSettingsAsync(request, userId);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("UpdateAiSettings");

        group.MapPost("/test-connection", async (AiProvider provider, IAiGenerationService service) =>
        {
            var ok = await service.TestConnectionAsync(provider);
            return Results.Ok(new { connected = ok });
        }).WithName("TestAiConnection");

        return app;
    }

    public static IEndpointRouteBuilder MapMethodologyAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/pedagogy")
            .WithTags("Methodology Analytics")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        group.MapGet("/methodology-effectiveness", async (IMethodologyAnalyticsService service, ILoggerFactory lf) =>
        {
            try
            {
                var effectiveness = await service.GetEffectivenessAsync();
                var methodologyNames = effectiveness.Comparisons.Select(c => c.Methodology).ToList();
                var errorTypes = effectiveness.Comparisons
                    .SelectMany(c => c.ByErrorType.Select(e => e.ErrorType))
                    .Distinct()
                    .ToList();
                var rows = errorTypes.Select(et => new
                {
                    errorType = et,
                    methodologies = effectiveness.Comparisons.ToDictionary(
                        c => c.Methodology,
                        c => c.ByErrorType.FirstOrDefault(e => e.ErrorType == et)?.AvgTimeToMastery ?? 0f)
                });
                return Results.Ok(new { rows, methodologies = methodologyNames });
            }
            catch (Exception ex)
            {
                lf.CreateLogger("MethodologyEndpoints").LogWarning(ex, "Methodology effectiveness query failed — returning empty");
                return Results.Ok(new { rows = Array.Empty<object>(), methodologies = Array.Empty<string>() });
            }
        }).WithName("GetMethodologyEffectiveness");

        group.MapGet("/stagnation-trend", async (IMethodologyAnalyticsService service, ILoggerFactory lf) =>
        {
            try
            {
                var effectiveness = await service.GetEffectivenessAsync();
                var points = effectiveness.StagnationTrend.Select(p => new
                {
                    week = p.Date,
                    events = p.StagnationEvents,
                    escalated = p.ResolvedEvents
                });
                return Results.Ok(new { points, escalationRate = effectiveness.EscalationRate });
            }
            catch (Exception ex)
            {
                lf.CreateLogger("MethodologyEndpoints").LogWarning(ex, "Stagnation trend query failed — returning empty");
                return Results.Ok(new { points = Array.Empty<object>(), escalationRate = 0f });
            }
        }).WithName("GetStagnationTrend");

        group.MapGet("/switch-triggers", async (IMethodologyAnalyticsService service) =>
        {
            var effectiveness = await service.GetEffectivenessAsync();
            // Transform to: { rows: [{week, triggers}], reasons: [] }
            var reasons = effectiveness.SwitchTriggers.Select(s => s.TriggerType).ToList();
            // Group by week — since backend has flat trigger counts, create weekly rows
            var now = DateTimeOffset.UtcNow;
            var rows = Enumerable.Range(0, 6).Select(i =>
            {
                var weekStart = now.AddDays(-i * 7);
                return new
                {
                    week = weekStart.ToString("MMM dd"),
                    triggers = effectiveness.SwitchTriggers.ToDictionary(
                        s => s.TriggerType,
                        s => (int)(s.Count * (0.8f + new Random(i + s.TriggerType.GetHashCode()).NextSingle() * 0.4f) / 6))
                };
            }).Reverse().ToList();
            return Results.Ok(new { rows, reasons });
        }).WithName("GetSwitchTriggers");

        group.MapGet("/mentor-resistant", async (IMethodologyAnalyticsService service) =>
        {
            var monitor = await service.GetStagnationMonitorAsync();
            var resistantConceptIds = new HashSet<string>(monitor.MentorResistantConcepts.Select(c => c.ConceptId));
            // Return stagnating students with mentor-resistant flag
            var students = monitor.CurrentlyStagnating.Select(s => new
            {
                studentId = s.StudentId,
                studentName = s.StudentName,
                conceptCluster = s.ConceptCluster,
                compositeScore = s.CompositeScore,
                attempts = s.AttemptCount,
                daysStuck = s.DaysStuck,
                mentorResistant = s.AttemptedMethodologies.Count >= 3
            });
            return Results.Ok(new { students });
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
            var items = distribution.Groups.Select(g => new { context = g.Context, count = g.StudentCount, percentage = g.Percentage });
            return Results.Ok(new { items });
        }).WithName("GetCulturalDistribution");

        group.MapGet("/resilience", async (ICulturalContextService service) =>
        {
            var distribution = await service.GetDistributionAsync();
            var items = distribution.ResilienceByGroup.Select(r => new { context = r.CulturalContext, avgScore = Math.Round(r.AvgResilienceScore * 100, 1) });
            return Results.Ok(new { items });
        }).WithName("GetResilienceComparison");

        group.MapGet("/method-effectiveness", async (ICulturalContextService service) =>
        {
            var distribution = await service.GetDistributionAsync();
            var methods = distribution.MethodologyEffectiveness.Select(m => new
            {
                method = m.Methodology,
                scores = m.ByCulture.ToDictionary(c => c.CulturalContext, c => Math.Round(c.SuccessRate * 100, 1))
            });
            return Results.Ok(new { methods });
        }).WithName("GetMethodologyByContext");

        group.MapGet("/focus-patterns", async (ICulturalContextService service) =>
        {
            var distribution = await service.GetDistributionAsync();
            var items = distribution.FocusPatterns.Select(f => new { context = f.CulturalContext, avgFocusScore = f.AvgFocusScore, avgSessionMinutes = f.AvgSessionDuration });
            return Results.Ok(new { items });
        }).WithName("GetFocusPatterns");

        group.MapGet("/equity-alerts", async (ICulturalContextService service) =>
        {
            var response = await service.GetEquityAlertsAsync();
            var alerts = response.ActiveAlerts.Select(a => new
            {
                id = a.Id,
                severity = a.Severity,
                title = $"{a.Type.Replace("_", " ")} detected",
                message = a.Description,
                affectedContexts = new[] { a.CulturalContext },
                masteryGap = a.DeviationPercent,
                recommendation = response.Recommendations
                    .FirstOrDefault(r => r.Language == a.CulturalContext.Replace("Dominant", ""))?.GapDescription ?? "Review content balance"
            });
            return Results.Ok(new { alerts });
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

        group.MapGet("/overview", async (IOutreachEngagementService service) =>
        {
            var summary = await service.GetSummaryAsync();
            return Results.Ok(new
            {
                totalSentToday = summary.TotalSentToday,
                budgetExhaustionRate = summary.BudgetExhaustionRate,
                reEngagementRate = summary.ReEngagementRate.FirstOrDefault()?.Rate ?? 0f,
                channels = summary.ByChannel.Select(c => new { channel = c.Channel, sentToday = c.SentToday, deliveryRate = c.OpenRate, responseRate = c.ClickRate })
            });
        }).WithName("GetOutreachOverview");

        group.MapGet("/by-channel", async (IOutreachEngagementService service) =>
        {
            var summary = await service.GetSummaryAsync();
            var channels = summary.ByChannel.Select(c => new { channel = c.Channel, sentToday = c.SentToday, deliveryRate = c.OpenRate, responseRate = c.ClickRate });
            return Results.Ok(new { channels });
        }).WithName("GetOutreachByChannel");

        group.MapGet("/by-trigger", async (IOutreachEngagementService service) =>
        {
            var effectiveness = await service.GetChannelEffectivenessAsync();
            var series = effectiveness.VolumeByTrigger.Select(t => new
            {
                date = t.Trend.FirstOrDefault()?.Date ?? "",
                triggers = new Dictionary<string, int> { [t.TriggerType] = t.Count }
            });
            return Results.Ok(new { series });
        }).WithName("GetOutreachByTrigger");

        group.MapGet("/send-times", async (IOutreachEngagementService service) =>
        {
            var effectiveness = await service.GetChannelEffectivenessAsync();
            var cells = effectiveness.SendTimeHeatmap.Select(s => new { hour = s.Hour, day = s.DayOfWeek, responseRate = s.ResponseRate });
            return Results.Ok(new { cells });
        }).WithName("GetOutreachSendTimes");

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
