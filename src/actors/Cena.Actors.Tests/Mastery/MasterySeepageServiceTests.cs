using Cena.Actors.Events;
using Cena.Actors.Mastery;
using SnapshotMasteryState = Cena.Actors.Events.ConceptMasteryState;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public class MasteryKeysTests
{
    [Fact]
    public void Key_produces_composite_key()
    {
        var key = MasteryKeys.Key("enr-001", "linear-equations");
        Assert.Equal("enr-001:linear-equations", key);
    }

    [Fact]
    public void Parse_splits_composite_key()
    {
        var (enrollmentId, conceptId) = MasteryKeys.Parse("enr-001:linear-equations");
        Assert.Equal("enr-001", enrollmentId);
        Assert.Equal("linear-equations", conceptId);
    }

    [Fact]
    public void Parse_handles_legacy_flat_key()
    {
        var (enrollmentId, conceptId) = MasteryKeys.Parse("linear-equations");
        Assert.Equal("default", enrollmentId);
        Assert.Equal("linear-equations", conceptId);
    }

    [Fact]
    public void Parse_handles_key_with_multiple_colons()
    {
        var (enrollmentId, conceptId) = MasteryKeys.Parse("enr:001:concept:x");
        Assert.Equal("enr", enrollmentId);
        Assert.Equal("001:concept:x", conceptId);
    }
}

public class MasterySeepageServiceTests
{
    private readonly MasterySeepageService _sut = new();

    [Fact]
    public void SameSubject_seeds_at_60_percent_capped_at_0_5()
    {
        var snapshot = CreateSnapshot("student-1");
        var sourceEnrollment = "enr-bagrut";
        var targetEnrollment = "enr-sat";

        // Source has mastered linear-equations at 0.95
        var sourceKey = MasteryKeys.Key(sourceEnrollment, "linear-equations");
        snapshot.ConceptMastery[sourceKey] = new SnapshotMasteryState { PKnown = 0.95 };

        var result = _sut.ApplySeepage(
            snapshot, sourceEnrollment, targetEnrollment,
            sourceSubject: "math", targetSubject: "math",
            overlappingConceptIds: new[] { "linear-equations" });

        Assert.Equal(1, result.ConceptsSeeded);
        Assert.Single(result.Events);

        // 0.95 * 0.60 = 0.57, capped at 0.50
        var targetKey = MasteryKeys.Key(targetEnrollment, "linear-equations");
        Assert.True(snapshot.ConceptMastery.ContainsKey(targetKey));
        Assert.Equal(0.50, snapshot.ConceptMastery[targetKey].PKnown);
        Assert.Equal(sourceEnrollment, snapshot.ConceptMastery[targetKey].SourceEnrollmentId);
        Assert.Equal(0.60, snapshot.ConceptMastery[targetKey].SeepageFactor);

        // Source unchanged
        Assert.Equal(0.95, snapshot.ConceptMastery[sourceKey].PKnown);

        // Event audit
        var evt = result.Events[0];
        Assert.Equal(0.60, evt.SeepageFactor);
        Assert.Equal(0.95, evt.SourcePKnown);
        Assert.Equal(0.50, evt.SeededPKnown);
    }

    [Fact]
    public void SameSubject_low_mastery_seeds_below_cap()
    {
        var snapshot = CreateSnapshot("student-1");
        var sourceKey = MasteryKeys.Key("enr-a", "quadratics");
        snapshot.ConceptMastery[sourceKey] = new SnapshotMasteryState { PKnown = 0.40 };

        var result = _sut.ApplySeepage(
            snapshot, "enr-a", "enr-b",
            sourceSubject: "math", targetSubject: "math",
            overlappingConceptIds: new[] { "quadratics" });

        // 0.40 * 0.60 = 0.24, below cap
        var targetKey = MasteryKeys.Key("enr-b", "quadratics");
        Assert.Equal(0.24, snapshot.ConceptMastery[targetKey].PKnown, precision: 10);
    }

