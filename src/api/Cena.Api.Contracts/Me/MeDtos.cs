// =============================================================================
// Cena Platform -- Me/Profile API DTOs (STB-00, STB-00b)
// Student-facing bootstrap and profile endpoints
// =============================================================================

namespace Cena.Api.Contracts.Me;

/// <summary>Bootstrap payload for student cold-start (STB-00)</summary>
public record MeBootstrapDto(
    string StudentId,
    string DisplayName,
    string Role,
    string Locale,
    DateTime? OnboardedAt,
    string[] Subjects,
    int Level,
    int StreakDays,
    string? AvatarUrl);

/// <summary>Full profile view (STB-00)</summary>
public record ProfileDto(
    string StudentId,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    string? Bio,
    string[] FavoriteSubjects,
    string Visibility);  // 'public' | 'class-only' | 'private'

/// <summary>Profile update request (JSON merge patch) (STB-00)</summary>
public record ProfilePatchDto(
    string? DisplayName,
    string? Bio,
    string[]? FavoriteSubjects,
    string? Visibility);

/// <summary>Weekly subject target for onboarding (STB-00)</summary>
public record WeeklySubjectTarget(string Subject, int AccuracyTarget);

/// <summary>Diagnostic result for onboarding (STB-00)</summary>
public record DiagnosticResult(string QuestionId, bool Correct, int ConfidencePercent);

/// <summary>Onboarding submission request (STB-00)</summary>
public record OnboardingRequest(
    string Role,                              // 'student' | 'self-learner' | 'test-prep' | 'homeschool'
    string Locale,                            // 'en' | 'ar' | 'he'
    string[] Subjects,
    int DailyTimeGoalMinutes,                 // 5 | 10 | 15 | 30 | 45 | 60
    WeeklySubjectTarget[] WeeklySubjectTargets,
    DiagnosticResult[]? DiagnosticResults,
    string? ClassroomCode);

/// <summary>Onboarding submission response (STB-00)</summary>
public record OnboardingResponse(bool Success, string RedirectTo);

// ═════════════════════════════════════════════════════════════════════════════
// STB-00b: Settings, Devices, Classroom, Share Tokens
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Student settings response (STB-00b)</summary>
public record SettingsDto(
    AppearanceSettings Appearance,
    NotificationSettings Notifications,
    PrivacySettings Privacy,
    LearningSettings Learning,
    HomeLayoutDto HomeLayout);

/// <summary>Appearance settings (STB-00b)</summary>
public record AppearanceSettings(
    string Theme,        // 'light' | 'dark' | 'system'
    string Language,     // 'en' | 'ar' | 'he'
    bool ReducedMotion,
    bool HighContrast);

/// <summary>Notification settings (STB-00b)</summary>
public record NotificationSettings(
    bool EmailNotifications,
    bool PushNotifications,
    bool DailyReminder,
    TimeSpan? DailyReminderTime,
    bool WeeklyProgress,
    bool StreakAlerts,
    bool NewContentAlerts);

/// <summary>Privacy settings (STB-00b)</summary>
public record PrivacySettings(
    string ProfileVisibility,  // 'public' | 'class-only' | 'private'
    bool ShowProgressToClass,
    bool AllowPeerComparison,
    bool ShareAnalytics);

/// <summary>Learning settings (STB-00b)</summary>
public record LearningSettings(
    bool AutoAdvance,
    bool ShowHintsByDefault,
    bool SoundEffects,
    int TargetSessionMinutes,
    string DifficultyPreference);  // 'adaptive' | 'easy' | 'medium' | 'hard'

/// <summary>Home layout configuration (STB-00b)</summary>
public record HomeLayoutDto(
    string[] WidgetOrder,
    string[] HiddenWidgets,
    bool CompactMode);

/// <summary>Settings update request (JSON merge patch) (STB-00b)</summary>
public record SettingsPatchDto(
    AppearanceSettings? Appearance,
    NotificationSettings? Notifications,
    PrivacySettings? Privacy,
    LearningSettings? Learning,
    HomeLayoutDto? HomeLayout);

/// <summary>Classroom join request (STB-00b)</summary>
public record ClassroomJoinRequest(string Code);

/// <summary>Classroom join response (STB-00b)</summary>
public record ClassroomJoinResponse(
    string ClassroomId,
    string ClassroomName,
    string TeacherName);

/// <summary>Home layout update request (STB-00b)</summary>
public record HomeLayoutPatchDto(
    string[]? WidgetOrder,
    string[]? HiddenWidgets,
    bool? CompactMode);

/// <summary>Device info DTO (STB-00b)</summary>
public record DeviceDto(
    string Id,
    string Platform,
    string? DeviceName,
    string? DeviceModel,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    bool IsCurrentDevice);

/// <summary>Share token creation request (STB-00b)</summary>
public record ShareTokenRequest(
    string Audience,   // 'teacher' | 'parent' | 'peer'
    string[] Scopes,   // 'progress' | 'achievements' | 'activity'
    int ExpiresInDays);

/// <summary>Share token creation response (STB-00b)</summary>
public record ShareTokenResponse(
    string Token,
    string Url,
    DateTime ExpiresAt);

// ═════════════════════════════════════════════════════════════════════════════
// FIND-privacy-001: Age Gate & Consent
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// POST /api/me/age-consent request body. Records DOB and optional parent
/// email for an authenticated student. Used during the registration flow
/// after the age gate step.
/// </summary>
public record AgeConsentRequest(
    DateOnly DateOfBirth,
    string? ParentEmail);
