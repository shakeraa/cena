// =============================================================================
// MST-001 Tests: ConceptMasteryState value object
// =============================================================================

using System.Text.Json;
using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class ConceptMasteryStateDomainTests
{
    [Fact]
    public void RecallProbability_AtHalfLife_Returns50Percent()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.95f,
            HalfLifeHours = 168f, // 1 week
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-168)
        };

        var recall = state.RecallProbability(DateTimeOffset.UtcNow);
        Assert.InRange(recall, 0.49f, 0.51f);
    }

    [Fact]
    public void RecallProbability_AtDoubleHalfLife_Returns25Percent()
    {
        var state = new ConceptMasteryState
        {
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-336)
        };

        var recall = state.RecallProbability(DateTimeOffset.UtcNow);
        Assert.InRange(recall, 0.24f, 0.26f);
    }

    [Fact]
    public void RecallProbability_ZeroHalfLife_ReturnsZero()
    {
        var state = new ConceptMasteryState { HalfLifeHours = 0f, LastInteraction = DateTimeOffset.UtcNow };
        Assert.Equal(0.0f, state.RecallProbability(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RecallProbability_DefaultLastInteraction_ReturnsZero()
    {
        var state = new ConceptMasteryState { HalfLifeHours = 168f };
        Assert.Equal(0.0f, state.RecallProbability(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RecallProbability_JustPracticed_ReturnsOne()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ConceptMasteryState
        {
            HalfLifeHours = 168f,
            LastInteraction = now
        };
        Assert.Equal(1.0f, state.RecallProbability(now));
    }

    [Fact]
    public void WithAttempt_Correct_ExtendsStreak()
    {
        var state = new ConceptMasteryState { CurrentStreak = 3, AttemptCount = 5, CorrectCount = 3 };
        var updated = state.WithAttempt(correct: true, DateTimeOffset.UtcNow);

        Assert.Equal(4, updated.CurrentStreak);
        Assert.Equal(6, updated.AttemptCount);
        Assert.Equal(4, updated.CorrectCount);
    }

    [Fact]
    public void WithAttempt_Incorrect_ResetsStreak()
    {
        var state = new ConceptMasteryState { CurrentStreak = 7, AttemptCount = 10, CorrectCount = 7 };
        var updated = state.WithAttempt(correct: false, DateTimeOffset.UtcNow);

        Assert.Equal(0, updated.CurrentStreak);
        Assert.Equal(11, updated.AttemptCount);
        Assert.Equal(7, updated.CorrectCount);
    }

    [Fact]
    public void WithAttempt_SetsFirstEncounterOnFirstAttempt()
    {
        var state = new ConceptMasteryState();
        var now = DateTimeOffset.UtcNow;
        var updated = state.WithAttempt(correct: true, now);

        Assert.Equal(now, updated.FirstEncounter);
        Assert.Equal(now, updated.LastInteraction);
    }

    [Fact]
    public void WithAttempt_PreservesExistingFirstEncounter()
    {
        var first = DateTimeOffset.UtcNow.AddDays(-7);
        var state = new ConceptMasteryState { FirstEncounter = first };
        var updated = state.WithAttempt(correct: true, DateTimeOffset.UtcNow);

        Assert.Equal(first, updated.FirstEncounter);
    }

    [Fact]
    public void IsDecaying_MasteredButForgotten_ReturnsTrue()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.95f,
            HalfLifeHours = 48f,
            LastInteraction = DateTimeOffset.UtcNow.AddDays(-7)
        };

        Assert.True(state.IsDecaying(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsDecaying_RecentPractice_ReturnsFalse()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.95f,
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.False(state.IsDecaying(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MasteryLevel_CorrectThresholds()
    {
        Assert.Equal(Cena.Actors.Mastery.MasteryLevel.NotStarted, new ConceptMasteryState { MasteryProbability = 0.05f }.MasteryLevel);
        Assert.Equal(Cena.Actors.Mastery.MasteryLevel.Introduced, new ConceptMasteryState { MasteryProbability = 0.25f }.MasteryLevel);
        Assert.Equal(Cena.Actors.Mastery.MasteryLevel.Developing, new ConceptMasteryState { MasteryProbability = 0.55f }.MasteryLevel);
        Assert.Equal(Cena.Actors.Mastery.MasteryLevel.Proficient, new ConceptMasteryState { MasteryProbability = 0.80f }.MasteryLevel);
        Assert.Equal(Cena.Actors.Mastery.MasteryLevel.Mastered, new ConceptMasteryState { MasteryProbability = 0.95f }.MasteryLevel);
    }

    [Fact]
    public void Immutability_WithMethods_ReturnNewInstances()
    {
        var original = new ConceptMasteryState { MasteryProbability = 0.5f };
        var updated = original.WithBktUpdate(0.8f);

        Assert.Equal(0.5f, original.MasteryProbability);
        Assert.Equal(0.8f, updated.MasteryProbability);
        Assert.NotSame(original, updated);
    }

    [Fact]
    public void WithBloomLevel_ValidRange_Updates()
    {
        var state = new ConceptMasteryState();
        var updated = state.WithBloomLevel(4);
        Assert.Equal(4, updated.BloomLevel);
    }

    [Fact]
    public void WithBloomLevel_OutOfRange_Throws()
    {
        var state = new ConceptMasteryState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.WithBloomLevel(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.WithBloomLevel(-1));
    }

    [Fact]
    public void WithMethodAttempt_AppendsToHistory()
    {
        var state = new ConceptMasteryState();
        var attempt = new MethodAttempt("Socratic", 3, "Improved");
        var updated = state.WithMethodAttempt(attempt);

        Assert.Single(updated.MethodHistory);
        Assert.Equal("Socratic", updated.MethodHistory[0].MethodologyId);
    }

    [Fact]
    public void RollingAccuracy_NoAttempts_ReturnsZero()
    {
        Assert.Equal(0.0f, new ConceptMasteryState().RollingAccuracy);
    }

    [Fact]
    public void RollingAccuracy_Computed()
    {
        var state = new ConceptMasteryState { AttemptCount = 10, CorrectCount = 7 };
        Assert.InRange(state.RollingAccuracy, 0.69f, 0.71f);
    }

    [Fact]
    public void DefaultValues_SensibleDefaults()
    {
        var state = new ConceptMasteryState();
        Assert.Equal(0f, state.MasteryProbability);
        Assert.Equal(0f, state.HalfLifeHours);
        Assert.Equal(0, state.BloomLevel);
        Assert.Equal(0, state.AttemptCount);
        Assert.Equal(0, state.CurrentStreak);
        Assert.Empty(state.RecentErrors);
        Assert.Empty(state.MethodHistory);
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesAllFields()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.85f,
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.Parse("2026-03-20T10:00:00Z"),
            BloomLevel = 4,
            SelfConfidence = 0.7f,
            RecentErrors = new[] { ErrorType.Procedural, ErrorType.Careless },
            QualityQuadrant = MasteryQuality.Mastered,
            CurrentStreak = 5,
            AttemptCount = 20,
            CorrectCount = 17
        };

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<ConceptMasteryState>(json)!;

        Assert.Equal(state.MasteryProbability, deserialized.MasteryProbability);
        Assert.Equal(state.HalfLifeHours, deserialized.HalfLifeHours);
        Assert.Equal(state.LastInteraction, deserialized.LastInteraction);
        Assert.Equal(state.BloomLevel, deserialized.BloomLevel);
        Assert.Equal(state.SelfConfidence, deserialized.SelfConfidence);
        Assert.Equal(state.QualityQuadrant, deserialized.QualityQuadrant);
        Assert.Equal(state.CurrentStreak, deserialized.CurrentStreak);
        Assert.Equal(state.AttemptCount, deserialized.AttemptCount);
        Assert.Equal(state.CorrectCount, deserialized.CorrectCount);
        Assert.Equal(state.RecentErrors, deserialized.RecentErrors);
    }
}
