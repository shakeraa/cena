// =============================================================================
// Cena Platform -- Me/Profile API DTOs (STB-00)
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
