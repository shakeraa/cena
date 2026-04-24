// =============================================================================
// Cena Platform — Accommodation Profile Service (PRR-151 R-22 compliance fix)
//
// This service is the SEAM between the Accommodations bounded context and
// the session-rendering pipeline. Before this service existed, the
// parent-console endpoint (AccommodationsEndpoints) persisted
// AccommodationProfileAssignedV1 events to the student stream but NO
// session-time code path consulted them. That produced the prr-151 R-22
// defect: Ministry-reportable consent-without-render — parents signed
// for accommodations, auditors found consent events in the log, but the
// platform never technically rendered the accommodation (no TTS button
// turned on, no extended-time multiplier applied, no distraction-reduced
// layout flag on the question DTO).
//
// RESOLUTION: session-rendering callers (SessionEndpoints.GetCurrentQuestion,
// session-pacing code, TTS setup) fetch the student's current profile
// through this service and THEN consult AccommodationProfile.IsEnabled to
// gate whichever rendering decision they own. If the student has never
// had a profile assigned, the service returns
// AccommodationProfile.Default — semantically equivalent to "no
// accommodations configured", and every IsEnabled call returns false.
//
// Implementation note: the profile is a fold of
// AccommodationProfileAssignedV1 events on the student stream. For
// Phase 1B this is a per-request event-scan (O(events-per-student));
// Phase 1C will introduce a dedicated projection document so the read
// is O(1). The service abstraction lets us swap implementations without
// touching callers.
// =============================================================================

namespace Cena.Actors.Accommodations;

/// <summary>
/// Retrieves the current accommodation profile for a student so the
/// session pipeline can gate TTS, extended-time, distraction-reduced
/// layout, and other accommodation-dependent rendering decisions.
/// </summary>
public interface IAccommodationProfileService
{
    /// <summary>
    /// Returns the current (latest) <see cref="AccommodationProfile"/> for
    /// the given student. Never returns null; students with no assigned
    /// profile get <see cref="AccommodationProfile.Default"/> — every
    /// <see cref="AccommodationProfile.IsEnabled"/> call on that default
    /// profile returns false, so renderers that consult this service are
    /// automatically "no-accommodations" when the parent has not opted in.
    /// </summary>
    /// <param name="studentAnonId">The stable student anon id used as
    /// the stream key on the parent-console side.</param>
    /// <param name="ct">Cancellation token for the async fetch.</param>
    Task<AccommodationProfile> GetCurrentAsync(
        string studentAnonId,
        CancellationToken ct = default);
}
