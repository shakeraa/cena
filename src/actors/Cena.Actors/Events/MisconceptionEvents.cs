// =============================================================================
// Cena Platform -- Misconception Domain Events (ADR-0003)
// Layer: Domain Events | Runtime: .NET 9
//
// Session-scoped misconception telemetry. These events are:
//   - [MlExcluded]: never used in ML training pipelines (Decision 3)
//   - Session-scoped: never persisted to StudentState (Decision 1)
//   - 30-day retention, 90-day hard cap (Decision 2)
//   - Excluded from GDPR portability exports (Decision 4)
// =============================================================================

using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when the tutor identifies a buggy rule in a student's step.
/// Appended to the student's event stream within the session boundary.
/// ADR-0003 Decision 1: this event drives <c>LearningSessionState</c>,
/// never <c>StudentState</c>.
/// </summary>
[MlExcluded("ADR-0003: session-scoped misconception data — Edmodo/COPPA/GDPR-K")]
public record MisconceptionDetected_V1(
    string StudentId,
    string SessionId,
    string BuggyRuleId,
    string TopicId,
    string QuestionId,
    string StudentAnswer,
    string ExpectedPattern,
    DateTimeOffset DetectedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted when the student demonstrates correct understanding of a
/// previously detected misconception within the same session.
/// </summary>
[MlExcluded("ADR-0003: session-scoped misconception data — Edmodo/COPPA/GDPR-K")]
public record MisconceptionRemediated_V1(
    string StudentId,
    string SessionId,
    string BuggyRuleId,
    string TopicId,
    int RemediationAttempts,
    DateTimeOffset RemediatedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted at session end. Explicitly clears all misconception state
/// from the session aggregate. ADR-0003 Decision 1: ensures no
/// misconception data leaks beyond the session boundary.
/// </summary>
[MlExcluded("ADR-0003: session-scoped misconception data — Edmodo/COPPA/GDPR-K")]
public record SessionMisconceptionsScrubbed_V1(
    string StudentId,
    string SessionId,
    int MisconceptionsDetectedCount,
    int MisconceptionsRemediatedCount,
    DateTimeOffset ScrubbedAt
) : IDelegatedEvent;
