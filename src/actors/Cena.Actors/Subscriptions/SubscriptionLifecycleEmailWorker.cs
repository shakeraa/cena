// =============================================================================
// Cena Platform — SubscriptionLifecycleEmailWorker (EPIC-PRR-I PRR-345)
//
// Transactional emails for the subscription lifecycle, all 7 kinds:
//   - Welcome            (on SubscriptionActivated_V1 from a non-trial path;
//                         suppressed when TrialConverted_V1 immediately
//                         precedes the activation in the stream — trial
//                         converters get the dedicated TrialConverted email
//                         instead, per t_c513d0ee089f cm 2026-04-30)
//   - RenewalUpcoming    (4 days before RenewsAt; per-cycle idempotent)
//   - PastDue            (on PaymentFailed_V1; per-parent idempotent)
//   - CancellationConfirm(on SubscriptionCancelled_V1; per-parent idempotent)
//   - RefundConfirm      (on SubscriptionRefunded_V1; per-parent idempotent)
//   - TrialEnding        (1 day before TrialEndsAt by default; per-cycle
//                         idempotent on TrialEndsAt; suppressed for trials
//                         that have already converted or expired — same
//                         shape as RenewalUpcoming)
//   - TrialConverted     (on TrialConverted_V1; per-parent idempotent;
//                         post-conversion confirmation/receipt with the
//                         converted-to tier + cycle + amount-paid carried
//                         on the event payload)
//
// Idempotency: LifecycleEmailMarker documents. Marker id shape:
//   - Kinds tied to a single terminal event : "{parentId}:{kind}"
//   - RenewalUpcoming (recurring per cycle) : "{parentId}:renewal_upcoming:{renewsAt:o}"
//   - TrialEnding (per-cycle on TrialEndsAt)  : "{parentId}:trial_ending:{trialEndsAt:o}"
//
// Honest framing per memory "Honest not complimentary" — no guilt-trip on
// cancellation, no pressure on past-due, no fake enthusiasm on welcome,
// no streak-style copy on trial-ending. The
// ISubscriptionLifecycleEmailDispatcher seam owns template selection +
// locale rendering; legal-reviewed HE / AR / EN copy is supplied by the
// concrete dispatcher impl (content + counsel gate).
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Subscriptions;

/// <summary>Marker doc tracking which lifecycle emails have been dispatched per parent.</summary>
public sealed class LifecycleEmailMarker
{
    /// <summary>Composite id: <c>{parentSubjectIdEncrypted}:{emailKind}</c>.</summary>
    public string Id { get; set; } = string.Empty;
    public string ParentSubjectIdEncrypted { get; set; } = string.Empty;
    public string EmailKind { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
}

/// <summary>
/// Interface for a lifecycle-specific dispatcher. Concrete impl renders
/// the right localized template for each kind + sends via the notification
/// backend. Kept separate from IParentDigestDispatcher so the digest and
/// lifecycle paths can evolve independently.
/// </summary>
public interface ISubscriptionLifecycleEmailDispatcher
{
    /// <summary>Known email kinds.</summary>
    public static class Kinds
    {
        public const string Welcome = "welcome";
        public const string RenewalUpcoming = "renewal_upcoming";
        public const string PastDue = "past_due";
        public const string CancellationConfirm = "cancellation_confirm";
        public const string RefundConfirm = "refund_confirm";
        // t_c513d0ee089f / 2026-04-30: trial lifecycle emails
        public const string TrialEnding = "trial_ending";
        public const string TrialConverted = "trial_converted";
    }

    /// <summary>Dispatch a transactional lifecycle email.</summary>
    Task<bool> SendAsync(
        string parentSubjectIdEncrypted, string emailKind, CancellationToken ct);
}

/// <summary>
/// Knobs for <see cref="SubscriptionLifecycleEmailWorker"/>. Bind via
/// <c>SubscriptionLifecycleEmail:*</c>.
/// </summary>
public sealed class SubscriptionLifecycleEmailWorkerOptions
{
    public const string SectionName = "SubscriptionLifecycleEmail";

