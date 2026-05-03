// =============================================================================
// Cena Platform — ConsentAggregate store abstraction (prr-155)
//
// Thin repository abstraction for the ConsentAggregate. Production backing
// will be Marten (per ADR-0012 convention for event-sourced aggregates);
// tests use InMemoryConsentAggregateStore for deterministic behaviour.
//
// The abstraction is deliberately narrow:
//   - AppendAsync: append an event to the stream (no conflict semantics
//     exposed here; Marten's optimistic-concurrency is internal).
//   - LoadAsync: rebuild the aggregate from its stream (full replay).
//
// No update-in-place, no projection primitives, no snapshotting — those
// are internal implementation details of the concrete store.
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Repository abstraction for the <see cref="ConsentAggregate"/>.
/// </summary>
public interface IConsentAggregateStore
{
    /// <summary>
    /// Append an event to the consent stream identified by <paramref name="subjectId"/>.
    /// </summary>
    Task AppendAsync(string subjectId, object @event, CancellationToken ct = default);

    /// <summary>
    /// Load the aggregate by replaying its stream. Returns an aggregate
    /// with an empty state if no events have been recorded for this subject.
    /// </summary>
    Task<ConsentAggregate> LoadAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// prr-130: Read the raw event sequence for a subject in append order.
    /// Distinct from <see cref="LoadAsync"/> which folds to a single
    /// state snapshot — the admin audit exporter needs every event,
    /// including grant/revoke/veto history, with its original timestamp
    /// and role.
    ///
    /// Tenant scoping is the HTTP boundary's responsibility (see the
    /// <c>ConsentAuditExportEndpoint</c>, which enforces institute
    /// matching via <c>TenantScope.GetInstituteFilter</c> before
    /// consulting this method).
    /// </summary>
    Task<IReadOnlyList<object>> ReadEventsAsync(
        string subjectId, CancellationToken ct = default);
}
