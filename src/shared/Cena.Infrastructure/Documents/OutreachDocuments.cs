// =============================================================================
// Cena Platform -- Outreach Engagement Documents (ADM-018)
// Marten-backed outreach audit log and budget singleton. Replaces the
// hand-crafted stubs in OutreachEngagementService.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// A single outreach event — one per message sent to a student or parent.
/// Records delivery, open, click, and re-engagement funnel metrics.
/// This is the canonical outreach audit log. All outreach dashboards
/// derive from grouping + aggregating this table.
/// </summary>
public class OutreachEventDocument
{
    public string Id { get; set; } = "";              // evt-{guid}
    public string StudentId { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public string? ClassId { get; set; }

    public string Channel { get; set; } = "";         // WhatsApp | Telegram | Push | Voice
    public string TriggerReason { get; set; } = "";   // disengagement | stagnation | reminder | onboarding
    public string MessagePreview { get; set; } = "";

    public DateTimeOffset SentAt { get; set; }
    public bool Delivered { get; set; }
    public bool Opened { get; set; }
    public bool Clicked { get; set; }
    public bool OptedOut { get; set; }
    public bool WasMerged { get; set; }               // merged into a parent thread

    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ReEngagedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Singleton-per-school outreach budget tracker. Updated when the outreach
/// worker sends a message. Powers the BudgetAlert dashboard.
/// </summary>
public class OutreachBudgetDocument
{
    public string Id { get; set; } = "";              // "outreach-budget:{schoolId}"
    public string SchoolId { get; set; } = "";
    public int DailyBudget { get; set; }              // messages/day cap
    public int UsedToday { get; set; }
    public DateTimeOffset BudgetDate { get; set; }    // local-day boundary
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-student notification preferences document. Powers GetStudentHistoryAsync.
/// One row per studentId. (SchoolId for tenant filtering.)
/// </summary>
public class StudentNotificationPreferencesDocument
{
    public string Id { get; set; } = "";              // "notif-prefs:{studentId}"
    public string StudentId { get; set; } = "";
    public string SchoolId { get; set; } = "";

    public bool WhatsAppEnabled { get; set; } = true;
    public bool TelegramEnabled { get; set; } = false;
    public bool PushEnabled { get; set; } = true;
    public bool VoiceEnabled { get; set; } = false;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
