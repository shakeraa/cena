// =============================================================================
// Cena Platform — Parent dashboard DTOs (EPIC-PRR-I PRR-320/324)
//
// Premium-tier parent view. Aggregated only — never session transcripts or
// raw misconception events (per ADR-0003 session-scope). Wire shape separate
// from domain types so the domain can evolve without breaking the Arabic /
// Hebrew frontend parity.
// =============================================================================

namespace Cena.Api.Contracts.Subscriptions;

/// <summary>One student as seen from the parent dashboard.</summary>
/// <param name="StudentId">Opaque student id (for deep-link only; no PII).</param>
/// <param name="DisplayName">Optional display name (may be empty if student opted out).</param>
/// <param name="ActiveTier">Effective tier.</param>
/// <param name="WeeklyMinutes">Total practice minutes in the last 7 days.</param>
/// <param name="MonthlyMinutes">Total practice minutes in the last 30 days.</param>
/// <param name="TopicsPracticed">Count of distinct topics practiced in 30 days.</param>
/// <param name="ReadinessScore">0-100 readiness score for the student's primary exam target (null if no target).</param>
/// <param name="LastActiveAt">When the student was last active.</param>
public sealed record ParentDashboardStudentDto(
    string StudentId,
    string DisplayName,
    string ActiveTier,
    int WeeklyMinutes,
    int MonthlyMinutes,
    int TopicsPracticed,
    int? ReadinessScore,
    DateTimeOffset? LastActiveAt);

/// <summary>Full dashboard response for one household.</summary>
public sealed record ParentDashboardResponseDto(
    IReadOnlyList<ParentDashboardStudentDto> Students,
    int HouseholdMinutesWeekly,
    int HouseholdMinutesMonthly,
    DateTimeOffset GeneratedAt);