    /// <summary>Days before RenewsAt at which RenewalUpcoming fires. Default 4.</summary>
    public int RenewalUpcomingLeadDays { get; set; } = 4;

    /// <summary>
    /// Lookback window for the renewal-upcoming scan, in hours. The worker
    /// runs hourly so any window ≥ 1h is correct; default 25h gives a full
    /// day of catch-up slack if a tick is missed by up to 1h drift. The
    /// idempotency marker prevents a duplicate email when a renewal event
    /// falls in overlapping windows.
    /// </summary>
    public int RenewalUpcomingWindowHours { get; set; } = 25;

    /// <summary>
    /// Days before TrialEndsAt at which TrialEnding fires. Default 1.
    /// Same shape as RenewalUpcomingLeadDays. A trial that has already
    /// converted (TrialConverted_V1 on the stream) or expired
    /// (TrialExpired_V1 on the stream) suppresses the upcoming notice
    /// regardless of how close TrialEndsAt is.
    /// </summary>
    public int TrialEndingLeadDays { get; set; } = 1;

    /// <summary>
    /// Lookback window for the trial-ending scan, in hours. Same role as
    /// RenewalUpcomingWindowHours; 25h default gives an hour of drift
    /// slack while the per-cycle marker prevents duplicate dispatch on
    /// overlap.
    /// </summary>
    public int TrialEndingWindowHours { get; set; } = 25;

    /// <summary>Tick cadence in hours. Default 1.</summary>
    public int TickIntervalHours { get; set; } = 1;
}

/// <summary>
/// Input row to <see cref="SubscriptionLifecycleEmailWorker.ClassifyDispatches"/>.
/// Decouples the classification logic from Marten's <c>IEvent</c> so the pure
/// function is unit-testable without a live document store.
/// </summary>
/// <param name="StreamKey">Marten stream key (Cena convention: <c>subscription:{parentId}</c>).</param>
/// <param name="Payload">The deserialized event payload (e.g. SubscriptionActivated_V1).</param>
public sealed record LifecycleEventInput(string StreamKey, object Payload);

/// <summary>
/// One dispatch row produced by <see cref="SubscriptionLifecycleEmailWorker.ClassifyDispatches"/>.
/// </summary>
public sealed record LifecycleDispatchPlanItem(
    string ParentSubjectIdEncrypted,
    string EmailKind,
    string MarkerId);

/// <summary>
/// Periodic scan. Walks subscription streams, identifies lifecycle events
/// that need an email (and haven't received one yet), dispatches. Idempotent
/// via the LifecycleEmailMarker document.
/// </summary>
public sealed class SubscriptionLifecycleEmailWorker : BackgroundService
{
    private readonly IDocumentStore _store;
    private readonly ISubscriptionLifecycleEmailDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly SubscriptionLifecycleEmailWorkerOptions _options;
    private readonly ILogger<SubscriptionLifecycleEmailWorker> _logger;

