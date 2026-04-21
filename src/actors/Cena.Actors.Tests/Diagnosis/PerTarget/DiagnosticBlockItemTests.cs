// =============================================================================
// Cena Platform — DiagnosticBlockItem factory tests (prr-228)
//
// Pins the ADR-0043 provenance stamping invariant at the item level:
//   - Every item carries a non-null Provenance record.
//   - ProvenanceKind.MinistryBagrut throws at construction — mirrors the
//     existing Deliverable<T>.From pattern.
// =============================================================================

using Cena.Actors.Content;
using Cena.Actors.Diagnosis.PerTarget;
using Cena.Actors.Mastery;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PerTarget;

public sealed class DiagnosticBlockItemTests
{
    private static readonly SkillCode Skill =
        SkillCode.Parse("math.algebra.quadratic-equations");

    [Fact]
    public void Create_AcceptsAiRecreated()
    {
        var provenance = new Provenance(
            ProvenanceKind.AiRecreated,
            DateTimeOffset.UtcNow,
            "recreation-abc-123");

        var item = DiagnosticBlockItem.Create(
            itemId: "q-1",
            skillCode: Skill,
            difficultyIrt: -0.5,
            band: "easy",
            provenance: provenance);

        Assert.Equal("q-1", item.ItemId);
        Assert.Equal(ProvenanceKind.AiRecreated, item.Provenance.Kind);
    }

    [Fact]
    public void Create_AcceptsTeacherAuthoredOriginal()
    {
        var provenance = new Provenance(
            ProvenanceKind.TeacherAuthoredOriginal,
            DateTimeOffset.UtcNow,
            "teacher-456");

        var item = DiagnosticBlockItem.Create(
            itemId: "q-2",
            skillCode: Skill,
            difficultyIrt: 0.5,
            band: "medium",
            provenance: provenance);

        Assert.Equal(ProvenanceKind.TeacherAuthoredOriginal, item.Provenance.Kind);
    }

    [Fact]
    public void Create_RejectsMinistryBagrut()
    {
        var ministryProvenance = new Provenance(
            ProvenanceKind.MinistryBagrut,
            DateTimeOffset.UtcNow,
            "035581");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiagnosticBlockItem.Create(
                itemId: "q-3",
                skillCode: Skill,
                difficultyIrt: 0.0,
                band: "medium",
                provenance: ministryProvenance));

        Assert.Contains("ADR-0043", ex.Message);
        Assert.Contains("035581", ex.Message);
    }

    [Fact]
    public void Create_RejectsEmptyItemId()
    {
        var provenance = new Provenance(
            ProvenanceKind.AiRecreated, DateTimeOffset.UtcNow, "x");

        Assert.Throws<ArgumentException>(() =>
            DiagnosticBlockItem.Create(
                itemId: "",
                skillCode: Skill,
                difficultyIrt: 0.0,
                band: "easy",
                provenance: provenance));
    }

    [Fact]
    public void Create_RejectsEmptyBand()
    {
        var provenance = new Provenance(
            ProvenanceKind.AiRecreated, DateTimeOffset.UtcNow, "x");

        Assert.Throws<ArgumentException>(() =>
            DiagnosticBlockItem.Create(
                itemId: "q-1",
                skillCode: Skill,
                difficultyIrt: 0.0,
                band: "",
                provenance: provenance));
    }
}
