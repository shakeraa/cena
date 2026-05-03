// =============================================================================
// Cena Platform — RenewalProcessed_V1 (EPIC-PRR-I PRR-300)
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Renewal payment cleared. Advances <see cref="SubscriptionState.RenewsAt"/>
/// to the next cycle boundary.
/// </summary>
public sealed record RenewalProcessed_V1(
    string ParentSubjectIdEncrypted,
    string PaymentTransactionIdEncrypted,
    long GrossAmountAgorot,
    DateTimeOffset RenewedAt,
    DateTimeOffset NextRenewsAt);
