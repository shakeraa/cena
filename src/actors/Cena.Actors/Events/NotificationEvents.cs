// =============================================================================
// Cena Platform — Notification Domain Events (STB-07b)
// Notification lifecycle events
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a notification is deleted by the student.
/// </summary>
public record NotificationDeleted_V1(
    string StudentId,
    string NotificationId,
    DateTimeOffset DeletedAt
);

/// <summary>
/// Emitted when a notification is snoozed.
/// </summary>
public record NotificationSnoozed_V1(
    string StudentId,
    string NotificationId,
    DateTimeOffset SnoozedUntil,
    DateTimeOffset SnoozedAt
);

/// <summary>
/// Emitted when a Web Push subscription is created.
/// </summary>
public record WebPushSubscribed_V1(
    string StudentId,
    string SubscriptionId,
    string Endpoint,
    DateTimeOffset SubscribedAt
);

/// <summary>
/// Emitted when a Web Push subscription is removed.
/// </summary>
public record WebPushUnsubscribed_V1(
    string StudentId,
    string Endpoint,
    DateTimeOffset UnsubscribedAt
);
