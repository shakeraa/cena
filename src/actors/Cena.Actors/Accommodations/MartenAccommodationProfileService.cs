// =============================================================================
// Cena Platform — Marten-backed AccommodationProfileService (PRR-151 R-22)
//
// Production implementation of IAccommodationProfileService. Folds the
// student's stream of AccommodationProfileAssignedV1 events into the
// current AccommodationProfile by taking the most recent event (by
// AssignedAtUtc). Matches the read-path the parent-console GET endpoint
// already uses (AccommodationsEndpoints.HandleGetAsync) so the student
// session sees exactly what the parent console sees — no divergence.
//
// Phase 1B scan behaviour: each call issues a Marten query that pulls
// the newest AccommodationProfileAssignedV1 for the given student. This
// is acceptable for Phase 1B traffic (accommodations are a small slice
// of total sessions) and matches the parent-console's read-path cost.
// Phase 1C will replace this with a dedicated projection document so
// the read becomes O(1).
//
// No caching layer here — a stale cache would defeat the whole purpose
// of the wiring fix. If a parent flips a dimension, the next question
// the student sees must reflect it.
// =============================================================================

using Marten;

namespace Cena.Actors.Accommodations;

/// <summary>
/// Marten-backed <see cref="IAccommodationProfileService"/>. Reads
/// <see cref="AccommodationProfileAssignedV1"/> from the student stream
/// and folds to the latest profile.
/// </summary>
public sealed class MartenAccommodationProfileService : IAccommodationProfileService
{
    private readonly IDocumentStore _store;

    public MartenAccommodationProfileService(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<AccommodationProfile> GetCurrentAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return AccommodationProfile.Default(studentAnonId ?? string.Empty);

        await using var session = _store.QuerySession();

        // Mirror the parent-console read path (AccommodationsEndpoints
        // HandleGetAsync, lines ~93–98): fold the most recent assigned
        // event for this student. Phase 1C will introduce a projection
        // document so this becomes an O(1) LoadAsync instead.
        var latest = await session.Events
            .QueryRawEventDataOnly<AccommodationProfileAssignedV1>()
            .Where(e => e.StudentAnonId == studentAnonId)
            .OrderByDescending(e => e.AssignedAtUtc)
            .Take(1)
            .ToListAsync(ct);

        if (latest.Count == 0)
        {
            // No consent event on file — the student has no assigned
            // profile and therefore receives zero accommodations. The
            // Default profile's IsEnabled(...) returns false for every
            // dimension so callers naturally render the non-accommodated
            // path.
            return AccommodationProfile.Default(studentAnonId);
        }

        var ev = latest[0];
        return new AccommodationProfile(
            StudentAnonId: ev.StudentAnonId,
            EnabledDimensions: new HashSet<AccommodationDimension>(ev.EnabledDimensions),
            Assigner: ev.Assigner,
            AssignerSignature: ev.AssignerSignature,
            AssignedAtUtc: ev.AssignedAtUtc,
            MinistryHatamaCode: ev.MinistryHatamaCode);
    }
}
