// =============================================================================
// Cena Platform — LD-Anxious Hint Governor (prr-029)
//
// Purpose
// -------
// A governor policy that wraps the output of IHintGenerator.Generate() and,
// when the student's AccommodationProfile has the LdAnxiousFriendly dimension
// enabled, rewrites the L1 hint into a concrete worked-step example instead
// of the default terse nudge. L2/L3 are left intact (governance is scoped to
// the first rung where novice / anxious students most benefit from a
// worked-example scaffold).
//
// WHY (cognitive science evidence) — required for senior-architect review:
// -------------------------------------------------------------------------
//   * Sweller, Van Merriënboer & Paas (2019) "Cognitive Architecture and
//     Instructional Design: 20 Years Later" — the worked-example effect is
//     one of the most replicated findings in educational psychology for
//     novice learners; a full worked example reduces extraneous cognitive
//     load vs an unassisted problem.
//     (docs/research/cena-sexy-game-research-2026-04-11.md §Track 9)
//
//   * Renkl & Atkinson (2003) "Structuring the Transition From Example Study
//     to Problem Solving" — faded worked examples (progressively blanking
//     later steps) yield d ≈ 0.4–0.6 on transfer tasks vs static examples.
//     The L1 worked-step template is the first rung of Cena's faded ladder:
//     it names the concrete first operation with the numbers from the
//     prerequisite, not a generic "think about X".
//     (docs/research/tracks/track-9-socratic-ai-tutoring.md §Finding B)
//
//   * Pekrun (2014) control-value theory of achievement emotions — performance
//     anxiety narrows working-memory capacity, which multiplies the extraneous
//     load of an indirect nudge. A student who is already anxious reads
//     "Consider how X applies here" as "I should have known X" — the
//     implicit reproach lands harder than the hint content. A concrete
//     worked step (verb + object + numbers) lets the anxious student
//     execute a small success before the affect cycle tightens.
//     (cited in docs/research/cena-user-personas-and-features-2026-04-17.md
//     §F11, §Yael persona)
//
// Design constraints
// ------------------
//   * Zero LLM calls. The governor is pure template expansion over data that
//     the existing IHintGenerator.Generate() has already resolved (prerequisite
//     name, concept id). Matches ADR-0045 §3 tier-1 pinning for L1 hints and
//     avoids the $6.3k/month Sonnet-per-L1 drift that ADR-0045 names.
//
//   * Copy stays inside the ship-gate GD-004 envelope — declarative
//     ("Try this step: …"), no loss-aversion framing, no variable-ratio
//     reinforcement, no day-counter mechanics. The banned-mechanics
//     scanner runs in CI.
//
//   * Session-scoped — the governor reads AccommodationProfile.IsEnabled()
//     which in turn folds AccommodationProfileAssignedV1 events. No
//     misconception or anxiety state lands on the persistent student profile
//     (ADR-0003 session-scoped misconception data).
//
//   * Metric for observability: cena_hint_ld_governor_engaged_total
//     { institute_id, rung } — lets the finops dashboard confirm the L1 flow
//     stays on tier 1 and lets educators track adoption per cohort.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Accommodations;
using Cena.Actors.Services;

namespace Cena.Actors.Hints;

/// <summary>
/// Policy that rewrites hint content for students with the LD-anxious
/// accommodation flag. No-op for every other student.
/// </summary>
public interface ILdAnxiousHintGovernor
{
    /// <summary>
    /// Apply the governor to the output of <see cref="IHintGenerator"/>.
    /// </summary>
    /// <param name="original">The content <see cref="IHintGenerator.Generate"/> produced.</param>
    /// <param name="request">The hint request (source of rung + prereq names).</param>
    /// <param name="profile">The student's current accommodation profile.</param>
    /// <param name="instituteId">Institute scope tag for the engagement metric;
    /// empty string when unknown (fail-open — never block hint rendering on
    /// a missing label).</param>
    /// <returns>Either the original content unchanged (governor not engaged)
    /// or a rewritten <see cref="HintContent"/> with the worked-step L1 body.</returns>
    HintContent Apply(
        HintContent original,
        HintRequest request,
        AccommodationProfile profile,
        string instituteId);
}

