// =============================================================================
// Cena Platform — SubscriptionRefunded_V1 (EPIC-PRR-I PRR-306)
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>Money returned under the 30-day money-back guarantee.</summary>
public sealed record SubscriptionRefunded_V1(
    string ParentSubjectIdEncrypted,
    long RefundedAmountAgorot,
    string Reason,
    DateTimeOffset RefundedAt);
