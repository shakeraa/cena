// =============================================================================
// MST-014 Tests: Initial state populator
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class InitialStatePopulatorTests
{
    [Fact]
    public void Populate_MasteredConcepts_GetOptimisticDefaults()
    {
        var result = new DiagnosticResult(
            MasteredConcepts: new HashSet<string> { "algebra", "geometry" }.ToImmutableHashSet(),
            GapConcepts: new HashSet<string> { "calculus" }.ToImmutableHashSet(),
            Confidence: 0.90f,
            QuestionsAsked: 12);

        var now = DateTimeOffset.Parse("2026-03-26T10:00:00Z");
        var states = InitialStatePopulator.Populate(result, now);

        Assert.Equal(2, states.Count);
        Assert.True(states.ContainsKey("algebra"));
        Assert.True(states.ContainsKey("geometry"));
        Assert.False(states.ContainsKey("calculus"));

        var algebraState = states["algebra"];
        Assert.Equal(0.85f, algebraState.MasteryProbability);
        Assert.Equal(168f, algebraState.HalfLifeHours);
        Assert.Equal(3, algebraState.BloomLevel);
        Assert.Equal(1, algebraState.AttemptCount);
        Assert.Equal(1, algebraState.CorrectCount);
        Assert.Equal(now, algebraState.LastInteraction);
    }

    [Fact]
    public void Populate_LowConfidence_ScalesDownMastery()
    {
        var result = new DiagnosticResult(
            MasteredConcepts: new HashSet<string> { "algebra" }.ToImmutableHashSet(),
            GapConcepts: ImmutableHashSet<string>.Empty,
            Confidence: 0.60f,
            QuestionsAsked: 10);

        var states = InitialStatePopulator.Populate(result, DateTimeOffset.UtcNow);

        var state = states["algebra"];
        // mastery = 0.85 * 0.60 = 0.51
        Assert.InRange(state.MasteryProbability, 0.50f, 0.52f);
        // half-life = 168 * 0.60 = 100.8
        Assert.InRange(state.HalfLifeHours, 100f, 102f);
    }

    [Fact]
    public void Populate_HighConfidence_UsesFullDefaults()
    {
        var result = new DiagnosticResult(
            MasteredConcepts: new HashSet<string> { "algebra" }.ToImmutableHashSet(),
            GapConcepts: ImmutableHashSet<string>.Empty,
            Confidence: 0.95f,
            QuestionsAsked: 15);

        var states = InitialStatePopulator.Populate(result, DateTimeOffset.UtcNow);

        Assert.Equal(0.85f, states["algebra"].MasteryProbability);
        Assert.Equal(168f, states["algebra"].HalfLifeHours);
    }

    [Fact]
    public void Populate_EmptyMastered_ReturnsEmpty()
    {
        var result = new DiagnosticResult(
            MasteredConcepts: ImmutableHashSet<string>.Empty,
            GapConcepts: new HashSet<string> { "everything" }.ToImmutableHashSet(),
            Confidence: 0.80f,
            QuestionsAsked: 15);

        var states = InitialStatePopulator.Populate(result, DateTimeOffset.UtcNow);

        Assert.Empty(states);
    }

    [Fact]
    public void CreateEvent_ProducesCorrectEvent()
    {
        var result = new DiagnosticResult(
            MasteredConcepts: new HashSet<string> { "A", "B", "C" }.ToImmutableHashSet(),
            GapConcepts: new HashSet<string> { "D" }.ToImmutableHashSet(),
            Confidence: 0.85f,
            QuestionsAsked: 12);

        var evt = InitialStatePopulator.CreateEvent("student-1", result, DateTimeOffset.UtcNow);

        Assert.Equal("student-1", evt.StudentId);
        Assert.Equal(3, evt.MasteredConceptIds.Count);
        Assert.Single(evt.GapConceptIds);
        Assert.Equal(12, evt.QuestionsAsked);
        Assert.Equal(0.85f, evt.Confidence);
    }
}
