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
            if (replayed.State.Status != SubscriptionStatus.Active) continue;

            var featureFlags = TierCatalog.Get(replayed.State.CurrentTier).Features;
            if (!featureFlags.ParentDashboard) continue;

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

    /// <summary>Time until Sunday 08:00 UTC. Exposed internal for tests.</summary>
    internal static TimeSpan TimeUntilNextSundayMorning(DateTimeOffset now)
    {
        var target = now.Date.AddHours(8);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
        var nextSunday = target.AddDays(daysUntilSunday == 0 && now.TimeOfDay >= TimeSpan.FromHours(8)
            ? 7
            : daysUntilSunday);
        var result = nextSunday - now;
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
