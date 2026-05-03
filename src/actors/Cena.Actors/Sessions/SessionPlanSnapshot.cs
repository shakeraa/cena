// =============================================================================
// Cena Platform — SessionPlanSnapshot (prr-149)
//
// SESSION-SCOPED VALUE OBJECT. NEVER persisted to StudentState, StudentActor,
// StudentProfileSnapshot, or any other profile-keyed aggregate/document.
//
// This record wraps the output of AdaptiveScheduler.PrioritizeTopics for a
// single learning session and lives only on the session-scoped surface:
//
//   - As the payload of SessionPlanComputed_V1 in the `session-{SessionId}`
//     Marten event stream (dies when the session stream is archived),
//   - As an in-memory document keyed by `session-plan-{SessionId}` that is
//     deleted when the session ends (see SessionPlanDocument),
//   - As the body of GET /api/session/{sessionId}/plan responses.
//
// ADR-0003 compliance notes:
//   - The plan is derived from mastery estimates (which belong on the student
//     profile), but the *composed* plan snapshot is considered session-local
//     guidance, NOT a persistent mastery artefact. It is allowed to carry
//     topic-level rationale strings because those strings are already the
//     motivation-safe copy AdaptiveScheduler built and they do not themselves
//     constitute misconception data.
//   - No misconception category, no at-risk/readiness scoring, no predicted
//     score. Only topic priority + rationale — the exact surface prr-013's
//     persistence ban was already written to permit.
//
// ADR-0026 compliance notes:
//   - This type is populated by a pure heuristic (AdaptiveScheduler); its
//     construction path does not call any LLM. The
//     SchedulerNoLlmCallTest architecture test enforces this statically.
//
// Do NOT add this type to any `*Snapshot` or `*ProfileSnapshot` aggregate.
// The NoAtRiskPersistenceTest + the new SessionScopedSnapshotTest catch
// regressions where a future refactor tries to pin a plan to the student.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;

namespace Cena.Actors.Sessions;

/// <summary>
/// Session-scoped snapshot of one AdaptiveScheduler run. Wraps the
/// <see cref="CompressedPlan"/> output plus the lightweight metadata
/// callers need to decide "is this plan stale?" without reaching back
/// to the scheduler. Purely a value object — equality is record-based.
/// </summary>
/// <param name="StudentAnonId">Anonymised student identifier. Never the
/// Firebase UID — matches the identifier used by
/// <see cref="AbilityEstimate.StudentAnonId"/>. Session-scoped only.</param>
/// <param name="SessionId">Session stream id. Matches the parent
/// <c>session-{SessionId}</c> Marten stream that holds the
/// SessionPlanComputed_V1 event.</param>
/// <param name="GeneratedAtUtc">Wall-clock time the scheduler ran. Used by
/// the client UI to display "last updated" without a separate round-trip.</param>
/// <param name="PriorityOrdered">Topics ordered by priority score descending
/// (same order AdaptiveScheduler returned them). Immutable to keep downstream
/// renderers honest — the client cannot resort and claim it ran the scheduler.</param>
/// <param name="MotivationProfile">Copied from <see cref="SchedulerInputs"/>
/// so downstream renderers pick the correct rationale copy template without
/// re-deriving the decision. Safe to externalise per ADR-0037 — this is the
/// student's self-declared stance, not a diagnostic category.</param>
/// <param name="DeadlineUtc">Exam deadline the scheduler planned against, if
/// the student supplied one via <c>IStudentPlanConfigService</c>. Null when
/// the scheduler ran against the fallback default (12-week horizon).</param>
/// <param name="WeeklyBudgetMinutes">Weekly time budget the scheduler planned
/// against, in minutes. Surfaced so the client can show "your plan assumes
/// X minutes/week" copy.</param>
public sealed record SessionPlanSnapshot(
    string StudentAnonId,
    string SessionId,
    DateTimeOffset GeneratedAtUtc,
    ImmutableArray<PlanEntry> PriorityOrdered,
    MotivationProfile MotivationProfile,
    DateTimeOffset? DeadlineUtc,
    int WeeklyBudgetMinutes);
