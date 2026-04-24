// =============================================================================
// Cena Platform — StudentPlanAggregate store abstraction (prr-148)
//
// Thin repository abstraction. Production backing will be Marten (event
// store); tests use InMemoryStudentPlanAggregateStore for deterministic
// behaviour. Shape: AppendAsync + LoadAsync, no update-in-place, no
// projection primitives — those are internal implementation details of
// the concrete store.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Repository abstraction for the <see cref="StudentPlanAggregate"/>.
/// </summary>
public interface IStudentPlanAggregateStore
{
    /// <summary>
    /// Append an event to the plan stream identified by
    /// <paramref name="studentAnonId"/>.
    /// </summary>
    Task AppendAsync(string studentAnonId, object @event, CancellationToken ct = default);

    /// <summary>
    /// Load the aggregate by replaying its stream. Returns an aggregate
    /// with empty state if no events have been recorded for this student.
    /// </summary>
    Task<StudentPlanAggregate> LoadAsync(string studentAnonId, CancellationToken ct = default);
}