    [Fact]
    public void CrossSubject_seeds_at_20_percent()
    {
        var snapshot = CreateSnapshot("student-1");
        var sourceKey = MasteryKeys.Key("enr-math", "linear-equations");
        snapshot.ConceptMastery[sourceKey] = new SnapshotMasteryState { PKnown = 0.90 };

        var result = _sut.ApplySeepage(
            snapshot, "enr-math", "enr-physics",
            sourceSubject: "math", targetSubject: "physics",
            overlappingConceptIds: new[] { "linear-equations" });

        // 0.90 * 0.20 = 0.18
        var targetKey = MasteryKeys.Key("enr-physics", "linear-equations");
        Assert.Equal(0.18, snapshot.ConceptMastery[targetKey].PKnown, precision: 10);
        Assert.Equal(0.20, snapshot.ConceptMastery[targetKey].SeepageFactor);
    }

    [Fact]
    public void Does_not_overwrite_existing_target_mastery()
    {
        var snapshot = CreateSnapshot("student-1");

        var sourceKey = MasteryKeys.Key("enr-a", "algebra");
        snapshot.ConceptMastery[sourceKey] = new SnapshotMasteryState { PKnown = 0.90 };

        // Target already has mastery data
        var targetKey = MasteryKeys.Key("enr-b", "algebra");
        snapshot.ConceptMastery[targetKey] = new SnapshotMasteryState { PKnown = 0.30 };

        var result = _sut.ApplySeepage(
            snapshot, "enr-a", "enr-b",
            sourceSubject: "math", targetSubject: "math",
            overlappingConceptIds: new[] { "algebra" });

        Assert.Equal(0, result.ConceptsSeeded);
        Assert.Empty(result.Events);
        // Target unchanged
        Assert.Equal(0.30, snapshot.ConceptMastery[targetKey].PKnown);
    }

    [Fact]
    public void Skips_concepts_without_source_mastery()
    {
        var snapshot = CreateSnapshot("student-1");

        var result = _sut.ApplySeepage(
            snapshot, "enr-a", "enr-b",
            sourceSubject: "math", targetSubject: "math",
            overlappingConceptIds: new[] { "no-such-concept" });

        Assert.Equal(0, result.ConceptsSeeded);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void Seeps_half_life_when_available()
    {
        var snapshot = CreateSnapshot("student-1");
        var sourceKey = MasteryKeys.Key("enr-a", "trig");
        snapshot.ConceptMastery[sourceKey] = new SnapshotMasteryState { PKnown = 0.85 };
        snapshot.HalfLifeMap[sourceKey] = 48.0;

        _sut.ApplySeepage(
            snapshot, "enr-a", "enr-b",
            sourceSubject: "math", targetSubject: "math",
            overlappingConceptIds: new[] { "trig" });

        var targetKey = MasteryKeys.Key("enr-b", "trig");
        Assert.Equal(48.0, snapshot.HalfLifeMap[targetKey]);
    }

    [Fact]
    public void Multiple_concepts_seep_independently()
    {
        var snapshot = CreateSnapshot("student-1");

        snapshot.ConceptMastery[MasteryKeys.Key("enr-a", "c1")] = new SnapshotMasteryState { PKnown = 0.80 };
        snapshot.ConceptMastery[MasteryKeys.Key("enr-a", "c2")] = new SnapshotMasteryState { PKnown = 0.60 };
        snapshot.ConceptMastery[MasteryKeys.Key("enr-a", "c3")] = new SnapshotMasteryState { PKnown = 0.30 };

        var result = _sut.ApplySeepage(
            snapshot, "enr-a", "enr-b",
            sourceSubject: "math", targetSubject: "math",
            overlappingConceptIds: new[] { "c1", "c2", "c3" });

        Assert.Equal(3, result.ConceptsSeeded);
        Assert.Equal(3, result.Events.Count);

        // c1: 0.80*0.60=0.48
        Assert.Equal(0.48, snapshot.ConceptMastery[MasteryKeys.Key("enr-b", "c1")].PKnown, precision: 10);
        // c2: 0.60*0.60=0.36
        Assert.Equal(0.36, snapshot.ConceptMastery[MasteryKeys.Key("enr-b", "c2")].PKnown, precision: 10);
        // c3: 0.30*0.60=0.18
        Assert.Equal(0.18, snapshot.ConceptMastery[MasteryKeys.Key("enr-b", "c3")].PKnown, precision: 10);
    }

    private static StudentProfileSnapshot CreateSnapshot(string studentId) => new()
    {
        StudentId = studentId,
        ConceptMastery = new Dictionary<string, SnapshotMasteryState>(),
        HalfLifeMap = new Dictionary<string, double>()
    };
}
