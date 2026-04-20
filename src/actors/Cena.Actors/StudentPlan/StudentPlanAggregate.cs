// =============================================================================
// Cena Platform — StudentPlanAggregate (prr-148)
//
// Aggregate root for the student's study-plan config: exam date + weekly
// time budget. Stream key `studentplan-{studentId}`; each stream is one
// student's plan history.
//
// *** CRITICAL ARCHITECTURE CONSTRAINT ***
// This aggregate exists as a NEW bounded context precisely because
// NoNewStudentActorStateTest (ADR-0012) blocks adding further event-handler
// cases to StudentActor*.cs. Study-plan config logically belongs on the
// Student aggregate, but that aggregate is being decomposed (EPIC-PRR-A).
// New event-sourced state must land in its own small aggregate instead —
// this is the first cut of that pattern for the plan context.
//
// Design:
//   - Stream key format `studentplan-{studentId}` mirrors the established
//     "<context>-{subjectId}" convention used by the other small aggregates
//     in this codebase.
//   - Event apply is delegated to StudentPlanState so the aggregate itself
//     is a thin stream-key + dispatch shell.
//   - Unknown events are silently ignored (forward-migration tolerance).
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Aggregate root for a single student-plan stream. Stream key:
/// <c>studentplan-{studentId}</c>.
/// </summary>
public sealed class StudentPlanAggregate
{
    /// <summary>Conventional stream-key prefix for this aggregate.</summary>
    public const string StreamKeyPrefix = "studentplan-";

    /// <summary>
    /// Build the stream key for a student id. Callers must have already
    /// validated that <paramref name="studentAnonId"/> is non-empty.
    /// </summary>
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
            case ExamDateSet_V1 examSet:
                State.Apply(examSet);
                break;
            case WeeklyTimeBudgetSet_V1 budgetSet:
                State.Apply(budgetSet);
                break;
        }
    }

    /// <summary>
    /// Replay a sequence of events into a fresh aggregate.
    /// </summary>
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
    /// Project the aggregate's state into the <see cref="StudentPlanConfig"/>
    /// VO consumed by the scheduler bridge (prr-149).
    /// </summary>
    public StudentPlanConfig ToConfig(string studentAnonId)
        => new(
            StudentAnonId: studentAnonId,
            DeadlineUtc: State.DeadlineUtc,
            WeeklyBudget: State.WeeklyBudget,
            UpdatedAt: State.UpdatedAt);
}
