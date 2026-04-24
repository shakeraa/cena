// =============================================================================
// Cena Platform — In-memory TeacherOverrideStore (prr-150)
//
// Thread-safe ConcurrentDictionary-backed in-memory impl. Serves both as
// the test double and the Phase-1 production fallback until a Marten-
// backed implementation ships. Same rationale as prr-148's in-memory
// StudentPlan store: the read path is on the hot scheduler loop and
// durability loss on pod restart is "teacher re-applies the override",
// which is acceptable relative to shipping Marten plumbing before the
// aggregate catalog stabilizes.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// In-memory implementation of <see cref="ITeacherOverrideStore"/>.
/// </summary>
public sealed class InMemoryTeacherOverrideStore : ITeacherOverrideStore
{
    private readonly ConcurrentDictionary<string, List<object>> _streams =
        new(StringComparer.Ordinal);

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
    public Task<TeacherOverrideAggregate> LoadAsync(string studentAnonId, CancellationToken ct = default)
    {
        if (!_streams.TryGetValue(studentAnonId, out var list))
        {
            return Task.FromResult(new TeacherOverrideAggregate());
        }
        List<object> snapshot;
        lock (list)
        {
            snapshot = new List<object>(list);
        }
        return Task.FromResult(TeacherOverrideAggregate.ReplayFrom(snapshot));
    }
}
