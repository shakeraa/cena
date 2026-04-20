// =============================================================================
// Cena Platform — Exam Simulation Events (SEC-ASSESS-002)
//
// prr-013 follow-up (2026-04-20): `ExamSimulationSubmitted_V1` used to carry
// `ReadinessLowerBound` / `ReadinessUpperBound` on-stream. ADR-0003 +
// RDY-080 forbid persisting readiness scalars — they are session-scoped and
// live on `SessionRiskAssessment` inside the session actor only.
//
// Migration contract:
//   * V1 stays declared (but [Obsolete] and forbidden for NEW writes) so
//     historical Marten replay keeps working. There are no aggregate or
//     projection handlers against V1 in this codebase (grep confirmed
//     2026-04-20), so V1 events simply replay into a no-op.
//   * V2 is the only event shape emitters may construct going forward.
//     Readiness is not persisted; if a session actor computes a
//     SessionRiskAssessment, it stays session-scoped.
// =============================================================================

namespace Cena.Actors.Events;

public record ExamSimulationStarted_V1(
    string StudentId,
    string SimulationId,
    string ExamCode,
    int TimeLimitMinutes,
    int PartACount,
    int PartBCount,
    int VariantSeed,
    DateTimeOffset StartedAt
) : IDelegatedEvent;

/// <summary>
/// DEPRECATED — do NOT emit new instances. Kept declared-only for Marten
/// backward-compatible replay of events written prior to 2026-04-20.
/// Use <see cref="ExamSimulationSubmitted_V2"/> for all new writes.
/// The readiness bounds on this record violate ADR-0003 (readiness is
/// session-scoped, never persisted) — see prr-013 follow-up.
/// </summary>
[Obsolete("Use ExamSimulationSubmitted_V2. V1 persists readiness bounds, which ADR-0003 forbids. Retained for historical replay only.", error: false)]
public record ExamSimulationSubmitted_V1(
    string StudentId,
    string SimulationId,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double ScorePercent,
    TimeSpan TimeTaken,
    int VisibilityWarnings,
    double ReadinessLowerBound,
    double ReadinessUpperBound,
    DateTimeOffset SubmittedAt
) : IDelegatedEvent;

/// <summary>
/// Post-prr-013 exam-submission event. No readiness bounds on-stream —
/// any session-scoped risk assessment (bucket + CI) lives on
/// <c>SessionRiskAssessment</c> inside the session actor and never leaves
/// the in-session surface (ADR-0003, RDY-080).
/// </summary>
public record ExamSimulationSubmitted_V2(
    string StudentId,
    string SimulationId,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double ScorePercent,
    TimeSpan TimeTaken,
    int VisibilityWarnings,
    DateTimeOffset SubmittedAt
) : IDelegatedEvent;

public record ExamVisibilityWarning_V1(
    string StudentId,
    string SimulationId,
    string VisibilityState,
    TimeSpan DurationAway,
    DateTimeOffset DetectedAt
) : IDelegatedEvent;
