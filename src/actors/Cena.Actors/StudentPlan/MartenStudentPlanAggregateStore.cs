// =============================================================================
// Cena Platform — MartenStudentPlanAggregateStore (prr-218 production binding)
//
// Production Marten-backed implementation of IStudentPlanAggregateStore +
// IMigrationMarkerStore. Replaces InMemoryStudentPlanAggregateStore as the
// production DI binding per memory "No stubs — production grade"
// (2026-04-11). The in-memory store remains the test-suitable default
// wired by AddStudentPlanServices(); production composition roots call
// AddStudentPlanMarten() afterwards to override it.
//
// Pattern mirrors MartenSubscriptionAggregateStore (EPIC-PRR-I PRR-300),
// which is the canonical Marten aggregate-stream pattern in this repo:
// LightweightSession for writes, QuerySession for reads, StartStream on
// the first event, Append thereafter, SaveChangesAsync to commit. Thread
// safety is delegated to Marten's session-per-unit-of-work model (one
// fresh session per call).
//
// Stream identity: string-typed, keyed by `studentplan-{studentAnonId}`.
// Stream-key construction lives on StudentPlanAggregate.StreamKey(...) so
// the same key format carries forward across legacy (prr-148) and
// multi-target (prr-218) event streams without a schema cut.
// =============================================================================

using Cena.Actors.StudentPlan.Events;
using Cena.Actors.StudentPlan.Migration;
using Marten;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Marten-backed event store for <see cref="StudentPlanAggregate"/>.
/// Also satisfies <see cref="IMigrationMarkerStore"/> because the marker
/// check is a simple scan for <c>StudentPlanMigrated_V1</c> events in the
/// same stream — no separate storage surface required.
/// </summary>
public sealed class MartenStudentPlanAggregateStore
    : IStudentPlanAggregateStore, IMigrationMarkerStore
{
    private readonly IDocumentStore _store;

    public MartenStudentPlanAggregateStore(IDocumentStore store)
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

        var streamKey = StudentPlanAggregate.StreamKey(studentAnonId);
        await using var session = _store.LightweightSession();

        // First event opens the stream; subsequent events append.
        // FetchStreamStateAsync returns null when the stream does not
        // exist yet (vs. a state with 0 events), which discriminates the
        // start-stream path cleanly without a try/catch.
        var existing = await session.Events
            .FetchStreamStateAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            session.Events.StartStream<StudentPlanAggregate>(streamKey, @event);
        }
        else
        {
            session.Events.Append(streamKey, @event);
        }
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StudentPlanAggregate> LoadAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        var streamKey = StudentPlanAggregate.StreamKey(studentAnonId);
        await using var session = _store.QuerySession();
        var events = await session.Events
            .FetchStreamAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        // Empty stream (plan never initialised for this student) returns a
        // fresh aggregate with default state — matches InMemory behaviour
        // and preserves the LoadAsync contract: "returns an aggregate with
        // empty state if no events have been recorded for this student".
        if (events.Count == 0)
        {
            return new StudentPlanAggregate();
        }

        return StudentPlanAggregate.ReplayFrom(events.Select(e => e.Data));
    }

    /// <inheritdoc />
    public async Task<bool> HasMarkerAsync(
        string studentAnonId,
        string migrationSourceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentAnonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationSourceId);

        var streamKey = StudentPlanAggregate.StreamKey(studentAnonId);
        await using var session = _store.QuerySession();
        var events = await session.Events
            .FetchStreamAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        // Linear scan is fine — StudentPlanMigrated_V1 appears at most
        // once per student per migration source (idempotency invariant
        // enforced by StudentPlanMigrationService), and the stream depth
        // per student is bounded by Events.ExamTarget* + Migration events,
        // i.e. tens-to-low-hundreds max over a student lifetime.
        return events
            .Select(e => e.Data)
            .OfType<StudentPlanMigrated_V1>()
            .Any(e => string.Equals(
                e.MigrationSourceId,
                migrationSourceId,
                StringComparison.Ordinal));
    }
}
