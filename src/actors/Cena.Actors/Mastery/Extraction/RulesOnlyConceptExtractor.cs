// =============================================================================
// Cena Platform — Rules-only Concept Extractor (ADR-0062 Phase 1, Tier 1)
//
// The cheapest possible extractor: take a hint produced by an upstream
// keyword classifier (e.g. `BagrutDraftPersistence.ClassifyTaxonomy`),
// canonicalize it through `BagrutTaxonomyCatalog`, and emit either one
// QuestionConcept (Primary) or an empty list. Never invents a SkillCode.
//
// Why a separate class instead of inlining into BagrutDraftPersistence:
//   * The same extractor runs at variant-generation time, not just at
//     draft-persist time. Encapsulating the canonicalization step here
//     means both call sites get the same closed-set discipline.
//   * Phase 1 next session swaps RulesOnly for Hybrid (rules + LLM)
//     without touching either call site.
//   * Tests can exercise the canonicalizer + extractor seam directly.
// =============================================================================

using Cena.Actors.Events;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Mastery.Extraction;

public sealed class RulesOnlyConceptExtractor : IQuestionConceptExtractor
{
    public const string StrategyId = "rules_v1";

    private readonly BagrutTaxonomyCatalog _catalog;
    private readonly ILogger<RulesOnlyConceptExtractor>? _log;

    public RulesOnlyConceptExtractor(
        BagrutTaxonomyCatalog catalog,
        ILogger<RulesOnlyConceptExtractor>? log = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _log = log;
    }

    public Task<ExtractionResult> ExtractAsync(
        ExtractionInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var hint = input.RuleTierHint;
        if (string.IsNullOrWhiteSpace(hint))
        {
            // No upstream rule tier fired. Phase 1 turn does NOT yet run
            // the LLM tier — caller falls back to "unlinked" semantics
            // and the curator picks during review.
            return Task.FromResult(new ExtractionResult(
                Concepts: Array.Empty<QuestionConcept>(),
                Strategy: StrategyId));
        }

        if (!_catalog.TryCanonicalize(hint, input.TrackHint, out var skill, out var leaf))
        {
            _log?.LogWarning(
                "RulesOnlyConceptExtractor: rule-tier hint {Hint} did not canonicalize against the closed-set catalog. "
                + "Treating as unlinked. (qid={QuestionId} track={Track})",
                hint, input.QuestionId, input.TrackHint);
            return Task.FromResult(new ExtractionResult(
                Concepts: Array.Empty<QuestionConcept>(),
                Strategy: StrategyId));
        }

        _log?.LogDebug(
            "RulesOnlyConceptExtractor: {Hint} → {Skill} (leaf={Leaf}, conf={Conf}) qid={QuestionId}",
            hint, skill.Value, leaf!.LeafId, input.RuleTierConfidence, input.QuestionId);

        var concept = new QuestionConcept(
            SkillCode: skill,
            Role: ConceptRole.Primary,
            // Confidence is what the upstream classifier said. ClassifyTaxonomy
            // uses 0.40–0.65 today; we pass that through verbatim so the
            // calibration corpus can measure precision against curator
            // overrides without a remapping layer.
            Confidence: Math.Clamp(input.RuleTierConfidence, 0.0, 1.0),
            Rationale: "",
            Tier: "rules");

        return Task.FromResult(new ExtractionResult(
            Concepts: new[] { concept },
            Strategy: StrategyId));
    }
}
