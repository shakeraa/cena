// =============================================================================
// Cena Platform — SubscriptionActivated_V1 (EPIC-PRR-I PRR-300)
//
// Emitted when a parent first activates a subscription. First event in the
// stream `subscription-{parentSubjectId}`. All PII fields encrypted per
// ADR-0038; payment transaction id is treated as PII-adjacent (it links
// the Cena user to an external-gateway identifier).
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Emitted at the first successful activation of a subscription for a parent.
/// </summary>
public sealed record SubscriptionActivated_V1(
    string ParentSubjectIdEncrypted,
    string PrimaryStudentSubjectIdEncrypted,
    SubscriptionTier Tier,
    BillingCycle Cycle,
    long GrossAmountAgorot,
    string PaymentTransactionIdEncrypted,
    DateTimeOffset ActivatedAt,
    DateTimeOffset RenewsAt);
