// =============================================================================
// FIND-privacy-010 — ICO Children's Code Std 3+7: high-privacy defaults
//
// Verifies that CreateDefaultPreferences returns a StudentPreferencesDocument
// where every visibility, sharing, notification, and engagement toggle is OFF
// and ProfileVisibility is "private". If any of these regress to true/"class-only"
// the tests fail, catching the exact violation that triggered this finding.
//
// Hosted inside Cena.Admin.Api.Tests because it already references
// Cena.Student.Api.Host (InternalsVisibleTo) and is wired into CI.
// =============================================================================

using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests;

public class HighPrivacyDefaultPreferencesTests
{
    /// <summary>
    /// FIND-privacy-010 regression gate: every privacy-relevant field in a
    /// freshly created StudentPreferencesDocument must default to its
    /// most-private value.
    /// </summary>
    [Fact]
    public void CreateDefaultPreferences_AllPrivacyFields_AreHighPrivacy()
    {
        var prefs = MeEndpoints.CreateDefaultPreferences("test-student-001");

        // Privacy settings — all OFF / most-private
        Assert.Equal("private", prefs.ProfileVisibility);
        Assert.False(prefs.ShowProgressToClass, "ShowProgressToClass must default to false (ICO Std 3)");
        Assert.False(prefs.AllowPeerComparison, "AllowPeerComparison must default to false (ICO Std 3)");
        Assert.False(prefs.ShareAnalytics, "ShareAnalytics must default to false (ICO Std 3)");
    }

    /// <summary>
    /// FIND-privacy-010 regression gate: all notification/engagement toggles
    /// must default to OFF per ICO Children's Code Standard 13 (nudge
    /// techniques) — no engagement loops without explicit opt-in.
    /// </summary>
    [Fact]
    public void CreateDefaultPreferences_AllNotificationFields_DefaultOff()
    {
        var prefs = MeEndpoints.CreateDefaultPreferences("test-student-002");

        Assert.False(prefs.EmailNotifications, "EmailNotifications must default to false (ICO Std 13)");
        Assert.False(prefs.PushNotifications, "PushNotifications must default to false (ICO Std 13)");
        Assert.False(prefs.DailyReminder, "DailyReminder must default to false (ICO Std 13)");
        Assert.False(prefs.WeeklyProgress, "WeeklyProgress must default to false (ICO Std 13)");
        Assert.False(prefs.StreakAlerts, "StreakAlerts must default to false (ICO Std 13)");
        Assert.False(prefs.NewContentAlerts, "NewContentAlerts must default to false (ICO Std 13)");
    }

    /// <summary>
    /// FIND-privacy-010: StudentPreferencesDocument C# class initializers
    /// must also reflect high-privacy defaults, not just CreateDefaultPreferences.
    /// This catches regressions where someone edits the DTO defaults without
    /// updating CreateDefaultPreferences (or vice versa).
    /// </summary>
    [Fact]
    public void StudentPreferencesDocument_ClassDefaults_AreHighPrivacy()
    {
        var doc = new StudentPreferencesDocument();

        // Privacy
        Assert.Equal("private", doc.ProfileVisibility);
        Assert.False(doc.ShowProgressToClass);
        Assert.False(doc.AllowPeerComparison);
        Assert.False(doc.ShareAnalytics);

        // Notifications
        Assert.False(doc.EmailNotifications);
        Assert.False(doc.PushNotifications);
        Assert.False(doc.DailyReminder);
        Assert.False(doc.WeeklyProgress);
        Assert.False(doc.StreakAlerts);
        Assert.False(doc.NewContentAlerts);
    }

    /// <summary>
    /// Functional defaults (learning settings) are not privacy-relevant and
    /// should remain unchanged — verify they survived the privacy sweep.
    /// </summary>
    [Fact]
    public void CreateDefaultPreferences_FunctionalDefaults_Preserved()
    {
        var prefs = MeEndpoints.CreateDefaultPreferences("test-student-003");

        // Learning defaults — not privacy-relevant, should be untouched
        Assert.False(prefs.AutoAdvance);
        Assert.True(prefs.ShowHintsByDefault);
        Assert.True(prefs.SoundEffects);
        Assert.Equal(15, prefs.TargetSessionMinutes);
        Assert.Equal("adaptive", prefs.DifficultyPreference);

        // Appearance defaults
        Assert.Equal("system", prefs.Theme);
        Assert.Equal("en", prefs.Language);
        Assert.False(prefs.ReducedMotion);
        Assert.False(prefs.HighContrast);
    }

    /// <summary>
    /// Verify the ID fields are correctly wired.
    /// </summary>
    [Fact]
    public void CreateDefaultPreferences_SetsStudentIdAndId()
    {
        const string studentId = "student-xyz";
        var prefs = MeEndpoints.CreateDefaultPreferences(studentId);

        Assert.Equal(studentId, prefs.Id);
        Assert.Equal(studentId, prefs.StudentId);
    }
}
