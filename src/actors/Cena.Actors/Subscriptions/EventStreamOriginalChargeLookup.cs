// =============================================================================
// Cena Platform — EventStreamOriginalChargeLookup (EPIC-PRR-I PRR-306)
//
// Finds the most-recent payment transaction id + gross amount on a
// subscription aggregate by walking its event stream. Reads every event
// via ISubscriptionAggregateStore.ReadEventsAsync and returns the last
// SubscriptionActivated_V1 or RenewalProcessed_V1 found (latest wins —
// renewal refunds go against the renewal charge, not the activation).
//
// This is the canonical way to reverse a charge without leaking gateway
// specifics into RefundService; the caller stays gateway-agnostic.
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Event-stream implementation of <see cref="IOriginalChargeLookup"/>.
/// </summary>
public sealed class EventStreamOriginalChargeLookup : IOriginalChargeLookup
{
    private readonly ISubscriptionAggregateStore _store;

    /// <summary>Construct with the aggregate store.</summary>
    public EventStreamOriginalChargeLookup(ISubscriptionAggregateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<OriginalCharge?> LookupAsync(
        string parentSubjectIdEncrypted, CancellationToken ct)
    {
        var events = await _store
            .ReadEventsAsync(parentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);

        // Walk from tail to head so the most-recent paid charge is found
        // first. Renewal charge (if any) takes precedence over activation.
        for (var i = events.Count - 1; i >= 0; i--)
        {
            switch (events[i])
            {
                case RenewalProcessed_V1 r:
                    return new OriginalCharge(
                        r.PaymentTransactionIdEncrypted, r.GrossAmountAgorot);
                case SubscriptionActivated_V1 a:
                    return new OriginalCharge(
                        a.PaymentTransactionIdEncrypted, a.GrossAmountAgorot);
            }
        }
        return null;
    }
}
