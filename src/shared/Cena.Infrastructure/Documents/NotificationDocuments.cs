// =============================================================================
// Cena Platform — Notification Documents (STB-07b)
// Notifications and Web Push subscriptions
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Notification document for in-app notifications.
/// </summary>
public class NotificationDocument
{
    public string Id { get; set; } = "";
    public string NotificationId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Kind { get; set; } = ""; // 'xp' | 'badge' | 'streak' | 'system' | 'friend-request' | 'review-due'
    public string Priority { get; set; } = "normal"; // 'low' | 'normal' | 'high'
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? IconName { get; set; }
    public string? DeepLinkUrl { get; set; }
    public bool Read { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public DateTime? SnoozedUntil { get; set; }

    /// <summary>
    /// Determines if the notification is visible (not deleted and not snoozed past now).
    /// </summary>
    public bool IsVisible => DeletedAt == null && (SnoozedUntil == null || SnoozedUntil < DateTime.UtcNow);
}

/// <summary>
/// Web Push subscription document for browser notifications.
/// </summary>
public class WebPushSubscriptionDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = ""; // Public key
    public string Auth { get; set; } = "";   // Auth secret
    public string? DeviceLabel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// Per-student notification preferences (STB-07c).
/// Stores channel opt-in, quiet hours window, timezone, and throttle settings.
/// </summary>
public class NotificationPreferencesDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public bool EmailEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = false;
    public bool SmsEnabled { get; set; } = false;
    public bool InAppEnabled { get; set; } = true;
    public bool SessionReminderEnabled { get; set; } = true;
    public bool AssignmentDueEnabled { get; set; } = true;
    public bool TeacherMessageEnabled { get; set; } = true;
    public bool WeeklySummaryEnabled { get; set; } = false;
    public bool MasteryMilestoneEnabled { get; set; } = true;
    public string Timezone { get; set; } = "UTC";
    public int? QuietHoursStartLocal { get; set; } // hour 0-23, null = no quiet window
    public int? QuietHoursEndLocal { get; set; }
    public bool DailyReminder { get; set; } = true;
    public int MaxXpNotificationsPerHour { get; set; } = 5;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
