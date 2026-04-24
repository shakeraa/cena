// =============================================================================
// Cena Platform -- Outreach & Engagement Service
// ADM-018: Re-engagement monitoring and analytics (production-grade)
// All methods query OutreachEventDocument + OutreachBudgetDocument. No Random.
// No hand-crafted arrays. No literal channel stats.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IOutreachEngagementService
{
    Task<OutreachSummaryResponse> GetSummaryAsync(ClaimsPrincipal user);
    Task<ChannelEffectivenessResponse> GetChannelEffectivenessAsync(ClaimsPrincipal user);
    Task<StudentOutreachHistoryResponse?> GetStudentHistoryAsync(string studentId, ClaimsPrincipal user);
    Task<BudgetAlertResponse> GetBudgetAlertAsync(ClaimsPrincipal user);
}

public sealed class OutreachEngagementService : IOutreachEngagementService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OutreachEngagementService> _logger;

    public OutreachEngagementService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<OutreachEngagementService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<OutreachSummaryResponse> GetSummaryAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var since7d = today.AddDays(-7);

        var query = session.Query<OutreachEventDocument>()
            .Where(e => e.SentAt >= since7d);
        if (schoolId is not null)
            query = (Marten.Linq.IMartenQueryable<OutreachEventDocument>)query.Where(e => e.SchoolId == schoolId);

        var events = await query.ToListAsync();

        var budget = schoolId is not null
            ? await session.LoadAsync<OutreachBudgetDocument>($"outreach-budget:{schoolId}")
            : null;

        return BuildSummary(events, today, budget);
    }

    public async Task<ChannelEffectivenessResponse> GetChannelEffectivenessAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var since60d = today.AddDays(-60);

        var query = session.Query<OutreachEventDocument>()
            .Where(e => e.SentAt >= since60d);
        if (schoolId is not null)
            query = (Marten.Linq.IMartenQueryable<OutreachEventDocument>)query.Where(e => e.SchoolId == schoolId);

        var events = await query.ToListAsync();
        return BuildChannelEffectiveness(events, today);
    }

    public async Task<StudentOutreachHistoryResponse?> GetStudentHistoryAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var eventsQuery = session.Query<OutreachEventDocument>()
            .Where(e => e.StudentId == studentId);
        if (schoolId is not null)
            eventsQuery = (Marten.Linq.IMartenQueryable<OutreachEventDocument>)eventsQuery.Where(e => e.SchoolId == schoolId);

        var events = await eventsQuery
            .OrderByDescending(e => e.SentAt)
            .Take(200)
            .ToListAsync();
        if (events.Count == 0) return null;

        var prefs = await session.LoadAsync<StudentNotificationPreferencesDocument>($"notif-prefs:{studentId}");
        return BuildStudentHistory(studentId, events, prefs);
    }

    public async Task<BudgetAlertResponse> GetBudgetAlertAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        if (schoolId is null)
            return new BudgetAlertResponse(false, 0f, 0, null);

        await using var session = _store.QuerySession();
        var budget = await session.LoadAsync<OutreachBudgetDocument>($"outreach-budget:{schoolId}");
        return BuildBudgetAlert(budget);
    }

    // -------------------------------------------------------------------------
    // Mapping helpers — pure, testable
    // -------------------------------------------------------------------------

    internal static OutreachSummaryResponse BuildSummary(
        IReadOnlyList<OutreachEventDocument> events,
        DateTimeOffset today,
        OutreachBudgetDocument? budget)
    {
        var todayStart = new DateTimeOffset(today.Date, TimeSpan.Zero);

        var byChannel = events
            .GroupBy(e => e.Channel)
            .Select(g =>
            {
                var todayCount = g.Count(e => e.SentAt >= todayStart);
                var weekCount = g.Count();
                var delivered = g.Count(e => e.Delivered);
                var opened = g.Count(e => e.Opened);
                var clicked = g.Count(e => e.Clicked);
                var optedOut = g.Count(e => e.OptedOut);
                var openRate = delivered > 0 ? (float)opened / delivered : 0f;
                var clickRate = delivered > 0 ? (float)clicked / delivered : 0f;
                var optOutRate = weekCount > 0 ? (float)optedOut / weekCount : 0f;
                return new ChannelStats(
                    Channel: g.Key,
                    SentToday: todayCount,
                    SentThisWeek: weekCount,
                    OpenRate: MathF.Round(openRate, 3),
                    ClickRate: MathF.Round(clickRate, 3),
                    OptOutRate: MathF.Round(optOutRate, 3));
            })
            .OrderBy(c => c.Channel)
            .ToList();

        // Per-day re-engagement rate for the last 7 days
        var reEngagement = new List<EngagementMetric>();
        for (int i = 6; i >= 0; i--)
        {
            var dayStart = today.AddDays(-i);
            var dayEnd = dayStart.AddDays(1);
            var dayEvents = events.Where(e => e.SentAt >= dayStart && e.SentAt < dayEnd).ToList();
            if (dayEvents.Count == 0)
            {
                reEngagement.Add(new EngagementMetric(dayStart.ToString("yyyy-MM-dd"), 0f, 0));
                continue;
            }
            var reEngaged = dayEvents.Count(e => e.ReEngagedAt != null);
            var rate = (float)reEngaged / dayEvents.Count;
            reEngagement.Add(new EngagementMetric(
                Period: dayStart.ToString("yyyy-MM-dd"),
                Rate: MathF.Round(rate, 3),
                SampleSize: dayEvents.Count));
        }

        var budgetExhaustionRate = budget is not null && budget.DailyBudget > 0
            ? MathF.Round((float)budget.UsedToday / budget.DailyBudget, 3)
            : 0f;

        var totalSentToday = byChannel.Sum(c => c.SentToday);

        return new OutreachSummaryResponse(
            ByChannel: byChannel,
            BudgetExhaustionRate: budgetExhaustionRate,
            ReEngagementRate: reEngagement,
            TotalSentToday: totalSentToday);
    }

    internal static ChannelEffectivenessResponse BuildChannelEffectiveness(
        IReadOnlyList<OutreachEventDocument> events,
        DateTimeOffset today)
    {
        var volumeByTrigger = events
            .GroupBy(e => e.TriggerReason)
            .Select(g =>
            {
                var trend = new List<VolumePoint>();
                for (int i = 6; i >= 0; i--)
                {
                    var dayStart = today.AddDays(-i);
                    var dayEnd = dayStart.AddDays(1);
                    var count = g.Count(e => e.SentAt >= dayStart && e.SentAt < dayEnd);
                    trend.Add(new VolumePoint(dayStart.ToString("yyyy-MM-dd"), count));
                }
                return new TriggerVolume(g.Key, g.Count(), trend);
            })
            .OrderByDescending(t => t.Count)
            .ToList();

        var channelComparison = events
            .GroupBy(e => e.Channel)
            .Select(g =>
            {
                var sample = g.Count();
                var reEngaged = g.Count(e => e.ReEngagedAt != null);
                var rate = sample > 0 ? (float)reEngaged / sample : 0f;
                var avgMinutes = g
                    .Where(e => e.ReEngagedAt != null)
                    .Select(e => (float)(e.ReEngagedAt!.Value - e.SentAt).TotalMinutes)
                    .DefaultIfEmpty(0f)
                    .Average();
                return new ChannelComparison(
                    Channel: g.Key,
                    ReEngagementRate: MathF.Round(rate, 3),
                    AvgTimeToReEngagementMinutes: MathF.Round(avgMinutes, 1),
                    SampleSize: sample);
            })
            .ToList();

        // Merged vs not merged effectiveness
        var mergeStats = events
            .GroupBy(e => e.WasMerged)
            .Select(g =>
            {
                var delivered = g.Count(e => e.Delivered);
                var opened = g.Count(e => e.Opened);
                var clicked = g.Count(e => e.Clicked);
                return new MergeEffectiveness(
                    WasMerged: g.Key,
                    OpenRate: delivered > 0 ? MathF.Round((float)opened / delivered, 3) : 0f,
                    ClickRate: delivered > 0 ? MathF.Round((float)clicked / delivered, 3) : 0f);
            })
            .ToList();

        // Real send-time heatmap
        var heatmap = events
            .GroupBy(e => (e.SentAt.DayOfWeek.ToString().Substring(0, 3), e.SentAt.Hour))
            .Select(g =>
            {
                var sample = g.Count();
                var reEngaged = g.Count(e => e.ReEngagedAt != null);
                var rate = sample > 0 ? (float)reEngaged / sample : 0f;
                return new OptimalSendTime(
                    DayOfWeek: g.Key.Item1,
                    Hour: g.Key.Hour,
                    ResponseRate: MathF.Round(rate, 3),
                    SampleSize: sample);
            })
            .OrderBy(o => o.DayOfWeek)
            .ThenBy(o => o.Hour)
            .ToList();

        return new ChannelEffectivenessResponse(volumeByTrigger, channelComparison, mergeStats, heatmap);
    }

    internal static StudentOutreachHistoryResponse BuildStudentHistory(
        string studentId,
        IReadOnlyList<OutreachEventDocument> events,
        StudentNotificationPreferencesDocument? prefs)
    {
        var eventList = events
            .OrderByDescending(e => e.SentAt)
            .Select(e => new OutreachEvent(
                EventId: e.Id,
                SentAt: e.SentAt,
                Channel: e.Channel,
                TriggerReason: e.TriggerReason,
                MessagePreview: e.MessagePreview,
                Delivered: e.Delivered,
                Opened: e.Opened,
                Clicked: e.Clicked,
                ReEngagedAt: e.ReEngagedAt))
            .ToList();

        var notification = new NotificationPreferences(
            WhatsAppEnabled: prefs?.WhatsAppEnabled ?? false,
            TelegramEnabled: prefs?.TelegramEnabled ?? false,
            PushEnabled: prefs?.PushEnabled ?? false,
            VoiceEnabled: prefs?.VoiceEnabled ?? false,
            UpdatedAt: prefs?.UpdatedAt);

        return new StudentOutreachHistoryResponse(studentId, eventList, notification);
    }

    internal static BudgetAlertResponse BuildBudgetAlert(OutreachBudgetDocument? budget)
    {
        if (budget is null || budget.DailyBudget <= 0)
            return new BudgetAlertResponse(false, 0f, 0, null);

        var usage = (float)budget.UsedToday / budget.DailyBudget;
        var remaining = Math.Max(0, budget.DailyBudget - budget.UsedToday);
        var alert = usage > 0.8f
            ? $"Daily message budget at {(int)(usage * 100)}%. Consider adjusting send rates."
            : null;
        return new BudgetAlertResponse(usage > 0.8f, MathF.Round(usage, 3), remaining, alert);
    }
}
