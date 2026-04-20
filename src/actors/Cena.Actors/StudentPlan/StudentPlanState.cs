// =============================================================================
// Cena Platform — StudentPlanAggregate state (prr-148)
//
// In-memory fold of StudentPlan events into the last-value-wins config.
//
// The fold is intentionally tiny: two fields, last-write-wins. No derived
// invariants, no cross-event consistency checks — those live in the command
// handler (which re-reads this state before emitting a new event).
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Folded state of a single <see cref="StudentPlanAggregate"/> instance.
/// </summary>
public sealed class StudentPlanState
{
    /// <summary>Latest deadline recorded on the stream, if any.</summary>
    public DateTimeOffset? DeadlineUtc { get; private set; }

    /// <summary>Latest weekly budget recorded on the stream, if any.</summary>
    public TimeSpan? WeeklyBudget { get; private set; }

    /// <summary>Wall-clock of the most recent event applied, if any.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Apply an <see cref="ExamDateSet_V1"/> event.</summary>
    public void Apply(ExamDateSet_V1 e)
    {
        DeadlineUtc = e.DeadlineUtc;
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a <see cref="WeeklyTimeBudgetSet_V1"/> event.</summary>
    public void Apply(WeeklyTimeBudgetSet_V1 e)
    {
        WeeklyBudget = e.WeeklyBudget;
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset b)
        => a is null || b > a.Value ? b : a;
}