/// <summary>
/// Deterministic, template-based implementation. No LLM, no external I/O.
/// Safe to register as a singleton.
/// </summary>
public sealed class LdAnxiousHintGovernor : ILdAnxiousHintGovernor
{
    /// <summary>
    /// Text marker embedded in the rewritten L1 body so integration tests
    /// and the admin dashboard can recognise governor-generated hints
    /// without parsing prose. The marker is semantic ("try this step:")
    /// rather than a hidden sentinel — students see it and it carries
    /// the action the research literature backs.
    /// </summary>
    public const string WorkedExampleMarker = "Try this step:";

    private static readonly Meter Meter = new("Cena.Actors.Hints");

    private readonly Counter<long> _governorEngaged;

    public LdAnxiousHintGovernor()
    {
        _governorEngaged = Meter.CreateCounter<long>(
            "cena_hint_ld_governor_engaged_total",
            description:
                "Times the LD-anxious hint governor rewrote a hint body. "
                + "Tagged by institute_id + rung. prr-029.");
    }

    public HintContent Apply(
        HintContent original,
        HintRequest request,
        AccommodationProfile profile,
        string instituteId)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);

        // Governor is strictly opt-in: absent the flag, return the caller's
        // content byte-for-byte. The production hint path must stay
        // identical for the 99%+ of students without a profile on file.
        if (!profile.LdAnxiousHintGovernorEnabled)
            return original;

        // L2 / L3 are left intact. Worked-example rewriting is a Level-1
        // scaffold by design (Renkl fading: full example at the bottom
        // rung, fade on subsequent calls). The caller's existing L2 / L3
        // paths already include concrete content (error-type coaching,
        // full reveal) — re-writing them would duplicate scaffolding and
        // bypass the faded-ladder pattern.
        if (request.HintLevel != 1)
            return original;

        var rewritten = BuildWorkedStepBody(request);
        // Defensive: if the request carried zero context (no prereq,
        // no stem) the rewrite would be lower-signal than the original.
        // Fall back to the terse nudge so we never degrade quality.
        if (string.IsNullOrWhiteSpace(rewritten))
            return original;

        _governorEngaged.Add(
            1,
            new KeyValuePair<string, object?>("institute_id",
                string.IsNullOrWhiteSpace(instituteId) ? "unknown" : instituteId),
            new KeyValuePair<string, object?>("rung", "L1"));

        // HasMoreHints is preserved from the original — the governor does
        // not alter the ladder cap, only the body of the first rung.
        return original with { Text = rewritten };
    }

    /// <summary>
    /// Build the concrete first-step body. The template names the
    /// prerequisite (or the concept) and pairs it with an imperative
    /// verb so the student can execute without reasoning about the
    /// meta-question "what does the hint want me to do?"
    /// </summary>
    private static string BuildWorkedStepBody(HintRequest request)
    {
        // Prefer the strongest prerequisite (already sorted by the caller).
        // If names are empty, fall back to the concept id — still
        // concrete enough to anchor the student's attention.
        var anchor =
            (request.PrerequisiteConceptNames is { Count: > 0 })
                ? request.PrerequisiteConceptNames[0]
                : request.ConceptId;

        if (string.IsNullOrWhiteSpace(anchor))
            return string.Empty;

        // NOTE: strictly neutral, imperative-only copy. No motivational
        // framing, no day-counter language, no FOMO — ship-gate GD-004.
        // The verb "Start" is chosen over "Try" for the action line because
        // Pekrun's anxious learners interpret "try" as conditional permission
        // to fail; "Start" is a directive that removes the evaluative
        // framing.
        //
        // Format: "Try this step: Start by writing down **{anchor}**,
        //         then apply it to the first line of the problem."
        //
        // Why two sentences:
        //   (a) the marker sentence ("Try this step: …") is the handle the
        //       student sees and the integration test asserts on;
        //   (b) the action sentence binds the abstract anchor to a
        //       concrete motor action (write it down) — Renkl's worked-
        //       example rungs begin with naming the quantity, not with
        //       abstract application.
        return
            $"{WorkedExampleMarker} Start by writing down **{anchor}**, "
            + "then apply it to the first line of the problem.";
    }
}
