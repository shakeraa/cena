// =============================================================================
// MST-006 Tests: StudentState mastery overlay integration
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Students;

namespace Cena.Actors.Tests.Mastery;

public sealed class StudentStateMasteryOverlayTests
{
    [Fact]
    public void Apply_ConceptAttempted_PopulatesMasteryOverlay()
    {
        var state = new StudentState { StudentId = "student-1" };
        var evt = new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            true, 8000, "q1", "MultipleChoice",
            "Socratic", "None", 0.3, 0.55,
            0, false, "hash", 0, 0, false, DateTimeOffset.UtcNow);

        state.Apply(evt);

        Assert.True(state.MasteryOverlay.ContainsKey("concept-1"));
        var overlay = state.MasteryOverlay["concept-1"];
        Assert.Equal(0.55f, overlay.MasteryProbability);
        Assert.Equal(1, overlay.AttemptCount);
        Assert.Equal(1, overlay.CorrectCount);
        Assert.Equal(1, overlay.CurrentStreak);
    }

    [Fact]
    public void Apply_MultipleAttempts_AccumulatesOverlay()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            true, 5000, "q1", "MultipleChoice",
            "Socratic", "None", 0.1, 0.35,
            0, false, "h1", 0, 0, false, now));

        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            true, 7000, "q2", "MultipleChoice",
            "Socratic", "None", 0.35, 0.60,
            0, false, "h2", 0, 0, false, now.AddSeconds(30)));

        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            false, 12000, "q3", "MultipleChoice",
            "Socratic", "Procedural", 0.60, 0.45,
            0, false, "h3", 0, 0, false, now.AddSeconds(60)));

        var overlay = state.MasteryOverlay["concept-1"];
        Assert.Equal(3, overlay.AttemptCount);
        Assert.Equal(2, overlay.CorrectCount);
        Assert.Equal(0, overlay.CurrentStreak); // last was incorrect
        Assert.Equal(0.45f, overlay.MasteryProbability);
    }

    [Fact]
    public void Apply_ConceptAttempted_TracksErrorType()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            false, 15000, "q1", "MultipleChoice",
            "Socratic", "Procedural", 0.3, 0.25,
            0, false, "h1", 0, 0, false, now));

        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            false, 18000, "q2", "MultipleChoice",
            "Socratic", "Conceptual", 0.25, 0.20,
            0, false, "h2", 0, 0, false, now.AddSeconds(30)));

        var overlay = state.MasteryOverlay["concept-1"];
        Assert.Equal(2, overlay.RecentErrors.Length);
        Assert.Equal(Cena.Actors.Mastery.ErrorType.Procedural, overlay.RecentErrors[0]);
        Assert.Equal(Cena.Actors.Mastery.ErrorType.Conceptual, overlay.RecentErrors[1]);
    }

    [Fact]
    public void Apply_ConceptAttempted_ClassifiesQualityQuadrant()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        // Warm up baseline with 3 attempts at ~15000ms
        state.Apply(new ConceptAttempted_V1(
            "student-1", "c1", "s1", true, 14000, "q1", "MC", "S", "None",
            0.1, 0.3, 0, false, "h", 0, 0, false, now));
        state.Apply(new ConceptAttempted_V1(
            "student-1", "c1", "s1", true, 15000, "q2", "MC", "S", "None",
            0.3, 0.5, 0, false, "h", 0, 0, false, now.AddSeconds(10)));
        state.Apply(new ConceptAttempted_V1(
            "student-1", "c1", "s1", true, 16000, "q3", "MC", "S", "None",
            0.5, 0.7, 0, false, "h", 0, 0, false, now.AddSeconds(20)));

        // Now a fast correct answer (should be Mastered)
        state.Apply(new ConceptAttempted_V1(
            "student-1", "c1", "s1", true, 5000, "q4", "MC", "S", "None",
            0.7, 0.85, 0, false, "h", 0, 0, false, now.AddSeconds(30)));

        var overlay = state.MasteryOverlay["c1"];
        Assert.Equal(MasteryQuality.Mastered, overlay.QualityQuadrant); // fast + correct
    }

    [Fact]
    public void Apply_ConceptMastered_UpdatesOverlayHalfLife()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        // First create an overlay entry via attempt
        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            true, 8000, "q1", "MC", "Socratic", "None",
            0.3, 0.92, 0, false, "h", 0, 0, false, now));

        // Then mastery event with half-life
        state.Apply(new ConceptMastered_V1(
            "student-1", "concept-1", "session-1",
            0.92, 10, 3, "Socratic", 168.0, now));

        var overlay = state.MasteryOverlay["concept-1"];
        Assert.Equal(168f, overlay.HalfLifeHours);
    }

    [Fact]
    public void Apply_MasteryDecayed_UpdatesOverlay()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        // Create overlay entry
        state.Apply(new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            true, 8000, "q1", "MC", "Socratic", "None",
            0.3, 0.92, 0, false, "h", 0, 0, false, now));

        state.Apply(new ConceptMastered_V1(
            "student-1", "concept-1", "session-1",
            0.92, 10, 3, "Socratic", 168.0, now));

        // Decay event
        state.Apply(new MasteryDecayed_V1(
            "student-1", "concept-1", 0.55, 100.0, 336.0));

        var overlay = state.MasteryOverlay["concept-1"];
        Assert.Equal(0.55f, overlay.MasteryProbability);
        Assert.Equal(100f, overlay.HalfLifeHours);
    }

    [Fact]
    public void Apply_MultiConcepts_IndependentOverlayEntries()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        state.Apply(new ConceptAttempted_V1(
            "student-1", "algebra", "s1", true, 8000, "q1", "MC", "S", "None",
            0.1, 0.40, 0, false, "h", 0, 0, false, now));

        state.Apply(new ConceptAttempted_V1(
            "student-1", "geometry", "s1", false, 20000, "q2", "MC", "S", "Conceptual",
            0.1, 0.15, 0, false, "h", 0, 0, false, now.AddSeconds(30)));

        Assert.Equal(2, state.MasteryOverlay.Count);
        Assert.Equal(0.40f, state.MasteryOverlay["algebra"].MasteryProbability);
        Assert.Equal(0.15f, state.MasteryOverlay["geometry"].MasteryProbability);
        Assert.Equal(1, state.MasteryOverlay["algebra"].CorrectCount);
        Assert.Equal(0, state.MasteryOverlay["geometry"].CorrectCount);
    }

    [Fact]
    public void ResponseBaseline_UpdatedOnEachAttempt()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(15_000f, state.ResponseBaseline.MedianResponseTimeMs); // default

        for (int i = 0; i < 5; i++)
        {
            state.Apply(new ConceptAttempted_V1(
                "student-1", "c1", "s1", true, 10_000, $"q{i}", "MC", "S", "None",
                0.1 + i * 0.1, 0.2 + i * 0.1, 0, false, "h", 0, 0, false,
                now.AddSeconds(i * 10)));
        }

        // After 5 attempts at 10000ms, median should be 10000
        Assert.Equal(10_000f, state.ResponseBaseline.MedianResponseTimeMs);
        Assert.Equal(5, state.ResponseBaseline.SampleCount);
    }

    [Fact]
    public void EventReplay_RebuildsOverlayCorrectly()
    {
        var state = new StudentState { StudentId = "student-1" };
        var now = DateTimeOffset.Parse("2026-03-20T10:00:00Z");

        // Replay a sequence of events
        state.Apply(new ConceptAttempted_V1(
            "student-1", "algebra", "s1", true, 10000, "q1", "MC", "S", "None",
            0.1, 0.35, 0, false, "h", 0, 0, false, now));

        state.Apply(new ConceptAttempted_V1(
            "student-1", "algebra", "s1", true, 8000, "q2", "MC", "S", "None",
            0.35, 0.60, 0, false, "h", 0, 0, false, now.AddMinutes(5)));

        state.Apply(new ConceptAttempted_V1(
            "student-1", "algebra", "s1", false, 15000, "q3", "MC", "S", "Procedural",
            0.60, 0.45, 0, false, "h", 0, 0, false, now.AddMinutes(10)));

        var overlay = state.MasteryOverlay["algebra"];
        Assert.Equal(3, overlay.AttemptCount);
        Assert.Equal(2, overlay.CorrectCount);
        Assert.Equal(0, overlay.CurrentStreak);
        Assert.Equal(0.45f, overlay.MasteryProbability);
        Assert.Single(overlay.RecentErrors);
        Assert.Equal(Cena.Actors.Mastery.ErrorType.Procedural, overlay.RecentErrors[0]);
    }
}
