// =============================================================================
// Cena Platform -- FERPA Compliance Admin Endpoints
// REV-013.2: Audit log query, summary, and data retention policy
// All endpoints are SuperAdminOnly (compliance data is highly sensitive).
// =============================================================================

using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Marten;
using Marten.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api;

public static class ComplianceEndpoints
{
    public static IEndpointRouteBuilder MapComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/compliance")
            .WithTags("Compliance")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        // GET /api/admin/compliance/audit-log
        group.MapGet("/audit-log", async (
            IQuerySession querySession,
            string? studentId,
            string? accessor,
            string? school,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int page = 1,
            int pageSize = 50) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 200) pageSize = 200;

            IQueryable<StudentRecordAccessLog> query = querySession.Query<StudentRecordAccessLog>();

            if (!string.IsNullOrWhiteSpace(studentId))
                query = query.Where(x => x.StudentId == studentId);

            if (!string.IsNullOrWhiteSpace(accessor))
                query = query.Where(x => x.AccessedBy == accessor);

            if (!string.IsNullOrWhiteSpace(school))
                query = query.Where(x => x.AccessorSchool == school);

            if (from.HasValue)
                query = query.Where(x => x.AccessedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.AccessedAt <= to.Value);

            var martenQuery = (IMartenQueryable<StudentRecordAccessLog>)query;
            var total = await martenQuery.CountAsync();

