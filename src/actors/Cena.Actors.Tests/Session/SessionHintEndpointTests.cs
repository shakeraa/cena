// =============================================================================
// Cena Platform — Session Hint Endpoint Helper Tests (FIND-pedagogy-006)
//
// The Vue session REST flow used to bypass ScaffoldingService entirely,
// leaving novice students with zero worked examples and zero hints.
// The fix wires the same IHintGenerator the actor-side session uses into
// the new POST /api/sessions/{id}/question/{qid}/hint endpoint AND
// surfaces scaffolding metadata on GET /api/sessions/{id}/current-question.
//
// This suite exercises the internal `BuildHintOptionStates` helper exposed
// via InternalsVisibleTo, and asserts the round-trip with HintGenerator
// produces real (not fake) hint text at each level of the ladder.
//
// Citations for the pedagogy under test:
//   - Sweller, van Merriënboer & Paas (1998). Cognitive Architecture and
//     Instructional Design. Educational Psychology Review, 10(3), 251-296.
//     DOI: 10.1023/A:1022193728205 (worked example effect)
//   - Renkl & Atkinson (2003). Structuring the Transition From Example
//     Study to Problem Solving. Educational Psychologist, 38(1), 15-22.
//     DOI: 10.1207/S15326985EP3801_3 (faded examples)
//   - Kalyuga, Ayres, Chandler & Sweller (2003). The Expertise Reversal
//     Effect. Educational Psychologist, 38(1), 23-31.
//     DOI: 10.1207/S15326985EP3801_4 (fade scaffolds for experts)
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Session;

public sealed class SessionHintEndpointTests
{
    private const string ConceptId = "concept:physics:ohms-law";
    private const string QuestionId = "q_ohm_101";