    public SubscriptionLifecycleEmailWorker(
        IDocumentStore store,
        ISubscriptionLifecycleEmailDispatcher dispatcher,
        TimeProvider clock,
        IOptions<SubscriptionLifecycleEmailWorkerOptions> options,
        ILogger<SubscriptionLifecycleEmailWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                        "SubscriptionLifecycleEmailWorker sent {Count} lifecycle emails.", count);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "SubscriptionLifecycleEmailWorker pass failed; retrying in 1h.");
            }
            var interval = TimeSpan.FromHours(Math.Max(1, _options.TickIntervalHours));
            try
            {
                await Task.Delay(interval, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Single idempotent pass. Returns count of emails dispatched.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        await using var session = _store.LightweightSession();
        var alreadySent = (await session.Query<LifecycleEmailMarker>().ToListAsync(ct))
            .Select(m => m.Id)
            .ToHashSet();

        var rawEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);

        var inputs = rawEvents
            .Where(e => e.StreamKey is not null && e.Data is not null)
            .Select(e => new LifecycleEventInput(e.StreamKey!, e.Data!))
            .ToList();

        var plan = ClassifyDispatches(inputs, alreadySent, now, _options);

        var dispatched = 0;
        foreach (var item in plan)
        {
            if (await TryDispatchAsync(
                session, item.ParentSubjectIdEncrypted, item.EmailKind,
                item.MarkerId, now, ct))
            {
                dispatched++;
            }
        }

        if (dispatched > 0) await session.SaveChangesAsync(ct);
        return dispatched;
    }

    /// <summary>
    /// Pure classifier. Given the subscription event log + set of already-
    /// sent marker ids + current instant + options, return the ordered list
    /// of dispatches to attempt. Idempotent: a marker id already present in
    /// <paramref name="alreadySent"/> is never planned again. Stream-scoped:
    /// a stream with a terminal (Cancelled / Refunded) event suppresses
    /// future RenewalUpcoming notices on the same stream.
    /// </summary>
    /// <remarks>
    /// Split out as a static pure function so the dispatch-classification
    /// logic can be unit-tested without a live Marten document store.
    /// Calls no I/O and no clock beyond <paramref name="now"/>.
    /// </remarks>
    public static IReadOnlyList<LifecycleDispatchPlanItem> ClassifyDispatches(
        IReadOnlyList<LifecycleEventInput> events,
        ISet<string> alreadySent,
        DateTimeOffset now,
        SubscriptionLifecycleEmailWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(alreadySent);
        ArgumentNullException.ThrowIfNull(options);

        var plan = new List<LifecycleDispatchPlanItem>();
        // Local copy so we can treat plan items as already-scheduled within
        // this pass (prevents a duplicate plan row if the same event shows
        // up twice in the input list).
        var scheduled = new HashSet<string>(alreadySent);

        // Stream-scoped state for the conversion-suppresses-Welcome rule
        // and the trial-end-suppression rule. Walk events in order and
        // track which Activations are conversion-activations: the FIRST
        // SubscriptionActivated_V1 after a TrialConverted_V1 within the
        // same stream's open lifecycle. A subsequent terminal event
        // (Cancelled/Refunded) closes that lifecycle, so a re-subscription
        // (Phase 5) on the same stream gets a fresh non-conversion
        // Activation that fires Welcome correctly.
        // - conversionActivationIndices: indices into `events` of the
        //   activations that should suppress Welcome.
        // - trialResolved: stream had TrialConverted_V1 OR TrialExpired_V1
        //   → suppress TrialEnding on that stream regardless of where in
        //   the lookback window TrialEndsAt sits.
        var conversionActivationIndices = new HashSet<int>();
        var trialResolved = new HashSet<string>();
        var streamConversionPending = new HashSet<string>();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Payload)
            {
                case TrialConverted_V1:
                    streamConversionPending.Add(e.StreamKey);
                    trialResolved.Add(e.StreamKey);
                    break;
                case TrialExpired_V1:
                    trialResolved.Add(e.StreamKey);
                    break;
                case SubscriptionActivated_V1:
                    if (streamConversionPending.Remove(e.StreamKey))
                    {
                        // First activation after a conversion within this
                        // lifecycle — suppress Welcome for THIS index only.
                        conversionActivationIndices.Add(i);
                    }
                    break;
                case SubscriptionCancelled_V1:
                case SubscriptionRefunded_V1:
                    // Lifecycle closed; clear any stale pending-conversion
                    // flag so a future re-subscription Activation isn't
                    // misclassified.
                    streamConversionPending.Remove(e.StreamKey);
                    break;
            }
        }

        // Pass 1: terminal-event-driven kinds (Welcome / PastDue /
        // CancellationConfirm / RefundConfirm / TrialConverted) — each
        // one-per-parent-lifetime.
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            string? kind;
            string? parentId;
            switch (e.Payload)
            {
                case SubscriptionActivated_V1 a:
                    // Welcome suppression: this index was tagged as a
                    // conversion-activation in the pre-walk above, so the
                    // dedicated TrialConverted email handles the receipt —
                    // option (a) per the task body. Without this, trial
                    // converters would receive both Welcome AND
                    // TrialConverted, which is duplicative + confusing.
                    // Re-subscription Activations (post Cancelled/Refunded)
                    // are NOT in this set and fire Welcome correctly.
                    if (conversionActivationIndices.Contains(i)) continue;
                    kind = ISubscriptionLifecycleEmailDispatcher.Kinds.Welcome;
                    parentId = a.ParentSubjectIdEncrypted;
                    break;
                case PaymentFailed_V1 pf:
                    kind = ISubscriptionLifecycleEmailDispatcher.Kinds.PastDue;
                    parentId = pf.ParentSubjectIdEncrypted;
                    break;
                case SubscriptionCancelled_V1 c:
                    kind = ISubscriptionLifecycleEmailDispatcher.Kinds.CancellationConfirm;
                    parentId = c.ParentSubjectIdEncrypted;
                    break;
                case SubscriptionRefunded_V1 r:
                    kind = ISubscriptionLifecycleEmailDispatcher.Kinds.RefundConfirm;
                    parentId = r.ParentSubjectIdEncrypted;
                    break;
                case TrialConverted_V1 tc:
                    kind = ISubscriptionLifecycleEmailDispatcher.Kinds.TrialConverted;
                    parentId = tc.ParentSubjectIdEncrypted;
                    break;
                default:
                    continue;
            }
            if (string.IsNullOrEmpty(parentId)) continue;

            var markerId = $"{parentId}:{kind}";
            if (!scheduled.Add(markerId)) continue;
            plan.Add(new LifecycleDispatchPlanItem(parentId, kind, markerId));
        }

        // Pass 2: RenewalUpcoming — time-window around RenewsAt.
        // Streams with a terminal event suppress future renewal notices.
        var terminated = events
            .Where(e => e.Payload is SubscriptionCancelled_V1 or SubscriptionRefunded_V1)
            .Select(e => e.StreamKey)
            .ToHashSet();

        var lead = TimeSpan.FromDays(Math.Max(0, options.RenewalUpcomingLeadDays));
        var window = TimeSpan.FromHours(Math.Max(1, options.RenewalUpcomingWindowHours));

        foreach (var e in events)
        {
            string parentId;
            DateTimeOffset renewsAt;
            switch (e.Payload)
            {
                case SubscriptionActivated_V1 a:
                    parentId = a.ParentSubjectIdEncrypted;
                    renewsAt = a.RenewsAt;
                    break;
                case RenewalProcessed_V1 r:
                    parentId = r.ParentSubjectIdEncrypted;
                    renewsAt = r.NextRenewsAt;
                    break;
                default:
                    continue;
            }
            if (string.IsNullOrEmpty(parentId)) continue;
            if (terminated.Contains(e.StreamKey)) continue;

            var fireAt = renewsAt - lead;
            if (now < fireAt) continue;                 // too early
            if (now >= fireAt + window) continue;       // outside window; marker guards re-fire after missed weeks

            var renewsKey = renewsAt.ToUniversalTime().ToString("o");
            var markerId = $"{parentId}:" +
                $"{ISubscriptionLifecycleEmailDispatcher.Kinds.RenewalUpcoming}:" +
                $"{renewsKey}";
            if (!scheduled.Add(markerId)) continue;
            plan.Add(new LifecycleDispatchPlanItem(
                parentId,
                ISubscriptionLifecycleEmailDispatcher.Kinds.RenewalUpcoming,
                markerId));
        }

        // Pass 3: TrialEnding — same shape as RenewalUpcoming, keyed off
        // TrialStarted_V1.TrialEndsAt. Streams with TrialConverted_V1 or
        // TrialExpired_V1 already in the log skip the notice — the trial
        // is resolved either way and the curator-facing message would be
        // misleading. Per-cycle marker on TrialEndsAt prevents a duplicate
        // when overlap windows fire across multiple ticks; if a parent
        // ever runs a SECOND trial (rare; abuse-defense usually blocks)
        // the new TrialStarted_V1 carries a different TrialEndsAt and the
        // marker key changes accordingly.
        var trialLead = TimeSpan.FromDays(Math.Max(0, options.TrialEndingLeadDays));
        var trialWindow = TimeSpan.FromHours(Math.Max(1, options.TrialEndingWindowHours));

        foreach (var e in events)
        {
            if (e.Payload is not TrialStarted_V1 ts) continue;
            if (string.IsNullOrEmpty(ts.ParentSubjectIdEncrypted)) continue;

            // Suppression: trial already resolved (converted or expired).
            // The trial-ending notice is a heads-up before paywall; it
            // makes no sense for a trial that has already moved past
            // that decision point.
            if (trialResolved.Contains(e.StreamKey)) continue;

            // TrialEndsAt == TrialStartedAt indicates a cap-hit-only trial
            // (no calendar bound — see TrialStarted_V1 docstring). Strict
            // equality matches the docstring contract; the prior <= was
            // defensive but skipped real trials when clock skew left
            // TrialEndsAt slightly before TrialStartedAt. Such streams are
            // truly malformed (start > end), so we let them flow past this
            // cap-only guard and let the downstream `now < fireAt` /
            // window check handle them — at worst they silently miss a
            // notice rather than be misclassified as cap-only.
            if (ts.TrialEndsAt == ts.TrialStartedAt) continue;

            var fireAt = ts.TrialEndsAt - trialLead;
            if (now < fireAt) continue;
            if (now >= fireAt + trialWindow) continue;

            var endsKey = ts.TrialEndsAt.ToUniversalTime().ToString("o");
            var markerId = $"{ts.ParentSubjectIdEncrypted}:" +
                $"{ISubscriptionLifecycleEmailDispatcher.Kinds.TrialEnding}:" +
                $"{endsKey}";
            if (!scheduled.Add(markerId)) continue;
            plan.Add(new LifecycleDispatchPlanItem(
                ts.ParentSubjectIdEncrypted,
                ISubscriptionLifecycleEmailDispatcher.Kinds.TrialEnding,
                markerId));
        }

        return plan;
    }

    private async Task<bool> TryDispatchAsync(
        IDocumentSession session,
        string parentId,
        string kind,
        string markerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var sent = await _dispatcher.SendAsync(parentId, kind, ct);
            if (sent)
            {
                session.Store(new LifecycleEmailMarker
                {
                    Id = markerId,
                    ParentSubjectIdEncrypted = parentId,
                    EmailKind = kind,
                    SentAt = now,
                });
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Lifecycle email dispatch failed for kind={Kind}; will retry.", kind);
            return false;
        }
    }
}

/// <summary>Null dispatcher for dev/test.</summary>
public sealed class NullSubscriptionLifecycleEmailDispatcher : ISubscriptionLifecycleEmailDispatcher
{
    private readonly ILogger<NullSubscriptionLifecycleEmailDispatcher> _logger;

    public NullSubscriptionLifecycleEmailDispatcher(
        ILogger<NullSubscriptionLifecycleEmailDispatcher> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendAsync(
        string parentSubjectIdEncrypted, string emailKind, CancellationToken ct)
    {
        _logger.LogInformation(
            "NullSubscriptionLifecycleEmailDispatcher: would send kind={Kind} to parent={ParentIdPrefix}",
            emailKind,
            string.IsNullOrEmpty(parentSubjectIdEncrypted)
                ? "∅"
                : parentSubjectIdEncrypted.Length <= 8
                    ? parentSubjectIdEncrypted
                    : parentSubjectIdEncrypted[..8] + "…");
        return Task.FromResult(true);
    }
}