            var items = await martenQuery
                .OrderByDescending(x => x.AccessedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new
            {
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
                items
            });
        })
        .WithName("QueryComplianceAuditLog")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/compliance/audit-log/summary
        group.MapGet("/audit-log/summary", async (
            IQuerySession querySession,
            DateTimeOffset? from,
            DateTimeOffset? to) =>
        {
            IQueryable<StudentRecordAccessLog> query = querySession.Query<StudentRecordAccessLog>();

            if (from.HasValue)
                query = query.Where(x => x.AccessedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(x => x.AccessedAt <= to.Value);

            var logs = await ((IMartenQueryable<StudentRecordAccessLog>)query).ToListAsync();

            var accessesByRole = logs
                .GroupBy(x => x.AccessorRole)
                .Select(g => new { role = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            var accessesBySchool = logs
                .Where(x => x.AccessorSchool != null)
                .GroupBy(x => x.AccessorSchool)
                .Select(g => new { school = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            var accessesByEndpoint = logs
                .GroupBy(x => x.Endpoint)
                .Select(g => new { endpoint = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            var uniqueAccessors = logs.Select(x => x.AccessedBy).Distinct().Count();
            var uniqueStudents = logs.Where(x => x.StudentId != null)
                .Select(x => x.StudentId).Distinct().Count();

            return Results.Ok(new
            {
                totalAccesses = logs.Count,
                uniqueAccessors,
                uniqueStudents,
                accessesByRole,
                accessesBySchool,
                accessesByEndpoint,
                periodFrom = from,
                periodTo = to
            });
        })
        .WithName("AuditLogSummary")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // GET /api/admin/compliance/data-retention
        group.MapGet("/data-retention", async (IQuerySession querySession) =>
        {
            // Query the most recent retention run history
            var lastRun = await querySession.Query<RetentionRunHistory>()
                .OrderByDescending(x => x.RunAt)
                .FirstOrDefaultAsync();

            // Determine retention enforcement status
            var hasEverRun = lastRun != null;
            var isEnforced = lastRun?.Status == RetentionRunStatus.Completed;
            var isRunning = lastRun?.Status == RetentionRunStatus.Running;

            // Calculate next scheduled run (default: 24 hours after last completed run)
            DateTimeOffset? nextRunAt = null;
            if (lastRun?.Status == RetentionRunStatus.Completed && lastRun.CompletedAt.HasValue)
            {
                nextRunAt = lastRun.CompletedAt.Value.AddHours(24);
            }
            else if (lastRun == null)
            {
                // No runs yet - schedule for next midnight
                var now = DateTimeOffset.UtcNow;
                nextRunAt = new DateTimeOffset(now.Date.AddDays(1), TimeSpan.Zero);
            }

            // Build per-category purge counts from last run
            var categoryStats = new List<object>();
            if (lastRun?.CategorySummaries != null)
            {
                foreach (var summary in lastRun.CategorySummaries)
                {
                    categoryStats.Add(new
                    {
                        category = summary.Category,
                        purgedCount = summary.PurgedCount,
                        expiredCount = summary.ExpiredCount,
                        retentionDays = (int)summary.RetentionPeriod.TotalDays
                    });
                }
            }

            // Default category stats if no runs yet
            if (categoryStats.Count == 0)
            {
                categoryStats = new List<object>
                {
                    new
                    {
                        category = "AuditLog",
                        purgedCount = 0,
                        expiredCount = 0,
                        retentionDays = (int)DataRetentionPolicy.AuditLogRetention.TotalDays
                    },
                    new
                    {
                        category = "ConsentRecord",
                        purgedCount = 0,
                        expiredCount = 0,
                        retentionDays = (int)DataRetentionPolicy.StudentRecordRetention.TotalDays
                    }
                };
            }

            // Determine status message
            string statusMessage;
            if (isRunning)
            {
                statusMessage = "Retention worker is currently running";
            }
            else if (isEnforced)
            {
                statusMessage = "Retention policies are actively enforced";
            }
            else if (lastRun?.Status == RetentionRunStatus.Failed)
            {
                statusMessage = $"Last retention run failed: {lastRun.ErrorMessage ?? "Unknown error"}";
            }
            else
            {
                statusMessage = "Retention enforcement pending - no completed runs yet";
            }

            return Results.Ok(new
            {
                policies = new[]
                {
                    new
                    {
                        category = "Student Education Records",
                        retentionDays = DataRetentionPolicy.StudentRecordRetention.Days,
                        retentionYears = DataRetentionPolicy.StudentRecordRetention.Days / 365,
                        description = "Event streams, mastery snapshots, tutoring transcripts (FERPA 7-year requirement)"
                    },
                    new
                    {
                        category = "Audit Logs",
                        retentionDays = DataRetentionPolicy.AuditLogRetention.Days,
                        retentionYears = DataRetentionPolicy.AuditLogRetention.Days / 365,
                        description = "Student data access audit trail (compliance best practice)"
                    },
                    new
                    {
                        category = "Session Analytics",
                        retentionDays = DataRetentionPolicy.AnalyticsRetention.Days,
                        retentionYears = DataRetentionPolicy.AnalyticsRetention.Days / 365,
                        description = "Focus scores, session timing, learning analytics aggregates"
                    },
                    new
                    {
                        category = "Engagement Data",
                        retentionDays = DataRetentionPolicy.EngagementRetention.Days,
                        retentionYears = DataRetentionPolicy.EngagementRetention.Days / 365,
                        description = "XP, streaks, badges (1 year after inactivity)"
                    }
                },
                enforcement = new
                {
                    isEnforced,
                    isRunning,
                    hasEverRun,
                    status = statusMessage
                },
                lastRun = lastRun != null ? new
                {
                    runId = lastRun.Id,
                    runAt = lastRun.RunAt,
                    completedAt = lastRun.CompletedAt,
                    status = lastRun.Status.ToString(),
                    rowsPurged = lastRun.DocumentsPurged,
                    rowsScanned = lastRun.DocumentsScanned,
                    erasureRequestsAccelerated = lastRun.ErasureRequestsAccelerated,
                    errorMessage = lastRun.ErrorMessage
                } : null,
                nextRunAt,
                categoryStats,
                failures = lastRun?.Status == RetentionRunStatus.Failed
                    ? new[] { new { error = lastRun.ErrorMessage ?? "Unknown error", runAt = lastRun.RunAt } }
                    : Array.Empty<object>()
            });
        })
        .WithName("DataRetentionPolicy")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