    private static QuestionDocument BuildQuestion(
        Dictionary<string, string>? rationales = null,
        string? explanation = null,
        string[]? choices = null,
        string correct = "6 ohms")
    {
        return new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            Subject = "Physics",
            Prompt = "A 12V battery drives 2A through a resistor. What is R?",
            QuestionType = "multiple-choice",
            Choices = choices ?? new[] { "3 ohms", "6 ohms", "12 ohms", "24 ohms" },
            CorrectAnswer = correct,
            Explanation = explanation,
            DistractorRationales = rationales,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // BuildHintOptionStates — projecting QuestionDocument onto the shape
    // HintGenerator expects.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildHintOptionStates_MarksCorrectOption()
    {
        var q = BuildQuestion();
        var states = SessionEndpoints.BuildHintOptionStates(q);

        Assert.Equal(4, states.Count);
        var correct = Assert.Single(states.Where(s => s.IsCorrect));
        Assert.Equal("6 ohms", correct.Label);
    }

    [Fact]
    public void BuildHintOptionStates_CarriesDistractorRationalesByExactMatch()
    {
        var q = BuildQuestion(new Dictionary<string, string>
        {
            ["3 ohms"] = "That would give 4A by Ohm's law, not 2A.",
            ["12 ohms"] = "That would give 1A.",
        });

        var states = SessionEndpoints.BuildHintOptionStates(q);

        var three = states.Single(s => s.Label == "3 ohms");
        Assert.NotNull(three.DistractorRationale);
        Assert.Contains("Ohm's law", three.DistractorRationale);
    }

    [Fact]
    public void BuildHintOptionStates_FallsBackToCaseInsensitiveRationaleLookup()
    {
        var q = BuildQuestion(new Dictionary<string, string>
        {
            ["3 OHMS"] = "Case-insensitive match required.",
        });

        var states = SessionEndpoints.BuildHintOptionStates(q);
        var three = states.Single(s => s.Label == "3 ohms");
        Assert.NotNull(three.DistractorRationale);
        Assert.Contains("Case-insensitive", three.DistractorRationale);
    }

    [Fact]
    public void BuildHintOptionStates_ReturnsEmptyForFreeTextQuestions()
    {
        var q = BuildQuestion(choices: Array.Empty<string>());
        var states = SessionEndpoints.BuildHintOptionStates(q);
        Assert.Empty(states);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Integration with IHintGenerator — the real service path the REST
    // endpoint uses. No mocks for HintGenerator: it is a pure function.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void HintGenerator_Level1_ProducesReadableNudgeWithoutPrereqGraph()
    {
        // The REST path does not pass prerequisite names (that comes from
        // the graph cache on the actor side). Level 1 must gracefully
        // fall back to a generic re-read prompt instead of throwing or
        // returning an empty string.
        var q = BuildQuestion();
        var gen = new HintGenerator();
        var content = gen.Generate(new HintRequest(
            HintLevel: 1,
            QuestionId: QuestionId,
            ConceptId: ConceptId,
            PrerequisiteConceptNames: Array.Empty<string>(),
            Options: SessionEndpoints.BuildHintOptionStates(q),
            Explanation: q.Explanation,
            StudentAnswer: null));

        Assert.False(string.IsNullOrWhiteSpace(content.Text));
        Assert.True(content.HasMoreHints);
    }

    [Fact]
    public void HintGenerator_Level2_UsesDistractorRationaleForEliminationWhenAvailable()
    {
        var q = BuildQuestion(new Dictionary<string, string>
        {
            ["3 ohms"] = "That would give 4A by Ohm's law, not 2A.",
        });
        var gen = new HintGenerator();
        var content = gen.Generate(new HintRequest(
            HintLevel: 2,
            QuestionId: QuestionId,
            ConceptId: ConceptId,
            PrerequisiteConceptNames: Array.Empty<string>(),
            Options: SessionEndpoints.BuildHintOptionStates(q),
            Explanation: q.Explanation,
            StudentAnswer: null));

        Assert.Contains("3 ohms", content.Text);
        Assert.Contains("Ohm's law", content.Text);
    }

    [Fact]
    public void HintGenerator_Level3_RevealsAuthoredExplanationWhenPresent()
    {
        var q = BuildQuestion(
            explanation: "Ohm's law: R = V / I = 12 / 2 = 6 ohms.");
        var gen = new HintGenerator();
        var content = gen.Generate(new HintRequest(
            HintLevel: 3,
            QuestionId: QuestionId,
            ConceptId: ConceptId,
            PrerequisiteConceptNames: Array.Empty<string>(),
            Options: SessionEndpoints.BuildHintOptionStates(q),
            Explanation: q.Explanation,
            StudentAnswer: null));

        Assert.Contains("R = V / I", content.Text);
        Assert.False(content.HasMoreHints);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Scaffolding thresholds — regression ensuring the REST path's
    // "effectiveMastery + PSI=1.0" shortcut still produces sensible levels.
    // Specifically: a novice (mastery=0.1) must get Full or Partial, never
    // None. An expert (mastery=0.85) must get None.
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.10f, ScaffoldingLevel.Partial)]  // PSI=1.0 demotes Full → Partial — still scaffolded
    [InlineData(0.19f, ScaffoldingLevel.Partial)]
    [InlineData(0.35f, ScaffoldingLevel.Partial)]
    [InlineData(0.50f, ScaffoldingLevel.HintsOnly)]
    [InlineData(0.85f, ScaffoldingLevel.None)]
    public void RestPath_ScaffoldingLevelMatchesMastery_UsingFullPsi(
        float mastery, ScaffoldingLevel expected)
    {
        var level = ScaffoldingService.DetermineLevel(mastery, 1.0f);
        Assert.Equal(expected, level);
    }

    [Fact]
    public void RestPath_MasteryLessThanTwentyPercent_StillGetsHintBudget()
    {
        // Critical pedagogy assertion: the Vue-path REST student at very low
        // mastery MUST receive a hint budget. The previous code gave them
        // zero. With PSI=1.0 the Full branch is unreachable, but Partial
        // still carries 2 hints — the student is not starved.
        var level = ScaffoldingService.DetermineLevel(0.10f, 1.0f);
        var meta = ScaffoldingService.GetScaffoldingMetadata(level);
        Assert.True(meta.ShowHintButton);
        Assert.True(meta.MaxHints >= 1);
    }
}
