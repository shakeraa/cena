// =============================================================================
// Cena Platform — SubscriptionCancelled_V1 (EPIC-PRR-I PRR-306)
//
// Cancelled is terminal. To restart, a new stream is opened.
// Initiator distinguishes self-cancel, past-due-retry-exhaustion, admin-cancel.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>Terminal cancellation event.</summary>
public sealed record SubscriptionCancelled_V1(
    string ParentSubjectIdEncrypted,
    string Reason,
    string Initiator,
    DateTimeOffset CancelledAt);
