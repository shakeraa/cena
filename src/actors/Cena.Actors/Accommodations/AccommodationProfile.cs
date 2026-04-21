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
    ProgressIndicatorToggle = 8,

    /// <summary>
    /// LD-anxious-friendly hint governor (prr-029). NOT Ministry-issued —
    /// this is a Cena-native self/parent opt-in that rewrites the L1 hint
    /// template into a concrete worked-step example instead of the default
    /// terse nudge. Zero LLM calls; pure template expansion. Shipped as a
    /// Phase-1B additive dimension (see <see cref="Phase1BDimensions"/>).
    ///
    /// WHY a separate dimension instead of overloading DistractionReducedLayout:
    /// the hint-content change is orthogonal to layout decisions — a student
    /// can benefit from worked-example L1 hints without needing the
    /// one-problem-per-page layout and vice versa. Cognitive-science backing:
    /// Sweller & Renkl's worked-example effect (d≈0.4–0.6) is especially
    /// strong for novice / anxious learners whose working memory is taxed by
    /// the affective load of "I'm stuck again" (Pekrun 2014 — performance
    /// anxiety narrows working-memory capacity). A terse "Consider how X
    /// applies here" imposes higher extraneous load than a concrete
    /// "Try this step: …" on that population.
    /// </summary>
    LdAnxiousFriendly = 9,

    /// <summary>
    /// Dyscalculia accommodation pack (prr-050). NOT Ministry-gated — this
    /// is a Cena-native opt-in that layers on top of whatever Ministry
    /// hatamot the student holds. When enabled, the session-render seam
    /// emits two additional signals:
    ///
    ///   - ShowNumberLineStrip = true  (frontend renders a 0–20 number-
    ///     line strip with a movable marker across every question DTO so
    ///     a student can count/point rather than holding numeric
    ///     cardinality in working memory).
    ///   - Extended-time multiplier = 1.5 (identical to ExtendedTime but
    ///     exposed via DyscalculiaExtendedTimeMultiplier so a student can
    ///     get dyscalculia-only time without also toggling the broader
    ///     ExtendedTime Ministry-facing dimension).
    ///
    /// WHY a separate dimension instead of reusing ExtendedTime +
    /// DistractionReducedLayout: dyscalculia-specific accommodations are
    /// orthogonal to the Ministry hatamot set and are described in
    /// ADR-0040 (accommodation-scope-and-bagrut-parity) as student-
    /// profile-scoped (they travel across enrolments), not enrollment-
    /// scoped (which is where the Ministry extended-time variant lives).
    /// Overloading ExtendedTime would bind dyscalculia support to a
    /// single enrolment's hatama-1 letter; that is the wrong scope.
    ///
    /// Research backing:
    ///
    ///   - Butterworth, Varma &amp; Laurillard (2011) "Dyscalculia: From
    ///     brain to education" (Science 332:1049–1053,
    ///     DOI 10.1126/science.1201536) document that dyscalculic
    ///     learners have an impaired "approximate number system" and
    ///     benefit from EXTERNAL spatial representations of number
    ///     magnitude — a number-line strip is the canonical intervention.
    ///     Effect size for number-line training on arithmetic fluency
    ///     is d≈0.4 (moderate; see Ramani &amp; Siegler 2008 and the
    ///     Fischer, Moeller, Bientzle, Cress &amp; Nuerk 2011 review).
    ///     Per ADR-0049 effect-size honesty: moderate, not transformative.
    ///
    ///   - Shalev, Manor &amp; Gross-Tsur (2005) "Developmental
    ///     dyscalculia: a prospective six-year follow-up" (Developmental
    ///     Medicine &amp; Child Neurology 47:121–125,
    ///     DOI 10.1017/S0012162205000216) — IL-cohort longitudinal study
    ///     showing dyscalculia prevalence of ~6% in Israeli primary
    ///     students with persistence into Bagrut-age cohorts. This is
    ///     the specific population Cena is shipping for; the
    ///     intervention set is not hypothetical.
    ///
    ///   - Extended time for dyscalculia: Gross-Tsur, Manor &amp; Shalev
    ///     (1996) "Developmental dyscalculia: prevalence and demographic
    ///     features" (Developmental Medicine &amp; Child Neurology
    ///     38:25–33) establishes that calculation-fluency deficits scale
    ///     roughly linearly with problem complexity — a 1.5× time
    ///     multiplier is the Israeli Ministry-of-Education standard
    ///     hatama magnitude for specific-maths-LD students (matches
    ///     hatama-1).
    ///
    /// Honest effect-size caveat (ADR-0049): number-line interventions
    /// produce moderate gains in early-grade arithmetic fluency; there
    /// is NO RCT-grade evidence that they improve Bagrut-calibre
    /// performance on complex items. The accommodation is ethically
    /// defensible (reduces cognitive load at zero cost to unaffected
    /// students) but we do not claim it closes an achievement gap.
    /// </summary>
    Dyscalculia = 10
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
/// Dimensions that Phase 1B ships additively on top of Phase 1A. The
/// set is the live "we render this now" gate for dimensions introduced
/// after the Phase 1A freeze (RDY-066). A dimension must appear here
/// AND in the event's <c>EnabledDimensions</c> for any derived
/// accessor to return <c>true</c> — the <see cref="AccommodationProfile.IsEnabled"/>
/// helper consults the union of Phase 1A + Phase 1B.
///
/// prr-029 ships <see cref="AccommodationDimension.LdAnxiousFriendly"/>
/// as the first Phase-1B live dimension. The remaining 1B rows
/// (TtsForHints, HighContrastTheme, ReducedAnimations,
/// ProgressIndicatorToggle) are still blocked on their authoring /
/// contrast-audit upstream work and are NOT listed here — an event
/// replay that carries them in the set will still be refused by the
/// IsEnabled gate.
/// </summary>
public static class Phase1BDimensions
{
    public static readonly IReadOnlySet<AccommodationDimension> Shipped =
        new HashSet<AccommodationDimension>
        {
            AccommodationDimension.LdAnxiousFriendly,
            // prr-050: dyscalculia pack (number-line strip + 1.5x time).
            // Wired in SessionEndpoints.GetCurrentQuestion via the
            // ShowNumberLineStrip + DyscalculiaExtendedTimeMultiplier
            // accessors. Research backing in the enum comment.
            AccommodationDimension.Dyscalculia
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
    /// for dimensions that are not yet shipped (Phase 1A ∪ Phase 1B
    /// guard). Adding a dimension to the enum is harmless; adding it
    /// to the shipped set is the live toggle.
    /// </summary>
    public bool IsEnabled(AccommodationDimension dimension)
        => (Phase1ADimensions.IsShipped(dimension) || Phase1BDimensions.IsShipped(dimension))
           && EnabledDimensions.Contains(dimension);

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
    /// True when the LD-anxious hint governor (prr-029) must rewrite
    /// the L1 hint template into a concrete worked-step example rather
    /// than the default terse prerequisite nudge. Consumed by
    /// <c>Cena.Actors.Hints.ILdAnxiousHintGovernor</c> in the student
    /// session hint pipeline.
    ///
    /// WHY: Pekrun (2014) documents that performance anxiety narrows
    /// working-memory capacity, which amplifies Sweller's extraneous
    /// cognitive load. For novice / anxious learners, a "Consider how
    /// X applies here" nudge imposes the same indirection cost as a
    /// rephrased question — Renkl &amp; Atkinson 2003 faded-worked-
    /// examples (d≈0.4–0.6) show the concrete-step template is
    /// strictly more effective for that population. No LLM call —
    /// deterministic template substitution.
    /// </summary>
    public bool LdAnxiousHintGovernorEnabled
        => IsEnabled(AccommodationDimension.LdAnxiousFriendly);

    /// <summary>
    /// prr-050 — true when the student has the dyscalculia accommodation
    /// pack enabled. Surfaced on the question DTO so the frontend renders
    /// a 0–20 number-line strip beneath the problem (the frontend owns
    /// the visual; the backend merely signals). Research citations live
    /// on <see cref="AccommodationDimension.Dyscalculia"/>.
    /// </summary>
    public bool ShowNumberLineStrip
        => IsEnabled(AccommodationDimension.Dyscalculia);

    /// <summary>
    /// prr-050 — dyscalculia-specific pacing multiplier. Returns 1.5 when
    /// the Dyscalculia dimension is enabled (matches the Ministry hatama-1
    /// magnitude for specific-maths-LD students — Gross-Tsur, Manor &amp;
    /// Shalev 1996), 1.0 otherwise. Exposed as a separate accessor from
    /// <see cref="ExtendedTimeMultiplier"/> so a student can opt into
    /// dyscalculia-only time without toggling the broader Ministry-facing
    /// ExtendedTime dimension. Callers that want the MOST-generous
    /// multiplier across both dimensions should take
    /// <c>Math.Max(profile.ExtendedTimeMultiplier, profile.DyscalculiaExtendedTimeMultiplier)</c>.
    /// </summary>
    public double DyscalculiaExtendedTimeMultiplier
        => IsEnabled(AccommodationDimension.Dyscalculia) ? 1.5 : 1.0;

    /// <summary>
    /// prr-050 — effective session-pacing multiplier combining Ministry
    /// ExtendedTime and Cena-native Dyscalculia. Returns the MAX of the
    /// two so a student who carries both accommodations does not
    /// accidentally get stacked multipliers (we do not compound 1.5 × 1.5
    /// = 2.25 — that is not what either accommodation authorises).
    /// </summary>
    public double SessionTimeMultiplier
        => Math.Max(ExtendedTimeMultiplier, DyscalculiaExtendedTimeMultiplier);

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
