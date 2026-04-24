// =============================================================================
// Cena Platform — MartenConsentAggregateStore (prr-155 / ADR-0042 prod)
//
// Production Marten-backed implementation of IConsentAggregateStore.
// Replaces InMemoryConsentAggregateStore as the production DI binding per
// memory "No stubs — production grade" (2026-04-11). The consent stream
// is the compliance audit trail for ADR-0042 (consent aggregate bounded
// context) and ADR-0038 (event-sourced RTBF) — an in-memory fallback
// loses the entire history on every host restart, which is
// unrecoverable: consent grants, revocations, age-band transitions, and
// parent overrides all disappear, and there is no way to reconstruct
// them from JWTs (which carry only the derived `parent_of` hint).
//
// Pattern mirrors MartenStudentPlanAggregateStore (prr-218) and
// MartenSubscriptionAggregateStore (EPIC-PRR-I). Stream key
// `consent-{subjectId}` via ConsentAggregate.StreamKey — unchanged, so
// existing streams replay cleanly through this implementation too.
// =============================================================================

using Marten;

namespace Cena.Actors.Consent;

/// <summary>
/// Marten-backed event store for <see cref="ConsentAggregate"/>.
/// Thread safety is delegated to Marten's session-per-unit-of-work model
/// (a fresh LightweightSession per write, fresh QuerySession per read).
/// </summary>
public sealed class MartenConsentAggregateStore : IConsentAggregateStore
{
    private readonly IDocumentStore _store;

    public MartenConsentAggregateStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        string subjectId,
        object @event,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("subjectId must be non-empty.", nameof(subjectId));
        }
        ArgumentNullException.ThrowIfNull(@event);

        var streamKey = ConsentAggregate.StreamKey(subjectId);
        await using var session = _store.LightweightSession();

        // FetchStreamStateAsync discriminates first-event (StartStream) vs.
        // subsequent-event (Append) paths without a try/catch on a
        // stream-not-found exception.
        var existing = await session.Events
            .FetchStreamStateAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            session.Events.StartStream<ConsentAggregate>(streamKey, @event);
        }
        else
        {
            session.Events.Append(streamKey, @event);
        }
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ConsentAggregate> LoadAsync(
        string subjectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("subjectId must be non-empty.", nameof(subjectId));
        }

        var streamKey = ConsentAggregate.StreamKey(subjectId);
        await using var session = _store.QuerySession();
        var events = await session.Events
            .FetchStreamAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        // Empty stream → fresh aggregate with default state, matching the
        // InMemory contract ("returns an aggregate with an empty state if
        // no events have been recorded for this subject").
        if (events.Count == 0)
        {
            return new ConsentAggregate();
        }

        return ConsentAggregate.ReplayFrom(events.Select(e => e.Data));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<object>> ReadEventsAsync(
        string subjectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return Array.Empty<object>();
        }

        var streamKey = ConsentAggregate.StreamKey(subjectId);
        await using var session = _store.QuerySession();
        var events = await session.Events
            .FetchStreamAsync(streamKey, token: ct)
            .ConfigureAwait(false);
        return events.Select(e => e.Data).ToArray();
    }
}
