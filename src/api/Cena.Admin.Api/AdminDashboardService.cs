// =============================================================================
// Cena Platform -- Admin Dashboard Service
// BKD-004: Aggregation + caching for dashboard widgets
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IAdminDashboardService
{
    Task<DashboardOverviewResponse> GetOverviewAsync(ClaimsPrincipal user);
    Task<ActivityTimeSeriesResponse> GetActivityAsync(string period, ClaimsPrincipal user);
    Task<ContentPipelineResponse> GetContentPipelineAsync(string period);
    Task<FocusDistributionResponse> GetFocusDistributionAsync();
    Task<MasteryProgressResponse> GetMasteryProgressAsync(string period);
    Task<IReadOnlyList<SystemAlert>> GetAlertsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<RecentAdminAction>> GetRecentActivityAsync(int limit, ClaimsPrincipal user);
    Task<PendingReviewSummary> GetPendingReviewSummaryAsync(ClaimsPrincipal user);
    Task<DashboardHomeResponse> GetDashboardHomeAsync(ClaimsPrincipal user);
}

public sealed class AdminDashboardService : IAdminDashboardService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AdminDashboardService> _logger;

    public AdminDashboardService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<AdminDashboardService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<DashboardOverviewResponse> GetOverviewAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        var cacheKey = schoolId is null ? "dashboard:overview" : $"dashboard:overview:{schoolId}";
        var cached = await TryGetCachedAsync<DashboardOverviewResponse>(cacheKey);
        if (cached != null) return cached;

        await using var session = _store.QuerySession();
        var q = session.Query<AdminUser>().Where(u => !u.SoftDeleted);
        if (schoolId is not null)
            q = q.Where(u => u.School == schoolId); // REV-014: tenant filter
        var users = await q.ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var weekAgo = now.AddDays(-7);
        var twoWeeksAgo = now.AddDays(-14);
        var todayStart = now.Date;

        var totalStudents = users.Count(u => u.Role == CenaRole.STUDENT);
        var studentsLastWeek = users.Count(u => u.Role == CenaRole.STUDENT && u.CreatedAt < weekAgo);
        var studentsDelta = studentsLastWeek > 0
            ? (int)Math.Round(((double)totalStudents - studentsLastWeek) / studentsLastWeek * 100)
            : 0;

        var activeToday = users.Count(u => u.LastLoginAt >= todayStart);
        var activeYesterday = users.Count(u => u.LastLoginAt >= todayStart.AddDays(-1) && u.LastLoginAt < todayStart);
        var activeDelta = activeYesterday > 0
            ? (int)Math.Round(((double)activeToday - activeYesterday) / activeYesterday * 100)
            : 0;

        var pendingReview = users.Count(u => u.Status == UserStatus.Pending);

        // Calculate average focus score from student states (if available)
        float avgFocusScore = 0f;
        float avgFocusScoreChange = 0f;

        var response = new DashboardOverviewResponse(
            ActiveUsers: activeToday,
            ActiveUsersChange: activeDelta,
            TotalStudents: totalStudents,
            TotalStudentsChange: studentsDelta,
            ContentItems: 0,  // Will be populated when content pipeline is wired
            PendingReview: pendingReview,
            AvgFocusScore: avgFocusScore,
            AvgFocusScoreChange: avgFocusScoreChange);

        await SetCacheAsync(cacheKey, response, TimeSpan.FromSeconds(60));
        return response;
    }

    public async Task<ActivityTimeSeriesResponse> GetActivityAsync(string period, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        var cacheKey = schoolId is null
            ? $"dashboard:activity:{period}"
            : $"dashboard:activity:{period}:{schoolId}";
        var cached = await TryGetCachedAsync<ActivityTimeSeriesResponse>(cacheKey);
        if (cached != null) return cached;

        var days = period switch
        {
            "7d" => 7,
            "30d" => 30,
            "90d" => 90,
            _ => 30
        };

        await using var session = _store.QuerySession();
        var q = session.Query<AdminUser>().Where(u => !u.SoftDeleted && u.LastLoginAt != null);
        if (schoolId is not null)
            q = q.Where(u => u.School == schoolId); // REV-014: tenant filter
        var users = await q.ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var dataPoints = new List<ActivityDataPoint>();

        for (int i = days - 1; i >= 0; i--)
        {
            var date = now.AddDays(-i).Date;
            var dateEnd = date.AddDays(1);
            var weekStart = date.AddDays(-6);
            var monthStart = date.AddDays(-29);

            var dau = users.Count(u => u.LastLoginAt >= date && u.LastLoginAt < dateEnd);
            var wau = users.Count(u => u.LastLoginAt >= weekStart && u.LastLoginAt < dateEnd);
            var mau = users.Count(u => u.LastLoginAt >= monthStart && u.LastLoginAt < dateEnd);

            dataPoints.Add(new ActivityDataPoint(
                date.ToString("yyyy-MM-dd"), dau, wau, mau));
        }

        var response = new ActivityTimeSeriesResponse(period, dataPoints);
        await SetCacheAsync(cacheKey, response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<ContentPipelineResponse> GetContentPipelineAsync(string period)
    {
        var cacheKey = $"dashboard:pipeline:{period}";
        var cached = await TryGetCachedAsync<ContentPipelineResponse>(cacheKey);
        if (cached != null) return cached;

        var days = period switch { "7d" => 7, "30d" => 30, "90d" => 90, _ => 30 };

        await using var session = _store.QuerySession();
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-days);

        // Query real question events from Marten event store
        // Marten stores type names with double underscore (e.g., question_authored__v1)
        var questionEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.Timestamp >= since)
            .Where(e => e.EventTypeName == "question_authored__v1"
                     || e.EventTypeName == "question_ai_generated__v1"
                     || e.EventTypeName == "question_ingested__v1"
                     || e.EventTypeName == "question_reviewed__v1"
                     || e.EventTypeName == "question_approved__v1")
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        var dataPoints = new List<PipelinePoint>();
        for (int i = days - 1; i >= 0; i--)
        {
            var date = new DateTimeOffset(now.AddDays(-i).Date, TimeSpan.Zero);
            var dateEnd = date.AddDays(1);
            var dayEvents = questionEvents.Where(e =>
                e.Timestamp >= date && e.Timestamp < dateEnd).ToList();

            dataPoints.Add(new PipelinePoint(
                Date: date.ToString("yyyy-MM-dd"),
                Created: dayEvents.Count(e => e.EventTypeName is "question_authored__v1"
                    or "question_ai_generated__v1" or "question_ingested__v1"),
                Reviewed: dayEvents.Count(e => e.EventTypeName == "question_reviewed__v1"),
                Approved: dayEvents.Count(e => e.EventTypeName == "question_approved__v1"),
                Rejected: 0));
        }

        var response = new ContentPipelineResponse(dataPoints);
        await SetCacheAsync(cacheKey, response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<FocusDistributionResponse> GetFocusDistributionAsync()
    {
        var cached = await TryGetCachedAsync<FocusDistributionResponse>("dashboard:focus-dist");
        if (cached != null) return cached;

        await using var session = _store.QuerySession();

        // Query real SessionEnded events for fatigue scores (proxy for focus)
        var recentSessions = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_ended_v1")
            .Where(e => e.Timestamp >= DateTimeOffset.UtcNow.AddDays(-30))
            .ToListAsync();

        // Extract fatigue scores (lower fatigue = higher focus)
        var focusScores = recentSessions
            .Select(e =>
            {
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(e.Data?.ToString() ?? "{}");
                    var fatigue = json.RootElement.TryGetProperty("fatigueScoreAtEnd", out var f) ? f.GetDouble() : 0.5;
                    return Math.Clamp(1.0 - fatigue, 0, 1); // Invert: low fatigue = high focus
                }
                catch { return 0.5; }
            })
            .ToList();

        // Bucket into 5 ranges
        var buckets = new[] { "0-20%", "21-40%", "41-60%", "61-80%", "81-100%" };
        var counts = new int[5];
        foreach (var score in focusScores)
        {
            var idx = Math.Min(4, (int)(score * 5));
            counts[idx]++;
        }

        var distribution = buckets.Select((b, i) => new FocusDistributionPoint(b, counts[i])).ToList();
        var totalStudents = focusScores.Count;
        var average = totalStudents > 0 ? (float)(focusScores.Average() * 100) : 0f;
        var sorted = focusScores.OrderBy(s => s).ToList();
        var median = sorted.Count > 0 ? (float)(sorted[sorted.Count / 2] * 100) : 0f;

        var response = new FocusDistributionResponse(distribution, average, median, totalStudents);
        await SetCacheAsync("dashboard:focus-dist", response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<MasteryProgressResponse> GetMasteryProgressAsync(string period)
    {
        var cacheKey = $"dashboard:mastery:{period}";
        var cached = await TryGetCachedAsync<MasteryProgressResponse>(cacheKey);
        if (cached != null) return cached;

        var days = period switch { "7d" => 7, "30d" => 30, "90d" => 90, _ => 30 };

        await using var session = _store.QuerySession();
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-days);

        // Query real ConceptMastered events from Marten
        var masteredEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_mastered_v1")
            .Where(e => e.Timestamp >= since)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        // Also get total ConceptAttempted events for denominator
        var attemptEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1")
            .Where(e => e.Timestamp >= since)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        var dataPoints = new List<SubjectMasteryPoint>();
        int cumulativeMastered = 0;

        for (int i = days - 1; i >= 0; i--)
        {
            var date = now.AddDays(-i).Date;
            var dateEnd = date.AddDays(1);

            var dayMastered = masteredEvents.Count(e => e.Timestamp >= date && e.Timestamp < dateEnd);
            cumulativeMastered += dayMastered;

            var totalAttempts = attemptEvents.Count(e => e.Timestamp < dateEnd);
            var masteryPct = totalAttempts > 0
                ? Math.Min(95f, (float)cumulativeMastered / (totalAttempts * 0.01f + 1) * 10f)
                : 0f;

            dataPoints.Add(new SubjectMasteryPoint(
                Date: date.ToString("yyyy-MM-dd"),
                Math: MathF.Round(masteryPct, 1),
                Physics: 0f)); // Physics curriculum not yet implemented
        }

        var response = new MasteryProgressResponse(period, dataPoints);
        await SetCacheAsync(cacheKey, response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<IReadOnlyList<SystemAlert>> GetAlertsAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        var alerts = new List<SystemAlert>();
        var now = DateTimeOffset.UtcNow;

        await using var session = _store.QuerySession();

        // Check pending moderation queue depth (scoped to school if applicable)
        var pendingQuery = session.Query<AdminUser>().Where(u => u.Status == UserStatus.Pending);
        if (schoolId is not null)
            pendingQuery = pendingQuery.Where(u => u.School == schoolId); // REV-014
        var pendingCount = await pendingQuery.CountAsync();

        if (pendingCount > 10)
        {
            alerts.Add(new SystemAlert(
                $"alert-pending-{now:yyyyMMdd}",
                pendingCount > 50 ? "warning" : "info",
                $"{pendingCount} users pending review",
                "User registration queue depth above threshold",
                now, "user-pipeline"));
        }

        // Check for stale data (no new users in 24h — potential pipeline issue)
        var latestUserQuery = session.Query<AdminUser>().AsQueryable();
        if (schoolId is not null)
            latestUserQuery = latestUserQuery.Where(u => u.School == schoolId); // REV-014
        var latestUser = await latestUserQuery
            .OrderByDescending(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestUser != null && (now - latestUser.CreatedAt).TotalHours > 24)
        {
            alerts.Add(new SystemAlert(
                $"alert-stale-{now:yyyyMMdd}",
                "info",
                "No new user registrations in 24+ hours",
                $"Last registration: {latestUser.CreatedAt:g}",
                now, "user-pipeline"));
        }

        return alerts;
    }

    public async Task<IReadOnlyList<RecentAdminAction>> GetRecentActivityAsync(int limit, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        // Query recent admin user mutations (created/suspended/activated)
        var recentQuery = session.Query<AdminUser>()
            .Where(u => u.SuspendedAt != null || u.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7));
        if (schoolId is not null)
            recentQuery = recentQuery.Where(u => u.School == schoolId); // REV-014
        var recentUsers = await recentQuery
            .OrderByDescending(u => u.CreatedAt)
            .Take(limit)
            .ToListAsync();

        var actions = new List<RecentAdminAction>();

        foreach (var u in recentUsers)
        {
            if (u.SuspendedAt != null)
            {
                actions.Add(new RecentAdminAction(
                    u.SuspendedAt.Value, "system", "System",
                    "user.suspend", u.Id,
                    $"Suspended user {u.FullName}: {u.SuspensionReason}"));
            }

            actions.Add(new RecentAdminAction(
                u.CreatedAt, "system", "System",
                u.Status == UserStatus.Pending ? "user.invite" : "user.create",
                u.Id,
                $"Created user {u.FullName} ({u.Role})"));
        }

        return actions.OrderByDescending(a => a.Timestamp).Take(limit).ToList();
    }

    public async Task<PendingReviewSummary> GetPendingReviewSummaryAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        var pendingQuery = session.Query<AdminUser>().Where(u => u.Status == UserStatus.Pending);
        if (schoolId is not null)
            pendingQuery = pendingQuery.Where(u => u.School == schoolId); // REV-014

        var pendingCount = await pendingQuery.CountAsync();

        var oldestPending = await pendingQuery
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        var oldestHours = oldestPending != null
            ? (int)(DateTimeOffset.UtcNow - oldestPending.CreatedAt).TotalHours
            : 0;

        var priority = oldestHours switch
        {
            > 72 => "critical",
            > 48 => "high",
            > 24 => "medium",
            _ => "low"
        };

        return new PendingReviewSummary(pendingCount, oldestHours, priority);
    }

    public async Task<DashboardHomeResponse> GetDashboardHomeAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        var cacheKey = schoolId is null ? "dashboard:home" : $"dashboard:home:{schoolId}";
        var cached = await TryGetCachedAsync<DashboardHomeResponse>(cacheKey);
        if (cached != null) return cached;

        var overview = await GetOverviewAsync(user);
        var alerts = await GetAlertsAsync(user);
        var pendingReview = await GetPendingReviewSummaryAsync(user);
        var recentActivity = await GetRecentActivityAsync(20, user);

        var response = new DashboardHomeResponse(
            Overview: overview,
            Alerts: alerts,
            PendingReview: pendingReview,
            RecentActivity: recentActivity);

        await SetCacheAsync(cacheKey, response, TimeSpan.FromSeconds(30));
        return response;
    }

    // ---- Redis Cache Helpers ----

    private async Task<T?> TryGetCachedAsync<T>(string key) where T : class
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            if (value.HasValue)
                return JsonSerializer.Deserialize<T>(value!);
        }
        catch (RedisConnectionException)
        {
            // Redis down — skip cache
        }
        return null;
    }

    private async Task SetCacheAsync<T>(string key, T value, TimeSpan ttl)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, JsonSerializer.Serialize(value), ttl);
        }
        catch (RedisConnectionException)
        {
            // Redis down — skip cache
        }
    }
}
