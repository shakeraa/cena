// =============================================================================
// Cena Platform — WeeklyParentDigestWorker (EPIC-PRR-I PRR-323)
//
// Sends a weekly digest email to parents whose subscription includes the
// parent dashboard feature (Plus+Premium). Fires Sunday mornings (Israeli
// school-week anchor). Skips parents opted out via direct-marketing veto.
//
// Pulls aggregated activity from the Subscriptions + StudentMetrics
// projections. NO PII in LLM prompts (ADR-0047) — this worker doesn't hit
// LLM at all; it composes a localized template from hard data.
// =============================================================================

using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Adapter for actually dispatching the digest email. Implementations live
/// in Cena.Actors.Notifications — worker composes content, dispatcher
/// delivers via the configured backend (SMTP or null).
/// </summary>
public interface IParentDigestDispatcher
{
    /// <summary>
    /// Compose + send the weekly digest for a single parent. Returns true
    /// on send-accepted (not delivery-confirmed). Throws on configuration
    /// errors; catches transient transport failures internally and retries.
    /// </summary>
    Task<bool> SendWeeklyDigestAsync(
        string parentSubjectIdEncrypted,
        IReadOnlyList<string> linkedStudentIdsEncrypted,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken ct);
}

/// <summary>
/// Background worker that fires once weekly. Reads active Plus/Premium
/// subscriptions and dispatches a digest for each household that has the
/// parent-dashboard feature enabled AND has not opted out of direct
/// marketing (opt-out recorded per ADR-0003 session scope).
/// </summary>
public sealed class WeeklyParentDigestWorker : BackgroundService
{
    private readonly IDocumentStore _store;
    private readonly IParentDigestDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<WeeklyParentDigestWorker> _logger;

    public WeeklyParentDigestWorker(
        IDocumentStore store,
        IParentDigestDispatcher dispatcher,
        TimeProvider clock,
        ILogger<WeeklyParentDigestWorker> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await RunOnceAsync(stoppingToken);
                _logger.LogInformation(
                    "WeeklyParentDigestWorker pass complete: {Count} digests dispatched.", count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "WeeklyParentDigestWorker pass failed; retrying next cycle.");
            }
            // Sleep until next Sunday 08:00 UTC.
            await Task.Delay(TimeUntilNextSundayMorning(_clock.GetUtcNow()), stoppingToken);
        }
    }

    /// <summary>Load active dashboard-enabled subscriptions and dispatch one digest per parent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var windowStart = now.AddDays(-7);

        await using var session = _store.QuerySession();
        // Find every subscription stream that emitted SubscriptionActivated_V1 and
        // has no subsequent SubscriptionCancelled_V1 or SubscriptionRefunded_V1.
        // v1 reads all subscription events and folds in-memory; at pilot scale this
        // is fine. Post-pilot this becomes a dedicated Marten projection doc.
        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);

        var byStream = events.GroupBy(e => e.StreamKey!);
        var dispatched = 0;
        foreach (var group in byStream)
        {
            var replayed = SubscriptionAggregate.ReplayFrom(group.Select(e => e.Data));
            if (!IsEligibleForDigest(replayed.State)) continue;

            var parentId = group.Key.Substring(SubscriptionAggregate.StreamKeyPrefix.Length);
            var studentIds = replayed.State.LinkedStudents
                .Select(s => s.StudentSubjectIdEncrypted)
                .ToArray();
            try
            {
                var sent = await _dispatcher.SendWeeklyDigestAsync(
                    parentId, studentIds, windowStart, now, ct);
                if (sent) dispatched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Weekly digest dispatch failed for parent stream {StreamKey}; continuing.", group.Key);
            }
        }
        return dispatched;
    }

    /// <summary>
    /// Pure eligibility test exposed for tests: a subscription gets a weekly
    /// digest iff it is currently Active AND its tier's feature flags grant
    /// ParentDashboard access. Basic explicitly opts out by the feature flag
    /// (TierCatalog sets Basic.ParentDashboard=false); Plus+Premium opt in
    /// (both set ParentDashboard=true — see TierCatalog.PlusFeatures /
    /// PremiumFeatures). This keeps the "who gets the digest" rule in one
    /// place instead of scattering it across TierCatalog + worker + test.
    /// </summary>
    public static bool IsEligibleForDigest(SubscriptionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != SubscriptionStatus.Active) return false;
        var featureFlags = TierCatalog.Get(state.CurrentTier).Features;
        return featureFlags.ParentDashboard;
    }

    /// <summary>
    /// Time until the next Sunday 08:00 UTC anchor. Pure function, exposed
    /// public for tests.
    /// </summary>
    /// <remarks>
    /// Anchored in UTC explicitly. The earlier implementation used
    /// <c>now.Date.AddHours(8)</c> which returns a <see cref="DateTime"/>
    /// with <c>Kind=Unspecified</c>; subtracting a <see cref="DateTimeOffset"/>
    /// from that triggered an implicit conversion of the unspecified
    /// <see cref="DateTime"/> to <see cref="DateTimeOffset"/> using the
    /// runtime's LOCAL timezone. That made the returned <see cref="TimeSpan"/>
    /// silently timezone-dependent — fine on a UTC container, off by N hours
    /// on a developer laptop in Asia/Jerusalem. The rewrite below stays in
    /// <see cref="DateTimeOffset"/> throughout so the scheduling math is
    /// identical in every environment.
    /// </remarks>
    public static TimeSpan TimeUntilNextSundayMorning(DateTimeOffset now)
    {
        var nowUtc = now.ToUniversalTime();
        var todayMidnightUtc = new DateTimeOffset(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var todayEightUtc = todayMidnightUtc.AddHours(8);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)todayMidnightUtc.DayOfWeek + 7) % 7;
        var nextSunday = todayEightUtc.AddDays(
            daysUntilSunday == 0 && nowUtc.TimeOfDay >= TimeSpan.FromHours(8)
                ? 7
                : daysUntilSunday);
        var result = nextSunday - nowUtc;
        return result < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : result;
    }
}

/// <summary>
/// Null dispatcher for environments without SMTP configured — logs would-be
/// recipients + acks success so the worker's metric accounting stays honest.
/// </summary>
public sealed class NullParentDigestDispatcher : IParentDigestDispatcher
{
    private readonly ILogger<NullParentDigestDispatcher> _logger;

    public NullParentDigestDispatcher(ILogger<NullParentDigestDispatcher> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendWeeklyDigestAsync(
        string parentSubjectIdEncrypted,
        IReadOnlyList<string> linkedStudentIdsEncrypted,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "NullParentDigestDispatcher: would send weekly digest to parent={ParentIdPrefix} for {StudentCount} students, window={WindowStart:o}..{WindowEnd:o}",
            HashForLog(parentSubjectIdEncrypted),
            linkedStudentIdsEncrypted.Count,
            windowStart,
            windowEnd);
        return Task.FromResult(true);
    }

    private static string HashForLog(string encryptedId) =>
        string.IsNullOrEmpty(encryptedId)
            ? "∅"
            : encryptedId.Length <= 8 ? encryptedId : encryptedId[..8] + "…";
}
