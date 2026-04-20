// =============================================================================
// Cena Platform — L1 Template Hint Generator (prr-203, ADR-0045 §3)
//
// The first rung on the hint ladder. Strictly NO LLM: L1 is a deterministic
// template expansion over already-resolved data (prerequisite name / concept
// id / subject). ADR-0045 §3 pins L1 to tier 1 because Dr. Kenji's 2026-04-20
// estimate shows naive "all hints are Sonnet" burns ~$6.3k/month at product
// volume. Template-only is provably sufficient for "re-read this step".
//
// Architecture rule (enforced by HintLadderEndpointUsesLadderTest + the ship-
// gate llm-routing-scanner): this file MUST NOT import Anthropic.SDK,
// ITutorLlmService, ILlmClient, or any other LLM seam. The class has no
// [TaskRouting] attribute precisely because it has no LLM call to route.
//
// Composition:
//   1. Delegates the underlying template text to IStaticHintLadderFallback
//      (the same component that backs the Socratic-cap fallback in prr-012),
//      reused so the L1 copy stays consistent whether it's reached through
//      the ladder or the cap-exhaustion fallback.
//   2. Wraps the result in ILdAnxiousHintGovernor (prr-029) when the student
//      has the LdAnxiousFriendly accommodation flag — the governor rewrites
//      the L1 body into a concrete worked-step example (Renkl/Sweller
//      worked-example effect, docs/research/cena-sexy-game-research-2026-04-11.md).
//      Governor failure is fail-open — raw template is returned unchanged.
//
// WHY a separate file for L1 instead of inlining into HintLadderOrchestrator:
//   The tier-1 no-LLM invariant is a compliance-shaped seam (ADR-0045 §3 +
//   ADR-0026 §silent-default). Concentrating the tier-1 path in its own file
//   makes the "no imports from LLM seams" ratchet trivial to assert — both
//   the shipgate scanner and HintLadderEndpointUsesLadderTest grep the file
//   rather than traverse the whole orchestrator.
// =============================================================================

using Cena.Actors.Accommodations;
using Cena.Actors.Services;
using Cena.Actors.Tutor;

namespace Cena.Actors.Hints;

/// <summary>
/// Deterministic tier-1 hint generator. No LLM calls. Pure template
/// expansion with optional prr-029 LD-anxious governor rewrite.
/// </summary>
public interface IL1TemplateHintGenerator
{
    /// <summary>
    /// Produce the L1 hint body for the given request + (optional)
    /// accommodation profile. Returns a source-tagged payload so the
    /// orchestrator can surface the correct <c>rungSource</c> on the
    /// response DTO — "template" for the raw fallback copy, or
    /// "template" after governor rewrite (the governor does not change
    /// the rung source; it is still tier-1 no-LLM).
    /// </summary>
    L1HintPayload Generate(
        HintLadderInput input,
        AccommodationProfile? accommodationProfile,
        string instituteId);
}

/// <summary>
/// The resolved L1 rung payload. Always carries <c>RungSource = "template"</c>
/// because tier-1 is template-only by construction.
/// </summary>
public sealed record L1HintPayload(string Body)
{
    /// <summary>Fixed to "template" — tier-1 invariant.</summary>
    public string RungSource => "template";
}

/// <summary>
/// Production implementation. Singleton-safe — no mutable state.
/// </summary>
public sealed class L1TemplateHintGenerator : IL1TemplateHintGenerator
{
    private readonly IStaticHintLadderFallback _staticLadder;
    private readonly ILdAnxiousHintGovernor _ldAnxiousGovernor;

    public L1TemplateHintGenerator(
        IStaticHintLadderFallback staticLadder,
        ILdAnxiousHintGovernor ldAnxiousGovernor)
    {
        ArgumentNullException.ThrowIfNull(staticLadder);
        ArgumentNullException.ThrowIfNull(ldAnxiousGovernor);
        _staticLadder = staticLadder;
        _ldAnxiousGovernor = ldAnxiousGovernor;
    }

    public L1HintPayload Generate(
        HintLadderInput input,
        AccommodationProfile? accommodationProfile,
        string instituteId)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Build a TutorContext carrier just to reuse StaticHintLadderFallback;
        // the fallback only reads Subject off the context, so supplying the
        // student / thread identifiers as empty strings is safe and avoids
        // surfacing them in the L1 path (which has no identity awareness).
        var tutorContext = new TutorContext(
            StudentId: string.Empty,
            ThreadId: string.Empty,
            MessageHistory: Array.Empty<TutorMessage>(),
            Subject: input.Subject,
            CurrentGrade: null,
            InstituteId: instituteId);

        // fallbackIndex=0 → L1 rung copy per StaticHintLadderFallback's
        // enum mapping (StaticHintRung.L1_TryThisStep).
        var fallback = _staticLadder.GetHint(tutorContext, fallbackIndex: 0);
        var rawL1 = new HintContent(Text: fallback.Text, HasMoreHints: true);

        // No profile on file → raw template.
        if (accommodationProfile is null)
            return new L1HintPayload(rawL1.Text);

        // prr-029: optionally rewrite the L1 body into a worked-step
        // example for students with the LdAnxiousFriendly flag. Governor
        // failure is fail-open — we return the raw template so hint
        // rendering is never blocked on governor errors.
        try
        {
            var hintRequest = new HintRequest(
                HintLevel: 1,
                QuestionId: input.QuestionId,
                ConceptId: input.ConceptId,
                PrerequisiteConceptNames: input.PrerequisiteConceptNames,
                Options: Array.Empty<Questions.QuestionOptionState>(),
                Explanation: null,
                StudentAnswer: null,
                Prerequisites: null,
                ConceptState: null);

            var governed = _ldAnxiousGovernor.Apply(
                rawL1, hintRequest, accommodationProfile, instituteId);
            return new L1HintPayload(governed.Text);
        }
        catch
        {
            // Fail-open: never block the student on a governor error.
            return new L1HintPayload(rawL1.Text);
        }
    }
}
