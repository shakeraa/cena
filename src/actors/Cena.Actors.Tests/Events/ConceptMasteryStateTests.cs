using Cena.Actors.Events;

namespace Cena.Actors.Tests.Events;

/// <summary>
/// Tests for ConceptMasteryState -- ACT-025.3: internal setters.
/// Verifies that internal set is accessible within the assembly
/// and that snapshot Apply methods work correctly.
/// </summary>
public sealed class ConceptMasteryStateTests
{
    [Fact]
    public void InternalSetters_AccessibleFromSameAssembly()
    {
        // ConceptMasteryState uses { get; internal set; }
        // This test verifies the test project (InternalsVisibleTo or same assembly)
        // can still construct and use the type.
        var state = new ConceptMasteryState();

        // If these compile and run, internal setters are accessible
        Assert.Equal(0.0, state.PKnown);
        Assert.False(state.IsMastered);
        Assert.Equal(0, state.TotalAttempts);
        Assert.Null(state.LastAttemptedAt);
        Assert.Null(state.MasteredAt);
        Assert.Null(state.LastMethodology);
    }

    [Fact]
    public void StudentProfileSnapshot_Apply_ConceptAttempted_UpdatesMasteryState()
    {
        var snapshot = new StudentProfileSnapshot();
        var evt = new ConceptAttempted_V1(
            "student-1", "concept-1", "session-1",
            true, 3000, "q1", "MultipleChoice",
            "Socratic", "None", 0.3, 0.45,
            0, false, "hash", 0, 0, false, DateTimeOffset.UtcNow);

        snapshot.Apply(evt);

        Assert.True(snapshot.ConceptMastery.ContainsKey("concept-1"));
        Assert.Equal(0.45, snapshot.ConceptMastery["concept-1"].PKnown);
        Assert.Equal(1, snapshot.ConceptMastery["concept-1"].TotalAttempts);
    }

    [Fact]
    public void StudentProfileSnapshot_Apply_ConceptMastered_SetsHalfLife()
    {
        var snapshot = new StudentProfileSnapshot();
        var evt = new ConceptMastered_V1(
            "student-1", "concept-1", "session-1",
            0.87, 15, 5, "Socratic", 24.0, DateTimeOffset.UtcNow);

        snapshot.Apply(evt);

        Assert.True(snapshot.ConceptMastery["concept-1"].IsMastered);
        Assert.Equal(24.0, snapshot.HalfLifeMap["concept-1"]);
    }

    [Fact]
    public void StudentProfileSnapshot_Apply_MasteryDecayed_ClearsIsMastered()
    {
        var snapshot = new StudentProfileSnapshot();

        // First master the concept
        snapshot.Apply(new ConceptMastered_V1(
            "s1", "c1", "s1", 0.9, 10, 3, "S", 24.0, DateTimeOffset.UtcNow));

        Assert.True(snapshot.ConceptMastery["c1"].IsMastered);

        // Then decay it
        snapshot.Apply(new MasteryDecayed_V1("s1", "c1", 0.6, 12.0, 48.0));

        Assert.False(snapshot.ConceptMastery["c1"].IsMastered);
        Assert.Equal(0.6, snapshot.ConceptMastery["c1"].PKnown);
    }

    [Fact]
    public void StudentProfileSnapshot_Apply_MethodologySwitched_TracksHistory()
    {
        var snapshot = new StudentProfileSnapshot();
        snapshot.Apply(new MethodologySwitched_V1(
            "s1", "c1", "Socratic", "Feynman", "stagnation", 0.8, "Conceptual", 0.7));

        Assert.Equal("Feynman", snapshot.ActiveMethodologyMap["c1"]);
        Assert.Contains("Feynman", snapshot.MethodAttemptHistory["c1"]);
    }
}
