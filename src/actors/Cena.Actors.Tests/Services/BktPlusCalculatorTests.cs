// =============================================================================
// Cena Platform — BKT+ Calculator Tests (BKT-PLUS-001)
// =============================================================================

using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

public sealed class BktPlusCalculatorTests
{
    private readonly BktPlusCalculator _calc = new(new BktService());
    private static readonly BktParameters DefaultParams = BktParameters.Default;
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 13, 10, 0, 0, TimeSpan.Zero);

    private static SkillMasteryState MakeState(
        double pLearned = 0.5,
        double halfLife = 14.0,
        int daysSinceLastPractice = 0) => new(
        SkillId: "test-skill",
        PLearned: pLearned,
        LastPracticedAt: BaseTime.AddDays(-daysSinceLastPractice),
        HalfLifeDays: halfLife,
        TotalAttempts: 10,
        CorrectAttempts: 5
    );

    // ── Forgetting curve ──

    [Fact]
    public void EffectiveMastery_NoDecay_WhenJustPracticed()
    {
        var state = MakeState(pLearned: 0.80, daysSinceLastPractice: 0);
        var effective = _calc.ComputeEffectiveMastery(state, BaseTime);
        Assert.Equal(0.80, effective, precision: 4);
    }

    [Fact]
    public void EffectiveMastery_HalvesAtHalfLife()
    {
        var state = MakeState(pLearned: 0.80, halfLife: 14.0, daysSinceLastPractice: 14);
        var effective = _calc.ComputeEffectiveMastery(state, BaseTime);
        Assert.Equal(0.40, effective, precision: 4);
    }

    [Fact]
    public void EffectiveMastery_QuartersAtTwoHalfLives()
    {
        var state = MakeState(pLearned: 0.80, halfLife: 14.0, daysSinceLastPractice: 28);
        var effective = _calc.ComputeEffectiveMastery(state, BaseTime);
        Assert.Equal(0.20, effective, precision: 4);
    }

    // ── Assistance weighting ──

    [Fact]
    public void SoloCorrect_GivesMoreCredit_ThanHintedCorrect()
    {
        var state = MakeState(pLearned: 0.3, daysSinceLastPractice: 0);

        var soloResult = _calc.Update(new BktPlusInput(
            state, IsCorrect: true, Assistance: AssistanceLevel.Solo, BaseTime, DefaultParams));

        var hintedResult = _calc.Update(new BktPlusInput(
            state, IsCorrect: true, Assistance: AssistanceLevel.TwoHints, BaseTime, DefaultParams));

        Assert.True(soloResult.PosteriorLearned > hintedResult.PosteriorLearned,
            "Solo correct should produce higher mastery than hint-assisted correct");
    }

    [Fact]
    public void AutoFilled_GivesMinimalCredit()
    {
        var state = MakeState(pLearned: 0.3, daysSinceLastPractice: 0);

        var autoResult = _calc.Update(new BktPlusInput(
            state, IsCorrect: true, Assistance: AssistanceLevel.AutoFilled, BaseTime, DefaultParams));

        // Auto-filled should give only 25% of normal learning rate
        Assert.True(autoResult.PosteriorLearned < 0.40,
            "Auto-filled should barely increase mastery");
    }

    // ── Half-life adjustment ──

    [Fact]
    public void Correct_IncreasesHalfLife()
    {
        var state = MakeState(pLearned: 0.5, halfLife: 14.0, daysSinceLastPractice: 0);
        var result = _calc.Update(new BktPlusInput(
            state, IsCorrect: true, Assistance: AssistanceLevel.Solo, BaseTime, DefaultParams));
        Assert.True(result.NewHalfLifeDays > 14.0);
    }

    [Fact]
    public void Incorrect_DecreasesHalfLife()
    {
        var state = MakeState(pLearned: 0.5, halfLife: 14.0, daysSinceLastPractice: 0);
        var result = _calc.Update(new BktPlusInput(
            state, IsCorrect: false, Assistance: AssistanceLevel.Solo, BaseTime, DefaultParams));
        Assert.True(result.NewHalfLifeDays < 14.0);
    }

    // ── Refresh detection ──

    [Fact]
    public void NeedsRefresh_WhenMasteredButDecayed()
    {
        // Was mastered (0.85), decayed below refresh threshold (0.40)
        var state = MakeState(pLearned: 0.85, halfLife: 14.0, daysSinceLastPractice: 15);
        var effective = _calc.ComputeEffectiveMastery(state, BaseTime);
        Assert.True(effective < SkillMasteryState.RefreshThreshold);

        var result = _calc.Update(new BktPlusInput(
            state, IsCorrect: true, Assistance: AssistanceLevel.Solo, BaseTime, DefaultParams));
        Assert.True(result.NeedsRefresh);
    }

    // ── Prerequisite gating ──

    [Fact]
    public void Prerequisites_Met_WhenAllAboveThreshold()
    {
        var graphJson = """
        {
          "skills": {
            "algebra-basics": { "prerequisites": [] },
            "algebra-linear": { "prerequisites": ["algebra-basics"] }
          }
        }
        """;
        var graph = SkillPrerequisiteGraph.LoadFromJson(graphJson);
        var masteryMap = new Dictionary<string, SkillMasteryState>
        {
            ["algebra-basics"] = MakeState(pLearned: 0.70, daysSinceLastPractice: 0)
        };

        var met = _calc.AllPrerequisitesMet("algebra-linear", masteryMap, graph, BaseTime);
        Assert.True(met);
    }

    [Fact]
    public void Prerequisites_NotMet_WhenBelowThreshold()
    {
        var graphJson = """
        {
          "skills": {
            "algebra-basics": { "prerequisites": [] },
            "algebra-linear": { "prerequisites": ["algebra-basics"] }
          }
        }
        """;
        var graph = SkillPrerequisiteGraph.LoadFromJson(graphJson);
        var masteryMap = new Dictionary<string, SkillMasteryState>
        {
            ["algebra-basics"] = MakeState(pLearned: 0.30, daysSinceLastPractice: 0)
        };

        var met = _calc.AllPrerequisitesMet("algebra-linear", masteryMap, graph, BaseTime);
        Assert.False(met);
    }

    [Fact]
    public void Prerequisites_NotMet_WhenDecayedBelowThreshold()
    {
        var graphJson = """
        {
          "skills": {
            "algebra-basics": { "prerequisites": [] },
            "algebra-linear": { "prerequisites": ["algebra-basics"] }
          }
        }
        """;
        var graph = SkillPrerequisiteGraph.LoadFromJson(graphJson);
        // Was 0.80 but practiced 20 days ago — effective ~0.30
        var masteryMap = new Dictionary<string, SkillMasteryState>
        {
            ["algebra-basics"] = MakeState(pLearned: 0.80, halfLife: 14.0, daysSinceLastPractice: 20)
        };

        var met = _calc.AllPrerequisitesMet("algebra-linear", masteryMap, graph, BaseTime);
        Assert.False(met, "Decayed prerequisite should not meet gate");
    }

    // ── Graph validation ──

    [Fact]
    public void Graph_Rejects_Cycles()
    {
        var cyclicJson = """
        {
          "skills": {
            "a": { "prerequisites": ["b"] },
            "b": { "prerequisites": ["a"] }
          }
        }
        """;
        Assert.Throws<InvalidOperationException>(() => SkillPrerequisiteGraph.LoadFromJson(cyclicJson));
    }

    [Fact]
    public void Graph_Rejects_MissingPrerequisite()
    {
        var missingJson = """
        {
          "skills": {
            "a": { "prerequisites": ["nonexistent"] }
          }
        }
        """;
        Assert.Throws<InvalidOperationException>(() => SkillPrerequisiteGraph.LoadFromJson(missingJson));
    }

    [Fact]
    public void Graph_LoadsRealTrack()
    {
        // Verify the 806 track JSON can be loaded and passes validation
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Cena.Actors", "Services", "Prerequisites", "SkillPrerequisites-806.json"));
        var graph = SkillPrerequisiteGraph.LoadFromJson(json);
        Assert.True(graph.AllSkills.Count >= 20);
    }

    // ── PP-010: Category-specific half-lives ──

    [Theory]
    [InlineData(SkillCategory.Procedural, 7.0)]
    [InlineData(SkillCategory.Conceptual, 21.0)]
    [InlineData(SkillCategory.MetaCognitive, 30.0)]
    [InlineData(SkillCategory.Mixed, 14.0)]
    public void DefaultHalfLife_MatchesCategory(SkillCategory cat, double expectedDays)
    {
        Assert.Equal(expectedDays, SkillMasteryState.DefaultHalfLifeForCategory(cat));
    }

    [Fact]
    public void ProceduralSkill_DecaysTo50Percent_In7Days()
    {
        var state = MakeState(pLearned: 1.0, halfLife: 7.0, daysSinceLastPractice: 7);
        var effective = _calc.ComputeEffectiveMastery(state, BaseTime);
        Assert.Equal(0.50, effective, precision: 4);
    }

    [Fact]
    public void ConceptualSkill_DecaysTo50Percent_In21Days()
    {
        var state = MakeState(pLearned: 1.0, halfLife: 21.0, daysSinceLastPractice: 21);
        var effective = _calc.ComputeEffectiveMastery(state, BaseTime);
        Assert.Equal(0.50, effective, precision: 4);
    }

    [Fact]
    public void Graph_ParsesCategories()
    {
        var json = """
        {
          "skills": {
            "factoring": { "prerequisites": [], "category": "procedural" },
            "functions": { "prerequisites": [], "category": "conceptual" },
            "strategy":  { "prerequisites": [], "category": "metacognitive" },
            "other":     { "prerequisites": [] }
          }
        }
        """;
        var graph = SkillPrerequisiteGraph.LoadFromJson(json);
        Assert.Equal(SkillCategory.Procedural, graph.GetCategory("factoring"));
        Assert.Equal(SkillCategory.Conceptual, graph.GetCategory("functions"));
        Assert.Equal(SkillCategory.MetaCognitive, graph.GetCategory("strategy"));
        Assert.Equal(SkillCategory.Mixed, graph.GetCategory("other"));
        Assert.Equal(SkillCategory.Mixed, graph.GetCategory("nonexistent"));
    }

    [Fact]
    public void RealTrack806_HasCategories()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Cena.Actors", "Services", "Prerequisites", "SkillPrerequisites-806.json"));
        var graph = SkillPrerequisiteGraph.LoadFromJson(json);

        Assert.Equal(SkillCategory.Procedural, graph.GetCategory("algebra-linear-equations"));
        Assert.Equal(SkillCategory.Conceptual, graph.GetCategory("algebra-basics"));
        Assert.Equal(SkillCategory.MetaCognitive, graph.GetCategory("calculus-derivatives-applications"));
    }
}
