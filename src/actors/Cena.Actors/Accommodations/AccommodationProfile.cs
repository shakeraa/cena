// =============================================================================
// Cena Platform — Accommodation Profile (RDY-066 Phase 1A)
//
// Per-student, session-scoped profile of formal accommodations (Hebrew:
// התאמות). The profile is a typed bundle of opt-ins — each dimension can
// be flipped independently by the parent (for minors) or by the student
// (for 18+). Defaults are OFF: the platform NEVER silently enables an
// accommodation, and NEVER shows the label "accommodations mode" to the
// student UI (Dr. Lior's critique: no "slow mode" visible variants).
//
// Phase 1A ships 4 of 8 dimensions per the RDY-066 spec:
//   - ExtendedTime
//   - TtsForProblemStatements
//   - DistractionReducedLayout
//   - NoComparativeStats
//
// Phase 1B adds: TtsForHints, HighContrastTheme, ReducedAnimations,
// ProgressIndicatorToggle (blocked by the Vuexy #7367F0 contrast audit).
//
// Privacy (ADR-0003 + GDPR Art 9): disability status is a sensitive
// special-category datum. The profile is session-scoped (stored with
// the session snapshot, not the persistent student profile) and flows
// through an AccommodationProfileAssignedV1 event so an audit trail
// exists for parental-consent verification — but never to long-term
// analytics exports (enforced by an ML-exclusion tag).
// =============================================================================

namespace Cena.Actors.Accommodations;

/// <summary>
/// Enumerated accommodation dimensions. Adding a new dimension requires
/// bumping the event schema version and shipping the UI flag wiring;
/// the enum values are stable across versions so event replay works.
/// </summary>
public enum AccommodationDimension
{
    /// <summary>
    /// Student may take as long as they want. The UI MUST hide all
    /// timers (not "25% more timer" — no timer at all). Per RDY-066:
    /// extended time is extended presence of the student's choice,
    /// not a more-generous clock.
    /// </summary>
    ExtendedTime = 1,

    /// <summary>
    /// Problem statements are read aloud via text-to-speech. Voice
    /// locale follows the student's L1 (Levantine Arabic / Modern
    /// Hebrew / English). Phase 1A: problem statements only. Phase 1B
    /// adds hint TTS once voice-prompt authoring lands.
    /// </summary>
    TtsForProblemStatements = 2,

    /// <summary>
    /// One problem per page. No sidebar, no mastery widgets, no
    /// ambient progress bars visible during a session.
    /// </summary>
    DistractionReducedLayout = 3,

    /// <summary>
    /// Hide rankings, peer averages, percentile indicators, and any
    /// comparative chart that names other students (even
    /// aggregate-anonymised). Cross-checks the shipgate GD-004 rules
    /// and the RDY-071 honest-framing gate.
    /// </summary>
    NoComparativeStats = 4,

    // Phase 1B / v2 (not shipped this commit):
    TtsForHints = 5,
    HighContrastTheme = 6,
    ReducedAnimations = 7,
    ProgressIndicatorToggle = 8
}

/// <summary>
/// Dimensions that Phase 1A ships. A runtime flag check uses this set
/// to refuse enabling anything beyond Phase 1A — prevents a config or
/// event-replay from silently enabling a v2 dimension that the UI
/// doesn't render yet.
/// </summary>
public static class Phase1ADimensions
{
    public static readonly IReadOnlySet<AccommodationDimension> Shipped =
        new HashSet<AccommodationDimension>
        {
            AccommodationDimension.ExtendedTime,
            AccommodationDimension.TtsForProblemStatements,
            AccommodationDimension.DistractionReducedLayout,
            AccommodationDimension.NoComparativeStats
        };

    public static bool IsShipped(AccommodationDimension d) => Shipped.Contains(d);
}

/// <summary>
/// Who set the profile. Used for the consent audit trail.
/// </summary>
public enum AccommodationAssigner
{
    /// <summary>Parent / guardian set the profile for a minor.</summary>
    Parent = 0,

    /// <summary>Adult student (18+) set their own profile.</summary>
    Self = 1,

    /// <summary>
    /// Classroom teacher on behalf of a student — Phase 2B only;
    /// blocked in Phase 1A because teacher-set accommodations require
    /// the RDY-070 teacher console + school-delegated-consent flow
    /// that isn't wired yet.
    /// </summary>
    Teacher = 2
}

