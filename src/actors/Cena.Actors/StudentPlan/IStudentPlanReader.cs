// =============================================================================
// Cena Platform — IStudentPlanReader (prr-218)
//
// Read-side facade for the multi-target StudentPlan (ADR-0050). Companion
// to IStudentPlanInputsService — where InputsService projects DOWN to the
// legacy single-target VO for the scheduler bridge, this reader exposes
// the full multi-target view for the new /api/me/exam-targets endpoints
// and for admin queries.
//
// Separate interface so the legacy scheduler bridge is not coupled to
// the richer multi-target DTO shape.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Read-side lookup returning the full multi-target plan view.
/// </summary>
public interface IStudentPlanReader
{
    /// <summary>
    /// List the student's targets. When <paramref name="includeArchived"/>
    /// is false (default), returns active only. Order: insertion order
    /// within Active, then archived targets appended in insertion order.
    /// </summary>
    Task<IReadOnlyList<ExamTarget>> ListTargetsAsync(
        string studentAnonId,
        bool includeArchived = false,
        CancellationToken ct = default);

    /// <summary>
    /// Return a single target by id, or null if not found (for any reason
    /// — archived or non-existent). Callers differentiate via the returned
    /// target's <see cref="ExamTarget.IsActive"/> flag.
    /// </summary>
    Task<ExamTarget?> FindTargetAsync(
        string studentAnonId,
        ExamTargetId targetId,
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation.
/// </summary>
public sealed class StudentPlanReader : IStudentPlanReader
{
    private readonly IStudentPlanAggregateStore _store;

    /// <summary>Wire via DI.</summary>
    public StudentPlanReader(IStudentPlanAggregateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExamTarget>> ListTargetsAsync(
        string studentAnonId,
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException("studentAnonId must be non-empty.", nameof(studentAnonId));
        }

        var aggregate = await _store.LoadAsync(studentAnonId, ct).ConfigureAwait(false);
        var all = aggregate.State.Targets;

        if (includeArchived) return all;

        return all.Where(t => t.IsActive).ToList();
    }

    /// <inheritdoc />
    public async Task<ExamTarget?> FindTargetAsync(
        string studentAnonId,
        ExamTargetId targetId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException("studentAnonId must be non-empty.", nameof(studentAnonId));
        }

        var aggregate = await _store.LoadAsync(studentAnonId, ct).ConfigureAwait(false);
        return aggregate.State.Targets.FirstOrDefault(t => t.Id == targetId);
    }
}
