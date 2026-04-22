// =============================================================================
// Cena Platform — SubscriptionAggregate (EPIC-PRR-I, ADR-0057)
//
// Aggregate root for the retail subscription bounded context. Stream key:
// `subscription-{parentSubjectId}`. Parent-keyed per ADR-0057 §2 — parent is
// the billing counterparty; students are entitlement targets.
//
// Pattern matches ConsentAggregate (ADR-0042): aggregate is a thin state
// + Apply-dispatch shell; command validation lives in SubscriptionCommands;
// persistence is the caller's concern.
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Aggregate root for a single subscription stream. Stream key:
/// <c>subscription-{parentSubjectId}</c>. State is <see cref="SubscriptionState"/>;
/// event application delegated to the state type.
/// </summary>
public sealed class SubscriptionAggregate
{
    /// <summary>Conventional stream-key prefix for this aggregate.</summary>
    public const string StreamKeyPrefix = "subscription-";

    /// <summary>
    /// Build the stream key for a parent subject id. Callers must have
    /// already validated that <paramref name="parentSubjectId"/> is non-empty.
    /// </summary>
    public static string StreamKey(string parentSubjectId)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectId))
        {
            throw new ArgumentException(
                "Parent subject id must be non-empty for stream-key construction.",
                nameof(parentSubjectId));
        }
        return StreamKeyPrefix + parentSubjectId;
    }

    /// <summary>Backing state carried by this aggregate instance.</summary>
    public SubscriptionState State { get; } = new();

    /// <summary>
    /// Apply an inbound domain event. Unknown events are silently ignored to
    /// tolerate forward migration — matches the <c>ConsentAggregate</c>
    /// convention.
    /// </summary>
    public void Apply(object @event)
    {
        switch (@event)
        {
            case SubscriptionActivated_V1 activated:
                State.Apply(activated);
                break;
            case TierChanged_V1 tierChanged:
                State.Apply(tierChanged);
                break;
            case BillingCycleChanged_V1 cycleChanged:
                State.Apply(cycleChanged);
                break;
            case SiblingEntitlementLinked_V1 siblingLinked:
                State.Apply(siblingLinked);
                break;
            case SiblingEntitlementUnlinked_V1 siblingUnlinked:
                State.Apply(siblingUnlinked);
                break;
            case RenewalProcessed_V1 renewed:
                State.Apply(renewed);
                break;
            case PaymentFailed_V1 paymentFailed:
                State.Apply(paymentFailed);
                break;
            case SubscriptionCancelled_V1 cancelled:
                State.Apply(cancelled);
                break;
            case SubscriptionRefunded_V1 refunded:
                State.Apply(refunded);
                break;
            case EntitlementSoftCapReached_V1 softCap:
                State.Apply(softCap);
                break;
        }
    }

    /// <summary>
    /// Replay a sequence of events into a fresh aggregate. Used by the
    /// read-side facade and the in-memory store to rebuild state.
    /// </summary>
    public static SubscriptionAggregate ReplayFrom(IEnumerable<object> events)
    {
        var aggregate = new SubscriptionAggregate();
        foreach (var evt in events)
        {
            aggregate.Apply(evt);
        }
        return aggregate;
    }
}
