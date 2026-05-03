// =============================================================================
// Cena Platform — MartenTeacherOverrideStore (prr-150 prod binding)
//
// Production Marten-backed implementation of ITeacherOverrideStore.
// Replaces InMemoryTeacherOverrideStore as the production DI binding per
// memory "No stubs — production grade" (2026-04-11). While the in-memory
// rationale ("teacher re-applies the override on pod restart") was
// reasonable as a Phase-1 stopgap, it silently erodes teacher trust at
// scale: every deploy reverts every motivation-profile override, budget
// adjustment, and pinned topic. This commit persists the stream via
// Marten so overrides carry across restarts.
//
// Pattern mirrors MartenStudentPlanAggregateStore (prr-218) and
// MartenConsentAggregateStore (prr-155 / ADR-0042). Stream key
// `teacheroverride-{studentAnonId}` via TeacherOverrideAggregate.StreamKey
// — unchanged from the InMemory binding so existing streams replay
// cleanly through either implementation.
// =============================================================================

using Marten;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Marten-backed event store for <see cref="TeacherOverrideAggregate"/>.
/// Thread safety is delegated to Marten's session-per-unit-of-work model
/// (a fresh LightweightSession per write, fresh QuerySession per read).
/// </summary>
public sealed class MartenTeacherOverrideStore : ITeacherOverrideStore
{
    private readonly IDocumentStore _store;

    public MartenTeacherOverrideStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        string studentAnonId,
        object @event,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }
        ArgumentNullException.ThrowIfNull(@event);

        var streamKey = TeacherOverrideAggregate.StreamKey(studentAnonId);
        await using var session = _store.LightweightSession();

        var existing = await session.Events
            .FetchStreamStateAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            session.Events.StartStream<TeacherOverrideAggregate>(streamKey, @event);
        }
        else
        {
            session.Events.Append(streamKey, @event);
        }
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TeacherOverrideAggregate> LoadAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        var streamKey = TeacherOverrideAggregate.StreamKey(studentAnonId);
        await using var session = _store.QuerySession();
        var events = await session.Events
            .FetchStreamAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        if (events.Count == 0)
        {
            return new TeacherOverrideAggregate();
        }

        return TeacherOverrideAggregate.ReplayFrom(events.Select(e => e.Data));
    }
}
