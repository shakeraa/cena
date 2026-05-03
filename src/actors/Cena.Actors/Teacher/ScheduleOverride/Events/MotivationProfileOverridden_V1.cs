// =============================================================================
// Cena Platform — TeacherOverrideAggregate event: MotivationProfileOverridden_V1 (prr-150)
//
// Emitted when a teacher / mentor overrides the student's scheduler
// motivation profile (Confident / Neutral / Anxious framing, see
// AdaptiveScheduler.MotivationProfile). Useful for scenarios such as an
// anxious student the teacher knows responds better to confident framing
// during a specific exam-prep push.
//
// The event carries a `SessionTypeScope` discriminator so the override can
// be narrowed to a subset of session types later (for example, "only apply
// to diagnostic sessions"). Phase-1 uses the sentinel "all"; future values
// land as new scope identifiers without a schema bump because the field is
// a string. Consumers that see an unknown scope treat it as no-match and
// fall through to the non-overridden profile — forward-compatible.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Teacher.ScheduleOverride.Events;

/// <summary>
/// Teacher overrode the student's motivation framing for scheduler copy.
/// Appended to <c>teacheroverride-{studentAnonId}</c>. Latest event wins
/// per <paramref name="SessionTypeScope"/>.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="SessionTypeScope">Session-type filter the override applies to. Phase 1 uses <c>"all"</c>; future versions may narrow.</param>
/// <param name="OverrideProfile">Motivation profile to use in place of the student's own.</param>
/// <param name="TeacherActorId">Pseudonymous teacher id for audit trail.</param>
/// <param name="InstituteId">Tenant id — verified by the command handler.</param>
/// <param name="Rationale">Free-text teacher audit rationale.</param>
/// <param name="SetAt">Wall-clock of the teacher's action (UTC).</param>
public sealed record MotivationProfileOverridden_V1(
    string StudentAnonId,
    string SessionTypeScope,
    MotivationProfile OverrideProfile,
    string TeacherActorId,
    string InstituteId,
    string Rationale,
    DateTimeOffset SetAt);
