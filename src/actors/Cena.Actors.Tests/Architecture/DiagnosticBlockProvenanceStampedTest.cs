// =============================================================================
// Cena Platform — Architecture test: per-target diagnostic items must
// always carry provenance (prr-228 / ADR-0043 stamping requirement)
//
// The ADR-0043 tightening in prr-228 requires every item served inside
// a per-target diagnostic block to declare its origin:
//
//    source: "ai-authored-recreation" | "teacher-authored-original"
//           | "ministry-reference"  (curator-internal, never served)
//
// This test asserts two things:
//   1. The `PerTargetDiagnosticItem` DTO (student-web contract) exposes a
//      `ProvenanceSource` AND `ProvenanceLabel` field. If either is
//      renamed or removed, the build fails — a bare diagnostic item
//      without a provenance stamp is an ADR-0043 violation.
//   2. The `DiagnosticBlockItem` domain record (Cena.Actors) carries a
//      `Provenance` field. The construction factory's Ministry-guard
//      is already pinned in DiagnosticBlockItemTests; this test locks
//      the *shape* so new code paths can't introduce a diagnostic item
//      type that skips the provenance stamp.
// =============================================================================

using System.Reflection;
using Cena.Actors.Diagnosis.PerTarget;
using Cena.Student.Api.Host.Endpoints;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class DiagnosticBlockProvenanceStampedTest
{
    [Fact]
    public void PerTargetDiagnosticItemDto_Has_ProvenanceSource_And_ProvenanceLabel()
    {
        var type = typeof(PerTargetDiagnosticItem);

        var provenanceSource = type.GetProperty(
            "ProvenanceSource",
            BindingFlags.Public | BindingFlags.Instance);
        var provenanceLabel = type.GetProperty(
            "ProvenanceLabel",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(provenanceSource);
        Assert.NotNull(provenanceLabel);

        Assert.Equal(typeof(string), provenanceSource!.PropertyType);
        Assert.Equal(typeof(string), provenanceLabel!.PropertyType);
    }

    [Fact]
    public void DiagnosticBlockItem_Has_ProvenanceField()
    {
        var type = typeof(DiagnosticBlockItem);

        var provenance = type.GetProperty(
            "Provenance",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(provenance);
        Assert.Equal("Provenance", provenance!.PropertyType.Name);
    }

    [Fact]
    public void DiagnosticBlockItem_FactoryRejects_MinistryBagrutProvenance()
    {
        // Defense-in-depth: assert the factory still throws on Ministry
        // provenance. Duplicates DiagnosticBlockItemTests (intentional) —
        // this is the arch-level tripwire that guarantees no refactor of
        // DiagnosticBlockItem can silently drop the Ministry guard.
        var ministryProvenance = new Cena.Actors.Content.Provenance(
            Cena.Actors.Content.ProvenanceKind.MinistryBagrut,
            DateTimeOffset.UtcNow,
            "035581");

        Assert.Throws<InvalidOperationException>(() =>
            DiagnosticBlockItem.Create(
                itemId: "q-1",
                skillCode: Cena.Actors.Mastery.SkillCode.Parse("math.algebra.x"),
                difficultyIrt: 0.0,
                band: "easy",
                provenance: ministryProvenance));
    }
}