/// <summary>
/// Immutable accommodation-profile record. Never mutate — to change a
/// profile, emit a new AccommodationProfileAssignedV1 event and the
/// projection folds the delta.
/// </summary>
public sealed record AccommodationProfile(
    string StudentAnonId,
    IReadOnlySet<AccommodationDimension> EnabledDimensions,
    AccommodationAssigner Assigner,
    string AssignerSignature,
    DateTimeOffset AssignedAtUtc,
    string? MinistryHatamaCode = null)
{
    /// <summary>
    /// True when a dimension is enabled for this student. Always false
    /// for dimensions that are not yet shipped (Phase 1A guard).
    /// </summary>
    public bool IsEnabled(AccommodationDimension dimension)
        => Phase1ADimensions.IsShipped(dimension) && EnabledDimensions.Contains(dimension);

    // =========================================================================
    // Session-pipeline consumer helpers (PRR-151 R-22 wiring fix).
    //
    // These are DERIVED from IsEnabled(...) — they do not add new state to
    // the profile and do not widen the bounded context. They give the
    // session-rendering code path a named accessor per decision instead
    // of forcing it to pass an enum token, which makes the wiring visible
    // to the NoUnwiredAccommodationsTest (accommodation flags used by
    // render decisions are trivially grep-able).
    //
    // Defaults when the dimension is OFF match the "no accommodation"
    // baseline the platform already ships: multiplier 1.0 (no time
    // extension), all feature flags false. A student with no profile
    // assignment therefore sees identical UX to today — the fix is purely
    // additive at the render seam.
    // =========================================================================

    /// <summary>
    /// Time-pacing multiplier for <c>ExpectedTimeSeconds</c> (and any other
    /// session-pacing budget). 1.0 when <see cref="AccommodationDimension.ExtendedTime"/>
    /// is OFF, 1.5 when ON — matches the Ministry hatama-1 (הארכת זמן) 50%
    /// extension convention referenced by
    /// <see cref="MinistryAccommodationMapping"/>. See RDY-066 spec
    /// §Extended-Time for the rationale: the UI hides the countdown
    /// entirely, but pacing hints (worked-example timing, fatigue budget)
    /// still need a concrete multiplier.
    /// </summary>
    public double ExtendedTimeMultiplier
        => IsEnabled(AccommodationDimension.ExtendedTime) ? 1.5 : 1.0;

    /// <summary>
    /// True when problem-statement text-to-speech is authorised for this
    /// student (Ministry hatama-2 / <see cref="AccommodationDimension.TtsForProblemStatements"/>).
    /// Session endpoints gate TTS rendering on this flag so a student
    /// without a signed consent event never hears the TTS voice, even if
    /// the client-side UI has a TTS button code-path present.
    /// </summary>
    public bool TtsForProblemStatementsEnabled
        => IsEnabled(AccommodationDimension.TtsForProblemStatements);

    /// <summary>
    /// True when the student's parent (or self, for adults) has opted
    /// into the distraction-reduced one-problem-per-page layout
    /// (Ministry hatama-3 / <see cref="AccommodationDimension.DistractionReducedLayout"/>).
    /// Carried on the question-delivery DTO so the frontend can switch
    /// to the minimal layout; authoring-side graph-paper overlay and
    /// similar render-time reductions sit behind the same flag because
    /// they're all the same accessibility class (reduce incidental
    /// visual load).
    /// </summary>
    public bool DistractionReducedLayoutEnabled
        => IsEnabled(AccommodationDimension.DistractionReducedLayout);

    /// <summary>
    /// True when comparative / peer-ranked stats must be hidden from the
    /// student UI (Ministry hatama-4 / <see cref="AccommodationDimension.NoComparativeStats"/>).
    /// Kept as a named accessor for symmetry; session-summary and mastery
    /// widgets consult this before rendering percentiles, leaderboards,
    /// or cohort comparisons.
    /// </summary>
    public bool NoComparativeStatsRequired
        => IsEnabled(AccommodationDimension.NoComparativeStats);

    /// <summary>
    /// Convenience: a default "no accommodations" profile for students
    /// whose parent has not assigned anything. The downstream session
    /// pipeline uses this as the base state.
    /// </summary>
    public static AccommodationProfile Default(string studentAnonId) =>
        new(
            StudentAnonId: studentAnonId,
            EnabledDimensions: new HashSet<AccommodationDimension>(),
            Assigner: AccommodationAssigner.Self,
            AssignerSignature: "default",
            AssignedAtUtc: DateTimeOffset.UtcNow);
}

/// <summary>
/// Event emitted on the student's stream when an accommodation
/// profile is assigned (or re-assigned). Persists only the fact
/// that an assignment happened + who did it; the current profile is
/// a fold of the latest event per student.
///
/// Schema V1 — phase 1A only. Adding v2 dimensions bumps to V2 with
/// a schema upcaster (ADR-0001 tenancy-event-upcaster pattern).
/// </summary>
public sealed record AccommodationProfileAssignedV1(
    string StudentAnonId,
    IReadOnlyCollection<AccommodationDimension> EnabledDimensions,
    AccommodationAssigner Assigner,
    string AssignerSignature,
    string? MinistryHatamaCode,
    string? ConsentDocumentHash,
    DateTimeOffset AssignedAtUtc);
