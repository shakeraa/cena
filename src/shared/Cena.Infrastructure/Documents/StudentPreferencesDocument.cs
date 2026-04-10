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
    public bool EmailNotifications { get; set; } = true;
    public bool PushNotifications { get; set; } = true;
    public bool DailyReminder { get; set; } = true;
    public TimeSpan? DailyReminderTime { get; set; } = TimeSpan.FromHours(9); // 9 AM default
    public bool WeeklyProgress { get; set; } = true;
    public bool StreakAlerts { get; set; } = true;
    public bool NewContentAlerts { get; set; } = true;

    // Privacy settings
    public string ProfileVisibility { get; set; } = "class-only"; // 'public' | 'class-only' | 'private'
    public bool ShowProgressToClass { get; set; } = true;
    public bool AllowPeerComparison { get; set; } = true;
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
