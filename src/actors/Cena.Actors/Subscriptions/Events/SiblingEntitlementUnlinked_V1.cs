// =============================================================================
// Cena Platform — SiblingEntitlementUnlinked_V1 (EPIC-PRR-I PRR-293)
//
// Sibling removed; pro-rata credit for the remainder of the current cycle.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>Sibling removed from a subscription.</summary>
public sealed record SiblingEntitlementUnlinked_V1(
    string ParentSubjectIdEncrypted,
    string SiblingStudentSubjectIdEncrypted,
    long ProRataCreditAgorot,
    DateTimeOffset UnlinkedAt);
