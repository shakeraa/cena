// =============================================================================
// Cena Platform — TeacherOverrideAggregate store abstraction (prr-150)
//
// Sibling of IStudentPlanAggregateStore. Thin Append + Load shape; the
// Marten-backed concrete is a follow-up once the aggregate catalog
// stabilizes (same staging as prr-148).
// =============================================================================

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Repository abstraction for <see cref="TeacherOverrideAggregate"/>.
/// </summary>
public interface ITeacherOverrideStore
{
    /// <summary>
    /// Append an event to the override stream identified by
    /// <paramref name="studentAnonId"/>.
    /// </summary>
    Task AppendAsync(string studentAnonId, object @event, CancellationToken ct = default);

    /// <summary>
    /// Load the aggregate by replaying its stream. Returns an aggregate
    /// with empty state if no events have been recorded for this student.
    /// </summary>
    Task<TeacherOverrideAggregate> LoadAsync(string studentAnonId, CancellationToken ct = default);
}
