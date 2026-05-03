// =============================================================================
// Cena Platform — LearningSession bounded-context event: SessionStarted_V2
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock) — LearningSession extraction
//
// This is the V2 migration target for the legacy SessionStarted_V1 event that
// currently lives inside the StudentActor god-aggregate (Events/PedagogyEvents.cs).
// Phase 1 writes this event via shadow-write alongside V1; no read path is cut
// over yet. See ADR-0012 migration strategy.
//
// Key difference from V1:
//   - V1 is appended to the student event stream (`student-{studentId}`), where
//     it is mixed with 17+ other event types.
//   - V2 is appended to the session event stream (`session-{sessionId}`) owned
//     by the new LearningSession bounded context. The stream key is the
//     SessionId carried by the record — see ADR-0012 "Target Bounded Contexts".
//
// The payload is byte-for-byte identical to V1 plus the stream-key contract.
// Downstream projections consume V2 via LearningSessionProjection.
// =============================================================================

namespace Cena.Actors.Sessions.Events;

/// <summary>
/// Emitted when a learning session begins. Appended to the LearningSession
/// stream <c>session-{SessionId}</c> by <c>ILearningSessionShadowWriter</c>
/// whenever <c>StudentActor.HandleStartSession</c> appends the legacy
/// <c>SessionStarted_V1</c> to the student stream.
/// <para>
/// Payload mirrors <c>Cena.Actors.Events.SessionStarted_V1</c> so that Phase 2
/// (cutover) is a rename-only migration for consumers. The meaningful shift is
/// the stream identity, not the payload.
/// </para>
/// </summary>
/// <param name="StudentId">The learner the session belongs to.</param>
/// <param name="SessionId">Stream key — uniquely identifies this session
/// stream (<c>session-{SessionId}</c>).</param>
/// <param name="DeviceType">Client device category (web, mobile-pwa, etc.).</param>
/// <param name="AppVersion">Client app build identifier.</param>
/// <param name="Methodology">Starting pedagogical methodology for the session.</param>
/// <param name="ExperimentCohort">Optional A/B experiment cohort tag.</param>
/// <param name="IsOffline">Whether the session was started in offline mode.</param>
/// <param name="ClientTimestamp">The client's reported start time (authoritative
/// for ordering within the session stream).</param>
/// <param name="SchoolId">REV-014 tenant scope. Null for events authored before
/// the tenant field existed, same as V1.</param>
public record SessionStarted_V2(
    string StudentId,
    string SessionId,
    string DeviceType,
    string AppVersion,
    string Methodology,
    string? ExperimentCohort,
    bool IsOffline,
    DateTimeOffset ClientTimestamp,
    string? SchoolId = null);
