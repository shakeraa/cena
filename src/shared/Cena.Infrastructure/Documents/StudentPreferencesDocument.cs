// =============================================================================
// Cena Platform — Student Preferences Document (STB-00b)
// Marten document for student settings, home layout, and preferences
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Student preferences document for settings, home layout, and privacy controls.
/// Created on first access with default values.
/// </summary>
public class StudentPreferencesDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";

    // Appearance settings
    public string Theme { get; set; } = "system"; // 'light' | 'dark' | 'system'
    public string Language { get; set; } = "en";  // 'en' | 'ar' | 'he'
    public bool ReducedMotion { get; set; } = false;
    public bool HighContrast { get; set; } = false;

    // Notification settings
    // FIND-privacy-010: ICO Children's Code Std 3+7 — all engagement/notification
    // defaults must be OFF (high-privacy by default for minors).
    public bool EmailNotifications { get; set; } = false;
    public bool PushNotifications { get; set; } = false;
    public bool DailyReminder { get; set; } = false;
    public TimeSpan? DailyReminderTime { get; set; } = TimeSpan.FromHours(9); // 9 AM default (only relevant when DailyReminder=true)
    public bool WeeklyProgress { get; set; } = false;
    public bool StreakAlerts { get; set; } = false;
    public bool NewContentAlerts { get; set; } = false;

    // Privacy settings
    // FIND-privacy-010: ICO Children's Code Std 3 — most-private defaults
    public string ProfileVisibility { get; set; } = "private"; // 'public' | 'class-only' | 'private'
    public bool ShowProgressToClass { get; set; } = false;
    public bool AllowPeerComparison { get; set; } = false;
    public bool ShareAnalytics { get; set; } = false;

    // Learning settings
    public bool AutoAdvance { get; set; } = false;
    public bool ShowHintsByDefault { get; set; } = true;
    public bool SoundEffects { get; set; } = true;
    public int TargetSessionMinutes { get; set; } = 15;
    public string DifficultyPreference { get; set; } = "adaptive"; // 'adaptive' | 'easy' | 'medium' | 'hard'

    // Home layout
    public HomeLayout HomeLayout { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Home layout configuration for dashboard widgets
/// </summary>
public class HomeLayout
{
    public string[] WidgetOrder { get; set; } = new[]
    {
        "streak", "progress", "recommended", "achievements", "activity"
    };

    public string[] HiddenWidgets { get; set; } = Array.Empty<string>();
    public bool CompactMode { get; set; } = false;
}
