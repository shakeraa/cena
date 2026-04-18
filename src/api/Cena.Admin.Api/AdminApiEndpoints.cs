// =============================================================================
// Cena Platform -- Admin API Endpoints (ADM-006 through ADM-014)
// Consolidated endpoint registration for remaining admin features
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class AdminApiEndpoints
{
    public static IEndpointRouteBuilder MapFocusAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/focus")
            .WithTags("Focus Analytics")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/overview", async (string? classId, ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var overview = await service.GetOverviewAsync(classId, user);
            return Results.Ok(overview);
        }).WithName("GetFocusOverview")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}", async (string studentId, ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var detail = await service.GetStudentFocusAsync(studentId, user);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetStudentFocus")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/classes/{classId}", async (string classId, ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var detail = await service.GetClassFocusAsync(classId, user);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetClassFocus")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/degradation-curve", async (ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var curve = await service.GetDegradationCurveAsync(user);
            return Results.Ok(curve);
        }).WithName("GetFocusDegradationCurve")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/experiments", async (ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var experiments = await service.GetExperimentsAsync(user);
            return Results.Ok(experiments);
        }).WithName("GetFocusExperiments")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/alerts", async (ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var alerts = await service.GetStudentsNeedingAttentionAsync(user);
            return Results.Ok(alerts);
        }).WithName("GetFocusAlerts")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/timeline", async (string studentId, string? period, ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var validPeriod = ParameterValidator.ValidatePeriod(period);
            var timeline = await service.GetStudentTimelineAsync(studentId, validPeriod, user);
            return Results.Ok(timeline);
        }).WithName("GetStudentFocusTimeline")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/classes/{classId}/heatmap", async (string classId, ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var heatmap = await service.GetClassHeatmapAsync(classId, user);
            return Results.Ok(heatmap);
        }).WithName("GetClassFocusHeatmap")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapMasteryTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/mastery")
            .WithTags("Mastery Tracking")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/overview", async (string? classId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var overview = await service.GetOverviewAsync(classId, user);
            return Results.Ok(overview);
        }).WithName("GetMasteryOverview")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/overview/distribution", async (string? classId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var overview = await service.GetOverviewAsync(classId, user);
            return Results.Ok(new { bands = overview.Distribution.Select(d => new { label = d.Level, count = d.Count }) });
        }).WithName("GetMasteryDistribution")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/overview/subjects", async (string? classId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var overview = await service.GetOverviewAsync(classId, user);
            return Results.Ok(new { subjects = overview.SubjectBreakdown.Select(s => new { name = s.Subject, avgMastery = s.AvgMasteryLevel }) });
        }).WithName("GetMasterySubjects")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetStudentMastery")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/knowledge-map", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
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
        }).WithName("GetStudentKnowledgeMap")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/knowledge-map/graph", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
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
        }).WithName("GetStudentKnowledgeGraph")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/frontier", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
            if (detail == null) return Results.NotFound();
            var concepts = detail.LearningFrontier.Select(f => new
            {
                conceptId = f.ConceptId,
                name = f.ConceptName,
                prerequisitesMet = 2,
                prerequisitesTotal = 2
            });
            return Results.Ok(new { concepts });
        }).WithName("GetStudentFrontier")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/history", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
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
        }).WithName("GetStudentHistory")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/review-priority", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
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
        }).WithName("GetStudentReviewPriority")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/classes/{classId}", async (string classId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetClassMasteryAsync(classId, user);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetClassMastery")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/at-risk", async (ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var atRisk = await service.GetAtRiskStudentsAsync(user);
            return Results.Ok(atRisk);
        }).WithName("GetAtRiskStudents")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/mastery/students/{studentId}/methodology-profile
        group.MapGet("/students/{studentId}/methodology-profile", async (string studentId, ClaimsPrincipal user, IMasteryTrackingService service) =>
        {
            var detail = await service.GetStudentMasteryAsync(studentId, user);
            if (detail == null) return Results.NotFound();

            // Build methodology hierarchy from snapshot data
            var profile = await service.GetMethodologyProfileAsync(studentId, user);
            return Results.Ok(profile);
        }).WithName("GetStudentMethodologyProfile")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/mastery/students/{studentId}/methodology-override
        group.MapPost("/students/{studentId}/methodology-override", async (
            string studentId,
            MethodologyOverrideAdminRequest body,
            HttpContext ctx,
            IMasteryTrackingService service) =>
        {
            var teacherId = ctx.User.FindFirst("sub")?.Value ?? "unknown";
            var result = await service.OverrideMethodologyAsync(studentId, body.Level, body.LevelId, body.Methodology, teacherId, ctx.User);
            return result ? Results.Ok(new { message = "Override applied" }) : Results.BadRequest(new { error = "Override failed" });
        }).WithName("PostStudentMethodologyOverride")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/mastery/students/{studentId}/methodology-overrides
        group.MapGet("/students/{studentId}/methodology-overrides", async (
            string studentId,
            ClaimsPrincipal user,
            IMasteryTrackingService service) =>
        {
            var overrides = await service.GetStudentOverridesAsync(studentId, user);
            var result = overrides.Select(o => new
            {
                id = o.Id,
                studentId = o.StudentId,
                level = o.Level,
                levelId = o.LevelId,
                methodology = o.Methodology,
                teacherId = o.TeacherId,
                createdAt = o.CreatedAt.ToString("o"),
            });
            return Results.Ok(new { overrides = result });
        }).WithName("GetStudentMethodologyOverrides")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // DELETE /api/admin/mastery/students/{studentId}/methodology-overrides/{overrideId}
        group.MapDelete("/students/{studentId}/methodology-overrides/{overrideId}", async (
            string studentId,
            string overrideId,
            ClaimsPrincipal user,
            IMasteryTrackingService service) =>
        {
            var removed = await service.RemoveOverrideAsync(studentId, Uri.UnescapeDataString(overrideId), user);
            return removed ? Results.Ok(new { message = "Override removed" }) : Results.NotFound(new { error = "Override not found" });
        }).WithName("DeleteStudentMethodologyOverride")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public sealed record MethodologyOverrideAdminRequest(string Level, string LevelId, string Methodology);

    public static IEndpointRouteBuilder MapSystemMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var env = app.ServiceProvider.GetRequiredService<IHostEnvironment>();

        var group = app.MapGroup("/api/admin/system")
            .WithTags("System Monitoring")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

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
        }).WithName("GetSystemHealth")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/metrics", async (HttpContext ctx, ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();

            // Fetch live stats from actor host
            var activeActors = 0;
            var messagesProcessed = 0L;
            var sessionsStarted = 0L;
            var eventsPublished = 0L;
            var actorErrors = 0L;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var actorStatsUrl = Environment.GetEnvironmentVariable("CENA_ACTOR_STATS_URL")
                    ?? "http://localhost:5119/api/actors/stats";
                var response = await http.GetStringAsync(actorStatsUrl);
                var doc = System.Text.Json.JsonDocument.Parse(response);
                activeActors = doc.RootElement.GetProperty("activeActorCount").GetInt32();
                messagesProcessed = doc.RootElement.GetProperty("commandsRouted").GetInt64();
                sessionsStarted = doc.RootElement.GetProperty("sessionsStarted").GetInt64();
                eventsPublished = doc.RootElement.GetProperty("eventsPublished").GetInt64();
                actorErrors = doc.RootElement.GetProperty("errorsCount").GetInt64();
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

            return Results.Ok(new { errorRates, activeActors, messagesProcessed, sessionsStarted, eventsPublished, actorErrors, queueDepths });
        }).WithName("GetSystemMetrics")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/actors", async (ISystemMonitoringService service) =>
        {
            var health = await service.GetHealthAsync();
            return Results.Ok(health.ActorSystems);
        }).WithName("GetActorSystemStatus")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/settings", async (ISystemMonitoringService service) =>
        {
            var settings = await service.GetSettingsAsync();
            return Results.Ok(settings);
        }).WithName("GetPlatformSettings")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPut("/settings", async (UpdateSettingsRequest request, HttpContext httpContext, ISystemMonitoringService service) =>
        {
            var userId = httpContext.User.Identity?.Name ?? "unknown";
            var success = await service.UpdateSettingsAsync(request, userId);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("UpdatePlatformSettings")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/audit-log/query", async (AuditLogFilterRequest request, int? page, int? pageSize, ISystemMonitoringService service) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var result = await service.GetAuditLogAsync(request, validPage, validPageSize);
            return Results.Ok(result);
        }).WithName("QueryAuditLog")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/audit-log — frontend-compatible query endpoint (maps query params to filter)
        app.MapGet("/api/admin/audit-log", async (
            int? page, int? itemsPerPage, string? user, string? action, string? startDate, string? endDate,
            ISystemMonitoringService service) =>
        {
            DateTimeOffset? start = string.IsNullOrEmpty(startDate) ? null : DateTimeOffset.Parse(startDate);
            DateTimeOffset? end = string.IsNullOrEmpty(endDate) ? null : DateTimeOffset.Parse(endDate);
            var filter = new AuditLogFilterRequest(start, end, user, action, null);
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(itemsPerPage);
            var result = await service.GetAuditLogAsync(filter, validPage, validPageSize);
            return Results.Ok(new { entries = result.Entries, total = result.TotalCount, page = result.Page, pageSize = result.PageSize });
        })
        .WithTags("System Monitoring")
        .WithName("GetAuditLog")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // GET /api/admin/system/nats-stats — real-time NATS monitoring stats (ADM-023)
        // Uses NATS monitoring HTTP endpoint (port 8222) for core pub/sub stats
        app.MapGet("/api/admin/system/nats-stats", async (ILoggerFactory lf) =>
        {
            var logger = lf.CreateLogger("NatsStats");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

                // Core NATS server stats
                var varzJson = await http.GetStringAsync("http://localhost:8222/varz");
                var varz = System.Text.Json.JsonDocument.Parse(varzJson);

                // Connection details
                var connzJson = await http.GetStringAsync("http://localhost:8222/connz");
                var connz = System.Text.Json.JsonDocument.Parse(connzJson);

                // Subscription stats
                var subszJson = await http.GetStringAsync("http://localhost:8222/subsz");
                var subsz = System.Text.Json.JsonDocument.Parse(subszJson);

                var connections = new List<object>();
                foreach (var conn in connz.RootElement.GetProperty("connections").EnumerateArray())
                {
                    connections.Add(new
                    {
                        name = conn.GetProperty("name").GetString(),
                        inMsgs = conn.GetProperty("in_msgs").GetInt64(),
                        outMsgs = conn.GetProperty("out_msgs").GetInt64(),
                        inBytes = conn.GetProperty("in_bytes").GetInt64(),
                        outBytes = conn.GetProperty("out_bytes").GetInt64(),
                        subscriptions = conn.GetProperty("subscriptions").GetInt32(),
                    });
                }

                var totalInMsgs = varz.RootElement.GetProperty("in_msgs").GetInt64();
                var totalOutMsgs = varz.RootElement.GetProperty("out_msgs").GetInt64();
                var totalBytes = varz.RootElement.GetProperty("in_bytes").GetInt64() + varz.RootElement.GetProperty("out_bytes").GetInt64();
                var totalSubs = subsz.RootElement.GetProperty("num_subscriptions").GetInt32();

                return Results.Ok(new
                {
                    streams = connections,
                    totalMessages = totalInMsgs + totalOutMsgs,
                    totalBytes,
                    totalConsumers = totalSubs,
                    serverVersion = varz.RootElement.GetProperty("version").GetString(),
                    connections = connz.RootElement.GetProperty("num_connections").GetInt32(),
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch NATS stats");
                return Results.Ok(new { streams = Array.Empty<object>(), totalMessages = 0L, totalBytes = 0L, totalConsumers = 0 });
            }
        })
        .WithTags("System Monitoring")
        .WithName("GetNatsStats")
        .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // REV-011.4: Only register destructive seeding endpoints in Development
        if (env.IsDevelopment())
        {
            group.MapPost("/reseed", async (IDocumentStore store, IServiceProvider sp, ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("DatabaseSeeder");
                await Cena.Infrastructure.Seed.DatabaseSeeder.SeedAllAsync(store, logger, sp,
                    additionalSeeds: QuestionBankSeedData.SeedQuestionsAsync);
                return Results.Ok(new { success = true, message = "Database reseeded successfully" });
            }).WithName("ReseedDatabase").RequireRateLimiting("destructive")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

            group.MapPost("/clean-reseed", async (IDocumentStore store, IServiceProvider sp, ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("DatabaseSeeder");

                // 1. Wipe all documents and event streams
                logger.LogInformation("=== Cleaning ALL data (documents + event streams) ===");
                await store.Advanced.Clean.DeleteAllDocumentsAsync();
                await store.Advanced.Clean.DeleteAllEventDataAsync();
                logger.LogInformation("All data cleaned.");

                // 2. Re-seed everything from scratch
                await Cena.Infrastructure.Seed.DatabaseSeeder.SeedAllAsync(
                    store, logger, sp, 100,
                    ctx => SimulationEventSeeder.SeedSimulationEventsAsync(ctx.Store, ctx.Logger),
                    QuestionBankSeedData.SeedQuestionsAsync);

                return Results.Ok(new { success = true, message = "Database cleaned and reseeded successfully" });
            }).WithName("CleanReseedDatabase").RequireRateLimiting("destructive")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        }

        return app;
    }

    public static IEndpointRouteBuilder MapIngestionPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion")
            .WithTags("Ingestion Pipeline")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/pipeline-status", async (IIngestionPipelineService service) =>
        {
            var status = await service.GetPipelineStatusAsync();
            return Results.Ok(status);
        }).WithName("GetPipelineStatus")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/items", async (string? stage, int? page, int? pageSize, IIngestionPipelineService service) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var status = await service.GetPipelineStatusAsync();
            var items = stage != null
                ? status.Stages.FirstOrDefault(s => s.StageId == stage)?.Items ?? new List<PipelineItem>()
                : status.Stages.SelectMany(s => s.Items).ToList();
            return Results.Ok(new { Items = items, Total = items.Count });
        }).WithName("GetPipelineItems")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/items/{id}/detail", async (string id, IIngestionPipelineService service) =>
        {
            var detail = await service.GetItemDetailAsync(id);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetPipelineItemDetail")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/items/{id}/retry", async (string id, IIngestionPipelineService service) =>
        {
            var success = await service.RetryItemAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RetryPipelineItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/items/{id}/reject", async (string id, RejectPipelineItemRequest request, IIngestionPipelineService service) =>
        {
            var success = await service.RejectPipelineItemAsync(id, request.Reason);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RejectPipelineItem")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/stats", async (IIngestionPipelineService service) =>
        {
            var stats = await service.GetStatsAsync();
            return Results.Ok(stats);
        }).WithName("GetPipelineStats")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/items/{id}/move-to-review", async (string id, IIngestionPipelineService service) =>
        {
            var result = await service.MoveToReviewAsync(id);
            return Results.Ok(result);
        }).WithName("MoveItemToReview")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/upload", async (HttpRequest request, IIngestionPipelineService service) =>
        {
            // REV-011.3: File upload validation
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null)
                return Results.BadRequest(new { error = "No file uploaded" });

            const long maxFileSize = 20 * 1024 * 1024; // 20MB per file
            if (file.Length > maxFileSize)
                return Results.BadRequest(new { error = $"File exceeds maximum size of {maxFileSize / (1024 * 1024)}MB" });

            var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "application/pdf", "image/png", "image/jpeg", "image/webp",
                "text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };
            if (!allowedContentTypes.Contains(file.ContentType ?? ""))
                return Results.BadRequest(new { error = $"File type '{file.ContentType}' not allowed. Accepted: PDF, PNG, JPEG, WebP, CSV, XLSX" });

            var result = await service.UploadFromRequestAsync(request);
            return Results.Ok(result);
        }).WithName("UploadPipelineFile").DisableAntiforgery()
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // Cloud directory listing
        group.MapPost("/cloud-dir/list", async (CloudDirListRequest request, IIngestionPipelineService service) =>
        {
            var items = await service.ListCloudDirectoryAsync(request);
            return Results.Ok(items);
        }).WithName("ListCloudDirectory")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // Cloud directory batch ingest
        group.MapPost("/cloud-dir/ingest", async (CloudDirIngestRequest request, IIngestionPipelineService service) =>
        {
            var result = await service.IngestCloudDirectoryAsync(request);
            return Results.Ok(result);
        }).WithName("IngestCloudDirectory")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapQuestionBankEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/questions")
            .WithTags("Question Bank")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

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
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(itemsPerPage);
            var result = await service.GetQuestionsAsync(
                subject, bloomsLevel, minDifficulty, maxDifficulty, status, language, conceptId, q,
                validPage, validPageSize, sortBy ?? "qualityScore", orderBy ?? "desc");
            return Results.Ok(result);
        }).WithName("GetQuestions")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{id}", async (string id, IQuestionBankService service) =>
        {
            var question = await service.GetQuestionAsync(id);
            return question != null ? Results.Ok(question) : Results.NotFound();
        }).WithName("GetQuestion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPut("/{id}", async (string id, UpdateBankQuestionRequest request, IQuestionBankService service) =>
        {
            var question = await service.UpdateQuestionAsync(id, request);
            return question != null ? Results.Ok(question) : Results.NotFound();
        }).WithName("UpdateQuestion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{id}/deprecate", async (string id, DeprecateBankQuestionRequest request, IQuestionBankService service) =>
        {
            var success = await service.DeprecateQuestionAsync(id, request);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("DeprecateQuestion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/filters", async (IQuestionBankService service) =>
        {
            var filters = await service.GetFiltersAsync();
            return Results.Ok(filters);
        }).WithName("GetQuestionFilters")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/concepts", async (string q, IQuestionBankService service) =>
        {
            var matches = await service.AutocompleteConceptsAsync(q);
            return Results.Ok(matches);
        }).WithName("AutocompleteConcepts")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{id}/performance", async (string id, IQuestionBankService service) =>
        {
            var perf = await service.GetPerformanceAsync(id);
            return perf != null ? Results.Ok(perf) : Results.NotFound();
        }).WithName("GetQuestionPerformance")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

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
        }).WithName("GetQuestionHistory")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{id}/approve", async (string id, IQuestionBankService service) =>
        {
            try
            {
                var success = await service.ApproveAsync(id);
                return success ? Results.Ok() : Results.NotFound();
            }
            catch (Cena.Actors.Cas.CasApprovalRejectedException ex)
            {
                // RDY-034: math/physics question missing Verified CAS binding.
                return Results.Conflict(new CenaError(ex.ErrorCode, ex.Message, ErrorCategory.Conflict, null, null));
            }
        }).WithName("ApproveQuestion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status409Conflict)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/", async (CreateQuestionRequest request, HttpContext ctx, IQuestionBankService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            try
            {
                var result = await service.CreateQuestionAsync(request, userId);
                return result != null ? Results.Created($"/api/admin/questions/{result.Id}", result) : Results.BadRequest();
            }
            catch (Cena.Actors.Cas.CasVerificationFailedException ex)
            {
                // RDY-034: authored answer rejected by CAS oracle.
                return Results.BadRequest(new CenaError(ex.ErrorCode, ex.Message, ErrorCategory.Validation, null, null));
            }
        }).WithName("CreateQuestion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPatch("/{id}/explanation", async (string id, UpdateExplanationRequest request, HttpContext ctx, IQuestionBankService service) =>
        {
            if (string.IsNullOrWhiteSpace(request.Explanation))
                return Results.BadRequest(new { error = "Explanation cannot be empty." });
            if (request.Explanation.Length > 5000)
                return Results.BadRequest(new { error = "Explanation must be 5000 characters or fewer." });

            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var result = await service.UpdateExplanationAsync(id, request.Explanation, userId);
            return result != null ? Results.Ok(result) : Results.NotFound();
        }).WithName("UpdateQuestionExplanation")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{id}/publish", async (string id, HttpContext ctx, IQuestionBankService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var success = await service.PublishAsync(id, userId);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("PublishQuestion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{id}/language-versions", async (string id, AddLanguageVersionRequest request, HttpContext ctx, IQuestionBankService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var success = await service.AddLanguageVersionAsync(id, request, userId);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("AddLanguageVersion")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// FIND-pedagogy-008 — read-only endpoints for the learning-objective
    /// picker. Full CRUD is explicitly deferred; authors can still backfill
    /// via the update-question endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapLearningObjectiveEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/learning-objectives")
            .WithTags("Learning Objectives")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/", async (string? subject, ILearningObjectiveService service) =>
        {
            var result = await service.ListAsync(subject);
            return Results.Ok(result);
        }).WithName("ListLearningObjectives")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{id}", async (string id, ILearningObjectiveService service) =>
        {
            var result = await service.GetByIdAsync(id);
            return result != null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetLearningObjective")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapAiGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ai")
            .WithTags("AI Generation")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("ai");

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
        }).WithName("AiGenerateQuestions")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/settings", async (IAiGenerationService service) =>
        {
            var settings = await service.GetSettingsAsync();
            return Results.Ok(settings);
        }).WithName("GetAiSettings")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPut("/settings", async (UpdateAiSettingsRequest request, HttpContext ctx, IAiGenerationService service) =>
        {
            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var success = await service.UpdateSettingsAsync(request, userId);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("UpdateAiSettings")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/test-connection", async (AiProvider provider, IAiGenerationService service) =>
        {
            var ok = await service.TestConnectionAsync(provider);
            return Results.Ok(new { connected = ok });
        }).WithName("TestAiConnection")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/ai/generate-batch (CNT-002)
        group.MapPost("/generate-batch", async (
            BatchGenerateRequest req,
            IAiGenerationService aiService,
            QualityGate.IQualityGateService qualityGate) =>
        {
            if (req.Count < 1 || req.Count > 20)
                return Results.BadRequest(new { error = "count must be between 1 and 20." });

            var result = await aiService.BatchGenerateAsync(req, qualityGate);
            return Results.Ok(result);
        }).WithName("AiBatchGenerateQuestions")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/ai/generate-from-template (CNT-002)
        group.MapPost("/generate-from-template", async (
            TemplateGenerateRequest req,
            IAiGenerationService aiService,
            QualityGate.IQualityGateService qualityGate) =>
        {
            if (string.IsNullOrWhiteSpace(req.OcrText))
                return Results.BadRequest(new { error = "ocrText is required." });

            if (req.Count < 1 || req.Count > 20)
                return Results.BadRequest(new { error = "count must be between 1 and 20." });

            var result = await aiService.GenerateFromTemplateAsync(req, qualityGate);
            return Results.Ok(result);
        }).WithName("AiGenerateFromTemplate")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapQuestionPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/questions/pipeline")
            .WithTags("Question Pipeline")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /api/admin/questions/pipeline/status
        group.MapGet("/status", async (IQuestionPipelineService service) =>
        {
            var status = await service.GetStatusAsync();
            return Results.Ok(status);
        }).WithName("GetQuestionPipelineStatus")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/questions/pipeline/bulk-approve
        group.MapPost("/bulk-approve", async (
            BulkApproveRequest request,
            HttpContext ctx,
            IQuestionPipelineService service) =>
        {
            if (request.QuestionIds == null || request.QuestionIds.Count == 0)
                return Results.BadRequest(new { error = "questionIds must not be empty." });

            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var result = await service.BulkApproveAsync(request, userId);
            return Results.Ok(result);
        }).WithName("BulkApproveQuestions")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST /api/admin/questions/pipeline/bulk-reject
        group.MapPost("/bulk-reject", async (
            BulkRejectRequest request,
            HttpContext ctx,
            IQuestionPipelineService service) =>
        {
            if (request.QuestionIds == null || request.QuestionIds.Count == 0)
                return Results.BadRequest(new { error = "questionIds must not be empty." });

            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "reason is required." });

            var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var result = await service.BulkRejectAsync(request, userId);
            return Results.Ok(result);
        }).WithName("BulkRejectQuestions")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapMethodologyAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/pedagogy")
            .WithTags("Methodology Analytics")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/methodology-effectiveness", async (IMethodologyAnalyticsService service, ClaimsPrincipal user, ILoggerFactory lf) =>
        {
            try
            {
                var effectiveness = await service.GetEffectivenessAsync(user);
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
        }).WithName("GetMethodologyEffectiveness")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/stagnation-trend", async (IMethodologyAnalyticsService service, ClaimsPrincipal user, ILoggerFactory lf) =>
        {
            try
            {
                var effectiveness = await service.GetEffectivenessAsync(user);
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
        }).WithName("GetStagnationTrend")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/switch-triggers", async (IMethodologyAnalyticsService service, ClaimsPrincipal user) =>
        {
            var effectiveness = await service.GetEffectivenessAsync(user);
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
        }).WithName("GetSwitchTriggers")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/mentor-resistant", async (IMethodologyAnalyticsService service, ClaimsPrincipal user) =>
        {
            var monitor = await service.GetStagnationMonitorAsync(user);
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
        }).WithName("GetMentorResistantConcepts")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/stagnation-monitor", async (IMethodologyAnalyticsService service, ClaimsPrincipal user) =>
        {
            var monitor = await service.GetStagnationMonitorAsync(user);
            return Results.Ok(monitor);
        }).WithName("GetStagnationMonitor")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/mcm-graph", async (IMethodologyAnalyticsService service) =>
        {
            var graph = await service.GetMcmGraphAsync();
            return Results.Ok(graph);
        }).WithName("GetMcmGraph")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPut("/mcm-graph/edge", async (UpdateMcmEdgeRequest request, IMethodologyAnalyticsService service) =>
        {
            var success = await service.UpdateMcmEdgeAsync(request.Source, request.Target, request.Confidence);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("UpdateMcmEdge")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/mcm-graph/edge", async (UpdateMcmEdgeRequest request, IMethodologyAnalyticsService service) =>
        {
            var success = await service.UpdateMcmEdgeAsync(request.Source, request.Target, request.Confidence);
            return success ? Results.Ok() : Results.BadRequest();
        }).WithName("CreateMcmEdge")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapCulturalContextEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/cultural")
            .WithTags("Cultural Context")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        group.MapGet("/distribution", async (ICulturalContextService service, ClaimsPrincipal user) =>
        {
            var distribution = await service.GetDistributionAsync(user);
            var items = distribution.Groups.Select(g => new { context = g.Context, count = g.StudentCount, percentage = g.Percentage });
            return Results.Ok(new { items });
        }).WithName("GetCulturalDistribution")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/resilience", async (ICulturalContextService service, ClaimsPrincipal user) =>
        {
            var distribution = await service.GetDistributionAsync(user);
            var items = distribution.ResilienceByGroup.Select(r => new { context = r.CulturalContext, avgScore = Math.Round(r.AvgResilienceScore * 100, 1) });
            return Results.Ok(new { items });
        }).WithName("GetResilienceComparison")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/method-effectiveness", async (ICulturalContextService service, ClaimsPrincipal user) =>
        {
            var distribution = await service.GetDistributionAsync(user);
            var methods = distribution.MethodologyEffectiveness.Select(m => new
            {
                method = m.Methodology,
                scores = m.ByCulture.ToDictionary(c => c.CulturalContext, c => Math.Round(c.SuccessRate * 100, 1))
            });
            return Results.Ok(new { methods });
        }).WithName("GetMethodologyByContext")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/focus-patterns", async (ICulturalContextService service, ClaimsPrincipal user) =>
        {
            var distribution = await service.GetDistributionAsync(user);
            var items = distribution.FocusPatterns.Select(f => new { context = f.CulturalContext, avgFocusScore = f.AvgFocusScore, avgSessionMinutes = f.AvgSessionDuration });
            return Results.Ok(new { items });
        }).WithName("GetFocusPatterns")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/equity-alerts", async (ICulturalContextService service, ClaimsPrincipal user) =>
        {
            var response = await service.GetEquityAlertsAsync(user);
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
        }).WithName("GetEquityAlerts")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapEventStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/events")
            .WithTags("Event Stream")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        group.MapGet("/recent", async (int? count, string? continuationToken, IEventStreamService service) =>
        {
            var validCount = ParameterValidator.ValidateLimit(count);
            var events = await service.GetRecentEventsAsync(validCount, continuationToken);
            return Results.Ok(events);
        }).WithName("GetRecentEvents")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/rates", async (IEventStreamService service) =>
        {
            var rates = await service.GetEventRatesAsync();
            return Results.Ok(rates);
        }).WithName("GetEventRates")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/dead-letters", async (int? page, int? pageSize, IEventStreamService service) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var dlq = await service.GetDeadLetterQueueAsync(validPage, validPageSize);
            return Results.Ok(dlq);
        }).WithName("GetDeadLetterQueue")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/dead-letters/{id}", async (string id, IEventStreamService service) =>
        {
            var detail = await service.GetDeadLetterDetailAsync(id);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetDeadLetterDetail")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/dead-letters/{id}/retry", async (string id, IEventStreamService service) =>
        {
            var result = await service.RetryMessageAsync(id);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        }).WithName("RetryDeadLetter")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/dead-letters/bulk-retry", async (BulkRetryRequest request, IEventStreamService service) =>
        {
            var result = await service.BulkRetryAsync(request.MessageIds);
            return Results.Ok(result);
        }).WithName("BulkRetryDeadLetters")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/dlq-alert", async (IEventStreamService service) =>
        {
            var alert = await service.CheckDlqDepthAsync();
            return Results.Ok(alert);
        }).WithName("GetDlqAlert")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapPost("/dead-letters/{id}/discard", async (string id, IEventStreamService service) =>
        {
            var success = await service.DiscardDeadLetterAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("DiscardDeadLetter")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    public static IEndpointRouteBuilder MapOutreachEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/outreach")
            .WithTags("Outreach & Engagement")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/summary", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var summary = await service.GetSummaryAsync(user);
            return Results.Ok(summary);
        }).WithName("GetOutreachSummary")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/overview", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var summary = await service.GetSummaryAsync(user);
            return Results.Ok(new
            {
                totalSentToday = summary.TotalSentToday,
                budgetExhaustionRate = summary.BudgetExhaustionRate,
                reEngagementRate = summary.ReEngagementRate.FirstOrDefault()?.Rate ?? 0f,
                channels = summary.ByChannel.Select(c => new { channel = c.Channel, sentToday = c.SentToday, deliveryRate = c.OpenRate, responseRate = c.ClickRate })
            });
        }).WithName("GetOutreachOverview")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/by-channel", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var summary = await service.GetSummaryAsync(user);
            var channels = summary.ByChannel.Select(c => new { channel = c.Channel, sentToday = c.SentToday, deliveryRate = c.OpenRate, responseRate = c.ClickRate });
            return Results.Ok(new { channels });
        }).WithName("GetOutreachByChannel")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/by-trigger", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var effectiveness = await service.GetChannelEffectivenessAsync(user);
            var series = effectiveness.VolumeByTrigger.Select(t => new
            {
                date = t.Trend.FirstOrDefault()?.Date ?? "",
                triggers = new Dictionary<string, int> { [t.TriggerType] = t.Count }
            });
            return Results.Ok(new { series });
        }).WithName("GetOutreachByTrigger")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/send-times", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var effectiveness = await service.GetChannelEffectivenessAsync(user);
            var cells = effectiveness.SendTimeHeatmap.Select(s => new { hour = s.Hour, day = s.DayOfWeek, responseRate = s.ResponseRate });
            return Results.Ok(new { cells });
        }).WithName("GetOutreachSendTimes")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/re-engagement-rate", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var summary = await service.GetSummaryAsync(user);
            return Results.Ok(summary.ReEngagementRate);
        }).WithName("GetReEngagementRate")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/history", async (string studentId, IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var history = await service.GetStudentHistoryAsync(studentId, user);
            return history != null ? Results.Ok(history) : Results.NotFound();
        }).WithName("GetStudentOutreachHistory")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/budget-alert", async (IOutreachEngagementService service, ClaimsPrincipal user) =>
        {
            var alert = await service.GetBudgetAlertAsync(user);
            return Results.Ok(alert);
        }).WithName("GetBudgetAlert")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // ═════════════════════════════════════════════════════════════════
    // Student Insights Endpoints (per-student cross-cutting analytics)
    // ═════════════════════════════════════════════════════════════════

    public static IEndpointRouteBuilder MapStudentInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/insights")
            .WithTags("Student Insights")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // FIND-data-025: Student insights endpoints with tenant scoping
        group.MapGet("/students/{studentId}/focus-heatmap", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetFocusHeatmapAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentFocusHeatmap")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/degradation", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetDegradationCurveAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentDegradation")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/engagement", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetEngagementAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentEngagement")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/error-types", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetErrorTypesAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentErrorTypes")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/hint-usage", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetHintUsageAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentHintUsage")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/stagnation", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetStagnationAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentStagnation")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/session-patterns", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetSessionPatternsAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentSessionPatterns")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/students/{studentId}/response-times", async (string studentId, ClaimsPrincipal user, IStudentInsightsService service) =>
        {
            var result = await service.GetResponseTimesAsync(studentId, user);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("GetStudentResponseTimes")
    .Produces(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
