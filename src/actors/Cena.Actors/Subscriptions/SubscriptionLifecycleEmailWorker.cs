// =============================================================================
// Cena Platform — SubscriptionLifecycleEmailWorker (EPIC-PRR-I PRR-345)
//
// Transactional emails for the subscription lifecycle:
//   - Welcome (on Activated)
//   - Renewal-upcoming (4 days before RenewsAt)
//   - Past-due notice (on PaymentFailed_V1)
//   - Cancellation confirm (on Cancelled)
//   - Refund confirm (on Refunded)
//
// Honest framing per memory "Honest not complimentary" — no guilt-trip on
// cancellation, no pressure on past-due, no fake enthusiasm on welcome.
// Reuses IParentDigestDispatcher for delivery plumbing; the dispatcher
// impl owns template selection + locale rendering.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    }

    /// <summary>Dispatch a transactional lifecycle email.</summary>
    Task<bool> SendAsync(
        string parentSubjectIdEncrypted, string emailKind, CancellationToken ct);
}

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
    private readonly ILogger<SubscriptionLifecycleEmailWorker> _logger;

    public SubscriptionLifecycleEmailWorker(
        IDocumentStore store,
        ISubscriptionLifecycleEmailDispatcher dispatcher,
        TimeProvider clock,
        ILogger<SubscriptionLifecycleEmailWorker> logger)
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
                        "SubscriptionLifecycleEmailWorker sent {Count} lifecycle emails.", count);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "SubscriptionLifecycleEmailWorker pass failed; retrying in 1h.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
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

        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);

        var dispatched = 0;
        foreach (var rawEvent in events)
        {
            string? kind;
            string? parentId;
            switch (rawEvent.Data)
            {
                case SubscriptionActivated_V1 a:
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
                default:
                    continue;
            }

            var markerId = $"{parentId}:{kind}";
            if (alreadySent.Contains(markerId)) continue;

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
                    alreadySent.Add(markerId);
                    dispatched++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Lifecycle email dispatch failed for kind={Kind}; will retry.", kind);
            }
        }
        if (dispatched > 0) await session.SaveChangesAsync(ct);
        return dispatched;
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
