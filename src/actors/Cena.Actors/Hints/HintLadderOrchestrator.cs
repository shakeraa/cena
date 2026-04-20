// =============================================================================
// Cena Platform — Hint Ladder Orchestrator (prr-203, ADR-0045)
//
// Given the current per-(session, question) rung state + a HintLadderInput,
// produces the NEXT rung and its body. Server-authoritative: the client
// cannot skip a rung by asking for L2 first, because the orchestrator
// advances from <c>currentRung</c> (supplied by the endpoint) and always
// returns <c>currentRung + 1</c> (clamped at 3).
//
// Fallback chain (persona-sre):
//   L1 requested → always succeeds (template, no LLM).
//   L2 requested → L2HaikuHintGenerator; on null (PII refused, LLM error,
//                  empty content) → fall back to L1 static template with
//                  RungSource = "template-fallback" so the UI + admin
//                  dashboard can see the degradation in the response DTO
//                  without changing status code.
//   L3 requested → L3WorkedExampleHintGenerator; on null (Socratic cap
//                  exhausted, PII refused, LLM error, empty content) →
//                  fall back to L1 static template with RungSource =
//                  "template-fallback".
//
// The orchestrator itself carries NO [TaskRouting] attribute because it
// is a pure dispatch + policy layer — the actual LLM call sites live in
// L2HaikuHintGenerator and L3WorkedExampleHintGenerator (each with its
// own [TaskRouting] + [FeatureTag]). The architecture-ratchet test
// HintLadderEndpointUsesLadderTest asserts the orchestrator is the only
// path the endpoint uses, and that the endpoint is NOT talking directly
// to the old inline-hint generator for the new route.
// =============================================================================

using Cena.Actors.Accommodations;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Hints;

/// <summary>
/// Advances the per-(session, question) hint-ladder rung and produces the
/// rung body. Consumed by the HintLadderEndpoint.
/// </summary>
public interface IHintLadderOrchestrator
{
    /// <summary>
    /// Produce the next rung's body given the current rung state. Safe to
    /// call repeatedly — each call advances by one rung up to the max (3).
    /// </summary>
    Task<HintLadderOutput> AdvanceAsync(
        HintLadderInput input,
        int currentRung,
        AccommodationProfile? accommodationProfile,
        CancellationToken ct);
}

/// <summary>
/// Pure data carrier — fed by the endpoint, consumed by the tier-specific
/// generators. Centralises the fields each rung needs to avoid per-rung
/// parameter drift.
/// </summary>
public sealed record HintLadderInput(
    string SessionId,
    string QuestionId,
    string ConceptId,
    string? Subject,
    string? QuestionStem,
    string? Explanation,
    string? Methodology,
    IReadOnlyList<string> PrerequisiteConceptNames,
    string? InstituteId);

/// <summary>
/// Orchestrator output. The endpoint maps this into
/// <c>HintLadderResponseDto</c>.
/// </summary>
public sealed record HintLadderOutput(
    int Rung,
    string Body,
    string RungSource,
    int MaxRungReached,
    bool NextRungAvailable);

public sealed class HintLadderOrchestrator : IHintLadderOrchestrator
{
    /// <summary>Highest rung the ladder can reach (L3).</summary>
    public const int MaxRung = 3;

    private readonly IL1TemplateHintGenerator _l1;
    private readonly IL2HaikuHintGenerator _l2;
    private readonly IL3WorkedExampleHintGenerator _l3;
    private readonly ILogger<HintLadderOrchestrator> _logger;

    public HintLadderOrchestrator(
        IL1TemplateHintGenerator l1,
        IL2HaikuHintGenerator l2,
        IL3WorkedExampleHintGenerator l3,
        ILogger<HintLadderOrchestrator> logger)
    {
        _l1 = l1;
        _l2 = l2;
        _l3 = l3;
        _logger = logger;
    }

    public async Task<HintLadderOutput> AdvanceAsync(
        HintLadderInput input,
        int currentRung,
        AccommodationProfile? accommodationProfile,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (currentRung < 0 || currentRung > MaxRung)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRung),
                $"currentRung must be in [0, {MaxRung}]; got {currentRung}.");
        }

        // Clamp the target rung at MaxRung so repeated calls after L3 keep
        // returning L3 (no 4th rung exists). The client still gets the same
        // body deterministically; the endpoint surfaces NextRungAvailable =
        // false so the UI can retire the "next hint" affordance.
        var targetRung = Math.Min(currentRung + 1, MaxRung);
        var instituteId = input.InstituteId ?? string.Empty;

        return targetRung switch
        {
            1 => RenderL1(input, accommodationProfile, instituteId),
            2 => await RenderL2OrFallbackAsync(input, accommodationProfile, instituteId, ct)
                    .ConfigureAwait(false),
            3 => await RenderL3OrFallbackAsync(input, accommodationProfile, instituteId, ct)
                    .ConfigureAwait(false),
            // currentRung was already MaxRung and we clamped — replay L3.
            _ => await RenderL3OrFallbackAsync(input, accommodationProfile, instituteId, ct)
                    .ConfigureAwait(false),
        };
    }

    private HintLadderOutput RenderL1(
        HintLadderInput input,
        AccommodationProfile? profile,
        string instituteId)
    {
        var l1 = _l1.Generate(input, profile, instituteId);
        return new HintLadderOutput(
            Rung: 1,
            Body: l1.Body,
            RungSource: l1.RungSource,
            MaxRungReached: 1,
            NextRungAvailable: true);
    }

    private async Task<HintLadderOutput> RenderL2OrFallbackAsync(
        HintLadderInput input,
        AccommodationProfile? profile,
        string instituteId,
        CancellationToken ct)
    {
        var l2 = await _l2.GenerateAsync(input, ct).ConfigureAwait(false);
        if (l2 is not null)
        {
            return new HintLadderOutput(
                Rung: 2,
                Body: l2.Body,
                RungSource: l2.RungSource,
                MaxRungReached: 2,
                NextRungAvailable: true);
        }

        // Fallback to L1 static template. The rung-state stays at 2 logically
        // (the endpoint has already incremented), so the next call still
        // advances toward L3; we just could not deliver a real L2 this time.
        // RungSource = "template-fallback" so observability can see the
        // degradation without losing the rung context.
        _logger.LogInformation(
            "L2 hint degraded to static template for question {QuestionId} "
            + "(L2 generator returned null).",
            input.QuestionId);
        var l1 = _l1.Generate(input, profile, instituteId);
        return new HintLadderOutput(
            Rung: 2,
            Body: l1.Body,
            RungSource: "template-fallback",
            MaxRungReached: 2,
            NextRungAvailable: true);
    }

    private async Task<HintLadderOutput> RenderL3OrFallbackAsync(
        HintLadderInput input,
        AccommodationProfile? profile,
        string instituteId,
        CancellationToken ct)
    {
        var l3 = await _l3.GenerateAsync(input, ct).ConfigureAwait(false);
        if (l3 is not null)
        {
            return new HintLadderOutput(
                Rung: 3,
                Body: l3.Body,
                RungSource: l3.RungSource,
                MaxRungReached: 3,
                NextRungAvailable: false);
        }

        _logger.LogInformation(
            "L3 hint degraded to static template for question {QuestionId} "
            + "(L3 generator returned null; cap exhausted or LLM unavailable).",
            input.QuestionId);
        var l1 = _l1.Generate(input, profile, instituteId);
        return new HintLadderOutput(
            Rung: 3,
            Body: l1.Body,
            RungSource: "template-fallback",
            MaxRungReached: 3,
            NextRungAvailable: false);
    }
}
