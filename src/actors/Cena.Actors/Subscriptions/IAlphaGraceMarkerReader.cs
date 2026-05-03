// =============================================================================
// Cena Platform — IAlphaGraceMarkerReader (EPIC-PRR-I PRR-344)
//
// Why this exists. The StudentEntitlementResolver is the hot-path seam every
// enforcement site (LLM router, photo-diagnostic intake, dashboard gate)
// consults to answer "what tier is this student on right now?". Before this
// task, that resolver could only answer from subscription event streams —
// which means alpha users with an active grace marker (but no paid Stripe
// subscription) would fall through to Unsubscribed caps and lose the 60-day
// Premium runway the migration policy promised them.
//
// IAlphaGraceMarkerReader closes the loop. The resolver asks "is there an
// active AlphaGraceMarker for this student's parent?"; if yes, it
// synthesises a Premium StudentEntitlementView with source tagged
// "alpha-grace" so analytics can separate natural-Premium from grace-
// Premium without a DB join. The reader contract is intentionally tiny
// (one parent-id lookup) so the resolver path stays cache-friendly; the
// lookup is also O(1) Marten-document-load keyed on the parent id.
//
// Production wiring:
//   - InMemoryAlphaGraceMarkerReader: tests + single-host dev. Shares a
//     dictionary with the worker so a test that runs the worker then
//     resolves an entitlement sees the marker consistently.
//   - MartenAlphaGraceMarkerReader: production. Reads the same
//     AlphaGraceMarker documents the worker wrote. No write path here —
//     markers are only ever created by the worker.
//
// Labels-match-data memory (2026-04-11): the synthesised entitlement view's
// SourceParentSubjectIdEncrypted field is the parent that owns the grace
// marker, and the caller-visible "EffectiveTier" is genuinely Premium for
// cap-enforcement purposes. We do NOT lie via an elevated tier the data
// doesn't support; the grace marker IS the entitlement record.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Read-path contract for <see cref="AlphaGraceMarker"/> documents.
/// Implementations must be safe for concurrent reads on the hot path.
/// </summary>
public interface IAlphaGraceMarkerReader
{
    /// <summary>
    /// Return the active marker for <paramref name="parentSubjectIdEncrypted"/>
    /// (i.e. <c>GraceEndAt &gt; now</c>) or null if no marker exists or the
    /// window has elapsed. <paramref name="nowUtc"/> is injected so tests
    /// can pin time without mocking the clock.
    /// </summary>
    Task<AlphaGraceMarker?> FindActiveAsync(
        string parentSubjectIdEncrypted,
        DateTimeOffset nowUtc,
        CancellationToken ct);
}

/// <summary>
/// In-memory reader + write-through helper used by tests and single-host
/// dev. Exposes <see cref="Upsert"/> so tests can drop markers into the
/// store without standing up Marten. Thread-safe.
/// </summary>
public sealed class InMemoryAlphaGraceMarkerReader : IAlphaGraceMarkerReader
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AlphaGraceMarker>
        _markers = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<AlphaGraceMarker?> FindActiveAsync(
        string parentSubjectIdEncrypted,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            return Task.FromResult<AlphaGraceMarker?>(null);
        }
        if (!_markers.TryGetValue(parentSubjectIdEncrypted, out var marker))
        {
            return Task.FromResult<AlphaGraceMarker?>(null);
        }
        // Active = end-at strictly greater than now. Exactly-at-end is
        // treated as expired so the boundary flip is monotonic.
        if (marker.GraceEndAt <= nowUtc)
        {
            return Task.FromResult<AlphaGraceMarker?>(null);
        }
        return Task.FromResult<AlphaGraceMarker?>(marker);
    }

    /// <summary>Seed a marker (test helper; also used by worker in-memory mode).</summary>
    public void Upsert(AlphaGraceMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (string.IsNullOrWhiteSpace(marker.Id))
        {
            throw new ArgumentException("Marker Id (parent subject id) is required.", nameof(marker));
        }
        _markers[marker.Id] = marker;
    }
}

/// <summary>
/// Marten-backed reader. Reads the same <see cref="AlphaGraceMarker"/>
/// documents the <see cref="AlphaUserMigrationWorker"/> writes.
/// </summary>
public sealed class MartenAlphaGraceMarkerReader : IAlphaGraceMarkerReader
{
    private readonly IDocumentStore _store;

    public MartenAlphaGraceMarkerReader(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task<AlphaGraceMarker?> FindActiveAsync(
        string parentSubjectIdEncrypted,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            return null;
        }
        await using var session = _store.QuerySession();
        var marker = await session.LoadAsync<AlphaGraceMarker>(parentSubjectIdEncrypted, ct);
        if (marker is null) return null;
        return marker.GraceEndAt > nowUtc ? marker : null;
    }
}
