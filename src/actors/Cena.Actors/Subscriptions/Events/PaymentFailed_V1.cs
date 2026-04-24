// =============================================================================
// Cena Platform — PaymentFailed_V1 (EPIC-PRR-I PRR-300/301)
//
// Reason strings are intentionally plaintext (gateway error codes, not PII).
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Payment attempt failed. Retry schedule: 3 attempts over 7 days per
/// PRR-300 scope; post-exhaustion transitions to Cancelled.
/// </summary>
public sealed record PaymentFailed_V1(
    string ParentSubjectIdEncrypted,
    string Reason,
    int AttemptNumber,
    DateTimeOffset FailedAt);
