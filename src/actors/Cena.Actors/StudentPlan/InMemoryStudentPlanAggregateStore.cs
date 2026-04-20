// =============================================================================
// Cena Platform — In-memory StudentPlanAggregate store (prr-148)
//
// Thread-safe (ConcurrentDictionary) in-memory impl of
// IStudentPlanAggregateStore. Serves as both the test-double and the
// Phase-1 production fallback until a Marten-backed store lands.
//
// Rationale for shipping in-memory to prod first: the plan-config read
// path is on the hot scheduler loop and the 2nd-order value of durability
// (student re-sets their deadline after a pod restart) is very low relative
// to the complexity of a Marten-backed read-side that needs to co-exist
// with the StudentActor decomposition still in progress (ADR-0012). The
// Marten overlay is a follow-up once the aggregate stream catalog
// stabilizes.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// In-memory implementation of <see cref="IStudentPlanAggregateStore"/>.
/// </summary>
public sealed class InMemoryStudentPlanAggregateStore : IStudentPlanAggregateStore
{
    private readonly ConcurrentDictionary<string, List<object>> _streams = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task AppendAsync(string studentAnonId, object @event, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException("studentAnonId must be non-empty.", nameof(studentAnonId));
        }
        ArgumentNullException.ThrowIfNull(@event);

        var list = _streams.GetOrAdd(studentAnonId, _ => new List<object>());
        lock (list)
        {
            list.Add(@event);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StudentPlanAggregate> LoadAsync(string studentAnonId, CancellationToken ct = default)
    {
        if (!_streams.TryGetValue(studentAnonId, out var list))
        {
            return Task.FromResult(new StudentPlanAggregate());
        }
        List<object> snapshot;
        lock (list)
        {
            snapshot = new List<object>(list);
        }
        return Task.FromResult(StudentPlanAggregate.ReplayFrom(snapshot));
    }
}
