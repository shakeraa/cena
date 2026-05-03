// =============================================================================
// Cena Platform — SiblingEntitlementLinked_V1 (EPIC-PRR-I PRR-293)
//
// Parent added a sibling student to their subscription. Discount depth
// depends on sibling ordinal (TierCatalog.SiblingMonthlyPrice); event
// captures the discount actually applied so historical replay matches
// what the customer saw.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>Sibling added to a subscription.</summary>
public sealed record SiblingEntitlementLinked_V1(
    string ParentSubjectIdEncrypted,
    string SiblingStudentSubjectIdEncrypted,
    int SiblingOrdinal,
    SubscriptionTier Tier,
    long SiblingMonthlyAgorot,
    DateTimeOffset LinkedAt);
