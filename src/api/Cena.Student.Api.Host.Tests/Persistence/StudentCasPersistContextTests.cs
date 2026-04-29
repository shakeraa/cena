// =============================================================================
// Cena Platform — StudentCasPersistContext unit tests (PRR-272 Slice 1)
//
// Pins the business-rule properties on StudentCasPersistContext:
//   • IdempotencyKey (existing rule from ADR-0059 §5).
//   • IsCoverageEligible (PRR-272 / ADR-0043 §1.1 / ADR-0059 §15.5; new).
//
// The IsCoverageEligible rule encodes the parametric-vs-structural coverage
// distinction at the closest layer to where the rule originates (the
// persist context that flows from the variant endpoint to the persister).
// Downstream consumers (CasGatedQuestionPersister + the prr-210 projection
// + CoverageCellVariantCounter.Record + scripts/shipgate/coverage-slo.mjs)
// will read this property as PRR-272 wires the full chain. Pinning it here
// catches a regression that flips the rule from "Structural counts" to
// e.g. "Parametric counts" or both, without anyone having to re-read the
// ADR amendments.
// =============================================================================

using Cena.Actors.Cas;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Persistence;

public sealed class StudentCasPersistContextTests
{
    // =========================================================================
    // PRR-272 — IsCoverageEligible business rule
    //
    // Rule: only Structural variants count toward coverage SLO. Parametric
    // are derivative under Israeli Copyright Law §16 (per persona-ministry
    // 2026-04-28 §14.2) and pedagogically thin (one underlying question
    // dressed up N times) so they do NOT close coverage gaps.
    // =========================================================================

    [Fact]
    [Trait("source", "PRR-272-coverage-eligible")]
    public void Structural_variant_is_coverage_eligible()
    {
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-001",
            SourceProvenance: null,
            SourceShailonCode: "035582",
            SourceQuestionIndex: 3,
            VariationKind: VariationKind.Structural);

        Assert.True(ctx.IsCoverageEligible,
            "Structural variants close coverage gaps; the rule from ADR-0043 §1.1 must hold.");
    }

    [Fact]
    [Trait("source", "PRR-272-coverage-eligible")]
    public void Parametric_variant_is_NOT_coverage_eligible()
    {
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-001",
            SourceProvenance: null,
            SourceShailonCode: "035582",
            SourceQuestionIndex: 3,
            VariationKind: VariationKind.Parametric,
            ParametricSeed: 42);

        Assert.False(ctx.IsCoverageEligible,
            "Parametric variants are derivative under §16; they MUST NOT close coverage gaps. "
            + "If this assertion ever flips, the variant-flood gap returns and ADR-0043 §1.1 + ADR-0059 §15.5 are violated.");
    }

    [Fact]
    [Trait("source", "PRR-272-coverage-eligible")]
    public void Parametric_with_no_lineage_is_still_NOT_coverage_eligible()
    {
        // The rule is on VariationKind alone, not on lineage presence — a
        // parametric variant without source lineage is even less defensible
        // for coverage credit. Pin the rule explicitly.
        var ctx = new StudentCasPersistContext(
            StudentId: "stu-001",
            SourceProvenance: null,
            SourceShailonCode: null,
            SourceQuestionIndex: null,
            VariationKind: VariationKind.Parametric,
            ParametricSeed: null);

        Assert.False(ctx.IsCoverageEligible);
    }

    [Fact]
    [Trait("source", "PRR-272-coverage-eligible")]
    public void Coverage_eligibility_does_not_depend_on_idempotency_key()
    {
        // Defense-in-depth: a structural variant has IdempotencyKey == null
        // (non-deterministic, no dedup), but it still counts toward coverage.
        // Conversely a parametric with a stable IdempotencyKey does NOT count.
        var structuralNoKey = new StudentCasPersistContext(
            StudentId: "stu-001",
            SourceProvenance: null,
            SourceShailonCode: "035582",
            SourceQuestionIndex: 3,
            VariationKind: VariationKind.Structural);

        var parametricWithKey = new StudentCasPersistContext(
            StudentId: "stu-001",
            SourceProvenance: null,
            SourceShailonCode: "035582",
            SourceQuestionIndex: 3,
            VariationKind: VariationKind.Parametric,
            ParametricSeed: 42);

        Assert.Null(structuralNoKey.IdempotencyKey);
        Assert.True(structuralNoKey.IsCoverageEligible);

        Assert.NotNull(parametricWithKey.IdempotencyKey);
        Assert.False(parametricWithKey.IsCoverageEligible);
    }
}
