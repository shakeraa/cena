// =============================================================================
// Cena Platform — StudentPlanAggregate (prr-218, supersedes prr-148)
//
// Aggregate root for a student's multi-target exam plan per ADR-0050.
// Stream key: `studentplan-{studentId}` (unchanged from prr-148; streams
// carry both legacy ExamDateSet_V1 / WeeklyTimeBudgetSet_V1 events and
// the new ExamTarget* events for backward compat during the prr-219
// migration window).
//
// *** CRITICAL ARCHITECTURE CONSTRAINT ***
// This aggregate exists as a successor bounded context to StudentActor
// per ADR-0012 (StudentActor split). NoNewStudentActorStateTest blocks
// adding further event-handler cases to StudentActor*.cs, so ALL new
// plan state (multi-target per ADR-0050 §1) lands here.
//
// Design:
//   - Stream key format `studentplan-{studentId}` stays identical across
//     the prr-148 → prr-218 transition so existing streams replay
//     cleanly.
//   - Event apply is delegated to StudentPlanState so the aggregate is a
//     thin dispatch shell. Command handlers in
//     <see cref="StudentPlanCommandHandler"/> enforce invariants.
//   - Unknown events are silently ignored (forward-migration tolerance).
//
// Invariants enforced at the COMMAND handler, not here (fold is
// permissive by design so replay of legacy streams with pre-invariant
// data cannot throw):
//   - ADR-0050 §5: count(active) ≤ 5, sum(WeeklyHours) ≤ 40,
//     (ExamCode, SittingCode, Track) unique across active.
//   - §6: ArchivedAt terminal.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Aggregate root for a single student's multi-target exam plan stream.
/// Stream key: <c>studentplan-{studentId}</c>.
/// </summary>
public sealed class StudentPlanAggregate
{
    /// <summary>Conventional stream-key prefix for this aggregate.</summary>
    public const string StreamKeyPrefix = "studentplan-";

    /// <summary>Build the stream key for a student id.</summary>
    public static string StreamKey(string studentAnonId)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "Student anon id must be non-empty for stream-key construction.",
                nameof(studentAnonId));
        }
        return StreamKeyPrefix + studentAnonId;
    }

    /// <summary>Backing state carried by this aggregate instance.</summary>
    public StudentPlanState State { get; } = new();

    /// <summary>
    /// Apply an inbound domain event. Unknown events are silently ignored.
    /// </summary>
    public void Apply(object @event)
    {
        switch (@event)
        {
            // Multi-target (prr-218) events.
            case StudentPlanInitialized_V1 init:
                State.Apply(init);
                break;
            case ExamTargetAdded_V1 added:
                State.Apply(added);
                break;
            case ExamTargetUpdated_V1 updated:
                State.Apply(updated);
                break;
            case ExamTargetArchived_V1 archived:
                State.Apply(archived);
                break;
            case ExamTargetCompleted_V1 completed:
                State.Apply(completed);
                break;
            case ExamTargetOverrideApplied_V1 overrideApplied:
                State.Apply(overrideApplied);
                break;
            case StudentPlanMigrationFailed_V1 migFailed:
                State.Apply(migFailed);
                break;
            case StudentPlanMigrated_V1 migrated:
                State.Apply(migrated);
                break;

            // Legacy (prr-148) events.
            case ExamDateSet_V1 examSet:
                State.Apply(examSet);
                break;
            case WeeklyTimeBudgetSet_V1 budgetSet:
                State.Apply(budgetSet);
                break;
        }
    }

    /// <summary>Replay a sequence of events into a fresh aggregate.</summary>
    public static StudentPlanAggregate ReplayFrom(IEnumerable<object> events)
    {
        var aggregate = new StudentPlanAggregate();
        foreach (var evt in events)
        {
            aggregate.Apply(evt);
        }
        return aggregate;
    }

    /// <summary>
    /// Project the aggregate's state into the legacy single-target
    /// <see cref="StudentPlanConfig"/> VO consumed by the scheduler
    /// bridge (prr-149). Prefers new multi-target state when available;
    /// falls back to legacy scalar fields during the migration window.
    /// </summary>
    /// <remarks>
    /// Projection rules per ADR-0050 §1:
    ///  - If there is at least one active target, sum active weekly hours
    ///    → TimeSpan (capped at 40h invariant). Take the earliest active
    ///    target's sitting canonical date as the deadline (the catalog
    ///    canonical-date lookup is supplied by the caller via
    ///    <see cref="StudentPlanInputsService"/>; this raw projection
    ///    leaves it as null so the scheduler bridge applies its default).
    ///  - If no active target but legacy scalars are set (pre-migration
    ///    student), project the legacy values as-is.
    ///  - If neither, return all-nulls.
    /// </remarks>
    public StudentPlanConfig ToConfig(string studentAnonId)
    {
        var active = State.ActiveTargets;

        if (active.Count > 0)
        {
            // Multi-target world: the scheduler bridge needs a SINGLE
            // deadline + weekly budget. Per ADR-0050 §10 (14-day exam-
            // week lock is scheduler-only), the scheduler picks a
            // target to run against. The projection chooses the earliest
            // canonical sitting date when the catalog supplies one, but
            // the raw aggregate does not know the catalog — it leaves
            // DeadlineUtc null here and lets the bridge fill in via the
            // catalog. WeeklyBudget is the sum of active hours.
            var totalHours = active.Sum(t => t.WeeklyHours);
            return new StudentPlanConfig(
                StudentAnonId: studentAnonId,
                DeadlineUtc: null,
                WeeklyBudget: TimeSpan.FromHours(totalHours),
                UpdatedAt: State.UpdatedAt);
        }

        // Pre-migration student: project legacy scalars directly.
        return new StudentPlanConfig(
            StudentAnonId: studentAnonId,
            DeadlineUtc: State.LegacyDeadlineUtc,
            WeeklyBudget: State.LegacyWeeklyBudget,
            UpdatedAt: State.UpdatedAt);
    }

    /// <summary>
    /// Convenience — true if the student has any active target.
    /// </summary>
    public bool HasActiveTargets => State.ActiveTargets.Count > 0;

    /// <summary>
    /// Convenience — true if the student has any stream event at all.
    /// </summary>
    public bool IsInitialized => State.InitializedAt is not null
        || State.LegacyDeadlineUtc is not null
        || State.LegacyWeeklyBudget is not null;
}
