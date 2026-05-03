// =============================================================================
// MST-013 Tests: KST diagnostic engine
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class DiagnosticEngineTests
{
    [Fact]
    public void SelectNextConcept_PicksMostInformative()
    {
        // 3-concept linear chain: A -> B -> C
        // Feasible states: {}, {A}, {A,B}, {A,B,C}
        var states = new List<KnowledgeState>
        {
            new(ImmutableHashSet<string>.Empty),
            new(ImmutableHashSet.Create("A")),
            new(ImmutableHashSet.Create("A", "B")),
            new(ImmutableHashSet.Create("A", "B", "C"))
        };
        var posterior = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };

        var concept = DiagnosticEngine.SelectNextConcept(states, posterior);

        // B appears in 2 of 4 states -> P(B) = 0.50 -> maximum information
        Assert.Equal("B", concept);
    }

    [Fact]
    public void UpdatePosterior_CorrectAnswer_IncreasesStatesWithConcept()
    {
        var states = new List<KnowledgeState>
        {
            new(ImmutableHashSet<string>.Empty),
            new(ImmutableHashSet.Create("A")),
            new(ImmutableHashSet.Create("A", "B")),
            new(ImmutableHashSet.Create("A", "B", "C"))
        };
        var prior = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };

        var posterior = DiagnosticEngine.UpdatePosterior(prior, states, "A", isCorrect: true);

        Assert.True(posterior[0] < prior[0], "State without A should decrease");
        Assert.True(posterior[1] > prior[1], "State with A should increase");
    }

    [Fact]
    public void UpdatePosterior_IncorrectAnswer_DecreasesStatesWithConcept()
    {
        var states = new List<KnowledgeState>
        {
            new(ImmutableHashSet<string>.Empty),
            new(ImmutableHashSet.Create("A"))
        };
        var prior = new float[] { 0.50f, 0.50f };

        var posterior = DiagnosticEngine.UpdatePosterior(prior, states, "A", isCorrect: false);

        Assert.True(posterior[0] > prior[0], "State without A should increase");
        Assert.True(posterior[1] < prior[1], "State with A should decrease");
    }

    [Fact]
    public void UpdatePosterior_Skip_WeakSignal()
    {
        var states = new List<KnowledgeState>
        {
            new(ImmutableHashSet<string>.Empty),
            new(ImmutableHashSet.Create("A"))
        };
        var prior = new float[] { 0.50f, 0.50f };

        var posteriorSkip = DiagnosticEngine.UpdatePosterior(prior, states, "A",
            isCorrect: false, isSkip: true);
        var posteriorWrong = DiagnosticEngine.UpdatePosterior(prior, states, "A",
            isCorrect: false, isSkip: false);

        Assert.True(posteriorSkip[1] > posteriorWrong[1],
            "Skip should be less punishing than incorrect");
    }

    [Fact]
    public void UpdatePosterior_Normalized()
    {
        var states = new List<KnowledgeState>
        {
            new(ImmutableHashSet<string>.Empty),
            new(ImmutableHashSet.Create("A")),
            new(ImmutableHashSet.Create("A", "B"))
        };
        var prior = new float[] { 0.33f, 0.34f, 0.33f };

        var posterior = DiagnosticEngine.UpdatePosterior(prior, states, "A", isCorrect: true);

        float sum = posterior.Sum();
        Assert.InRange(sum, 0.99f, 1.01f);
    }

    [Fact]
    public void RunDiagnostic_KnownStudent_IdentifiesMasteredConcepts()
    {
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["A"] = new("A", "Basics", "math", "algebra", 1, 0.3f, 0.5f, 3),
                ["B"] = new("B", "Intermediate", "math", "algebra", 2, 0.5f, 0.6f, 4),
                ["C"] = new("C", "Advanced", "math", "algebra", 3, 0.7f, 0.7f, 5),
            },
            prerequisites: new()
            {
                ["B"] = new() { new("A", "B", 1.0f) },
                ["C"] = new() { new("B", "C", 1.0f) }
            });

        var answers = new Dictionary<string, bool> { ["A"] = true, ["B"] = true, ["C"] = false };

        var result = DiagnosticEngine.RunDiagnostic(graphCache,
            conceptId => answers.GetValueOrDefault(conceptId, false),
            minQuestions: 3, maxQuestions: 5);

        Assert.Contains("A", result.MasteredConcepts);
        Assert.Contains("B", result.MasteredConcepts);
        Assert.Contains("C", result.GapConcepts);
        Assert.True(result.QuestionsAsked >= 3);
    }

    [Fact]
    public void KnowledgeStateSpace_SmallGraph_EnumeratesAll()
    {
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["A"] = new("A", "A", "math", "alg", 1, 0.3f, 0.5f, 3),
                ["B"] = new("B", "B", "math", "alg", 2, 0.5f, 0.6f, 4),
            },
            prerequisites: new()
            {
                ["B"] = new() { new("A", "B", 1.0f) }
            });

        var states = KnowledgeStateSpace.BuildFeasibleStates(graphCache);

        // Feasible: {}, {A}, {A,B} — {B} alone is NOT feasible (A is prereq)
        Assert.Equal(3, states.Count);
        Assert.Contains(states, s => s.Count == 0);
        Assert.Contains(states, s => s.Count == 1 && s.Contains("A"));
        Assert.Contains(states, s => s.Count == 2 && s.Contains("A") && s.Contains("B"));
    }
}
