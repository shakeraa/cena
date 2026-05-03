// =============================================================================
// Cena Platform — RulesOnlyConceptExtractor tests (ADR-0062 Phase 1)
//
// Locks the Tier-1 extractor's contract:
//   - Empty input → empty extraction (caller falls back to "unlinked").
//   - Valid rule hint → exactly ONE QuestionConcept with role=Primary,
//     SkillCode canonicalized via the catalog, Tier="rules".
//   - Unmappable rule hint → empty extraction (closed-set discipline;
//     never invents a SkillCode per ADR-0062 §Risks).
//   - Track hint propagates through the catalog so 4u vs 5u resolves
//     to the right LeafEntry.
//   - Confidence is clamped to [0..1] regardless of upstream mistakes.
//   - Strategy id is the versioned constant the calibration corpus
//     reads to roll back a bad extractor pass.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Extraction;

namespace Cena.Actors.Tests.Mastery;

public sealed class RulesOnlyConceptExtractorTests
{
    private const string SyntheticTaxonomyJson = """
    {
      "version": "test",
      "tracks": {
        "math_5u": {
          "name": "5u",
          "topics": {
            "calculus": {
              "name": "Calculus",
              "subtopics": {
                "derivative_rules": { "conceptId": "CAL-003", "bloom_range": [3,5] },
                "integrals_intro":  { "conceptId": "CAL-005", "bloom_range": [3,5] }
              }
            }
          }
        },
        "math_4u": {
          "name": "4u",
          "topics": {
            "calculus": {
              "name": "Calculus",
              "subtopics": {
                "derivative_rules": { "conceptId": "CAL-003", "bloom_range": [3,5] }
              }
            }
          }
        }
      }
    }
    """;

    private static RulesOnlyConceptExtractor MakeExtractor()
        => new RulesOnlyConceptExtractor(BagrutTaxonomyCatalog.Parse(SyntheticTaxonomyJson));

    [Fact]
    public async Task ExtractAsync_NoRuleHint_ReturnsEmpty()
    {
        var ex = MakeExtractor();
        var result = await ex.ExtractAsync(new ExtractionInput(
            QuestionId: "q-1", Prompt: "irrelevant", Latex: null));

        Assert.Empty(result.Concepts);
        Assert.Equal("rules_v1", result.Strategy);
    }

    [Fact]
    public async Task ExtractAsync_ValidRuleHint_ReturnsOnePrimaryConcept()
    {
        var ex = MakeExtractor();
        var result = await ex.ExtractAsync(new ExtractionInput(
            QuestionId:         "q-1",
            Prompt:             "find derivative",
            Latex:              null,
            RuleTierHint:       "calculus.derivative_rules",
            RuleTierConfidence: 0.65));

        var concept = Assert.Single(result.Concepts);
        Assert.Equal("math.calculus.derivative-rules", concept.SkillCode.Value);
        Assert.Equal(ConceptRole.Primary, concept.Role);
        Assert.Equal(0.65, concept.Confidence, 3);
        Assert.Equal("rules", concept.Tier);
        Assert.Equal("rules_v1", result.Strategy);
    }

    [Fact]
    public async Task ExtractAsync_RuleHintWithConceptIdForm_Resolves()
    {
        var ex = MakeExtractor();
        var result = await ex.ExtractAsync(new ExtractionInput(
            QuestionId: "q-1", Prompt: null, Latex: null,
            RuleTierHint: "CAL-005", RuleTierConfidence: 0.5));

        var concept = Assert.Single(result.Concepts);
        Assert.Equal("math.calculus.integrals-intro", concept.SkillCode.Value);
    }

    [Fact]
    public async Task ExtractAsync_UnmappableRuleHint_ReturnsEmpty_NeverInvents()
    {
        // Closed-set discipline: a free-form skill the catalog doesn't
        // know about must NOT silently produce a new SkillCode (would
        // fork the catalog and pollute mastery posteriors per ADR-0062
        // §Risks).
        var ex = MakeExtractor();
        var result = await ex.ExtractAsync(new ExtractionInput(
            QuestionId: "q-1", Prompt: null, Latex: null,
            RuleTierHint: "calculus.unicorn_studies", RuleTierConfidence: 0.9));

        Assert.Empty(result.Concepts);
        Assert.Equal("rules_v1", result.Strategy);
    }

    [Fact]
    public async Task ExtractAsync_TrackHint_DisambiguatesAcrossTracks()
    {
        // Same SkillCode in 4u and 5u; track hint picks the right
        // LeafEntry. The SkillCode is intentionally identical (ADR-0050)
        // — the test is that the catalog accepts the hint and doesn't
        // refuse the lookup.
        var ex = MakeExtractor();

        var r4 = await ex.ExtractAsync(new ExtractionInput(
            QuestionId: "q-1", Prompt: null, Latex: null,
            TrackHint: "math_4u",
            RuleTierHint: "calculus.derivative_rules", RuleTierConfidence: 0.6));
        Assert.Single(r4.Concepts);

        var r5 = await ex.ExtractAsync(new ExtractionInput(
            QuestionId: "q-1", Prompt: null, Latex: null,
            TrackHint: "math_5u",
            RuleTierHint: "calculus.derivative_rules", RuleTierConfidence: 0.6));
        Assert.Single(r5.Concepts);

        // Both tracks land on the same SkillCode (track collapses at
        // SkillCode level per ADR-0050).
        Assert.Equal(r4.Concepts[0].SkillCode.Value, r5.Concepts[0].SkillCode.Value);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]    // negative clamps to 0
    [InlineData( 1.5, 1.0)]    // >1 clamps to 1
    [InlineData( 0.42, 0.42)]  // mid-range passes through
    public async Task ExtractAsync_Confidence_ClampedTo01(double input, double expected)
    {
        var ex = MakeExtractor();
        var result = await ex.ExtractAsync(new ExtractionInput(
            QuestionId: "q-1", Prompt: null, Latex: null,
            RuleTierHint: "CAL-003", RuleTierConfidence: input));

        Assert.Equal(expected, result.Concepts[0].Confidence, 3);
    }

    [Fact]
    public async Task ExtractAsync_NullInput_Throws()
    {
        var ex = MakeExtractor();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ex.ExtractAsync(null!));
    }
}
