// =============================================================================
// Cena Platform — TwoWeekProgressReportWorker (EPIC-PRR-I PRR-295)
//
// At +14 days post-activation, send a concrete progress report to each
// subscribed parent. "Pre-pays" the second invoice's perceived value for
// the price-sensitive cost-conscious-parent persona (#1 from the 10-persona
// review). Honest numbers per memory "Honest not complimentary" — reports
// zero progress as zero, doesn't manufacture optimism.
//
// Dispatch reuses IParentDigestDispatcher via a thin adapter that passes a
// "two-week" kind marker; the dispatcher impl picks the right template.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Tracks which parents have already received their two-week report so
/// restarts + retries don't double-send. Keyed by parent subject id.
/// </summary>
public sealed class TwoWeekReportSentMarker
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
}

/// <summary>
/// Daily pass looking for subscriptions activated 14 days ago that haven't
/// had a two-week report sent yet.
/// </summary>
public sealed class TwoWeekProgressReportWorker : BackgroundService
{
    private readonly IDocumentStore _store;
    private readonly IParentDigestDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<TwoWeekProgressReportWorker> _logger;

    public TwoWeekProgressReportWorker(
        IDocumentStore store,
        IParentDigestDispatcher dispatcher,
        TimeProvider clock,
        ILogger<TwoWeekProgressReportWorker> logger)
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
                if (count > 0)
                {
                    _logger.LogInformation(
                        "TwoWeekProgressReportWorker sent {Count} two-week reports.", count);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "TwoWeekProgressReportWorker pass failed; retrying tomorrow.");
            }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    /// <summary>Idempotent daily pass. Returns count of reports dispatched this cycle.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();

        await using var session = _store.LightweightSession();
        var alreadySent = (await session.Query<TwoWeekReportSentMarker>().ToListAsync(ct))
            .Select(m => m.Id)
            .ToHashSet(StringComparer.Ordinal);

        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);

        var activations = events
            .Select(e => e.Data)
            .OfType<SubscriptionActivated_V1>();
        var candidates = SelectCandidates(activations, alreadySent, now).ToList();

        var dispatched = 0;
        foreach (var activation in candidates)
        {
            var parentId = activation.ParentSubjectIdEncrypted;
            var studentIds = new[] { activation.PrimaryStudentSubjectIdEncrypted };
            try
            {
                var sent = await _dispatcher.SendWeeklyDigestAsync(
                    parentId, studentIds,
                    windowStart: activation.ActivatedAt,
                    windowEnd: now,
                    ct);
                if (sent)
                {
                    session.Store(new TwoWeekReportSentMarker
                    {
                        Id = parentId,
                        SentAt = now,
                    });
                    dispatched++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Two-week report dispatch failed for parent; will retry tomorrow.");
            }
        }
        if (dispatched > 0) await session.SaveChangesAsync(ct);
        return dispatched;
    }

    /// <summary>
    /// Pure candidate-selection helper. Given every subscription-activation
    /// event + the set of parent ids already sent a two-week report + the
    /// current clock, return the activations whose age falls in
    /// [14, 21] days and whose parent has not already been sent. The
    /// 1-week retry window (21d upper bound) is how we recover from a
    /// worker down-day without permanently losing the report — the
    /// sent-marker keeps it idempotent so a retry on day 15 after a
    /// dispatch on day 14 is still a no-op.
    /// </summary>
    public static IEnumerable<SubscriptionActivated_V1> SelectCandidates(
        IEnumerable<SubscriptionActivated_V1> activations,
        ISet<string> alreadySentParentIds,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activations);
        ArgumentNullException.ThrowIfNull(alreadySentParentIds);

        var windowLowerBound = now.AddDays(-21);
        var windowUpperBound = now.AddDays(-14);
        foreach (var a in activations)
        {
            if (a.ActivatedAt < windowLowerBound) continue;
            if (a.ActivatedAt > windowUpperBound) continue;
            if (alreadySentParentIds.Contains(a.ParentSubjectIdEncrypted)) continue;
            yield return a;
        }
    }
}
