// =============================================================================
// Cena Platform — StepSkippingTolerator tests (EPIC-PRR-J PRR-362, ADR-0002)
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class StepSkippingToleratorTests
{
    private static readonly CasVerifyResult FailedCas = CasVerifyResult.Failure(
        CasOperation.Equivalence, "SymPy", 15, "not equal");

    [Fact]
    public void Close_algebraic_error_is_classified_Wrong()
    {
        // "x + 2 = 5" vs "x = 2" — student dropped the subtraction.
        // Short strings, high character overlap → Wrong.
        var sut = new StepSkippingTolerator();
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "x + 2 = 5",
            ToCanonical: "x = 2",
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.Wrong, outcome);
    }

    [Fact]
    public void Short_close_strings_with_one_typo_are_Wrong()
    {
        // Overlap ratio high → Wrong.
        var sut = new StepSkippingTolerator();
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "2x + 3",
            ToCanonical: "2x + 5",   // typo-like error
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.Wrong, outcome);
    }

    [Fact]
    public void Drastically_different_forms_are_classified_UnfollowableSkip()
    {
        // Student jumped from a quadratic equation to a solution pair
        // with no intermediate work visible. Length ratio + low overlap
        // → UnfollowableSkip.
        var sut = new StepSkippingTolerator();
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "x^2 - 5x + 6 = 0",
            ToCanonical: "x \\in \\{2, 3\\}",
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.UnfollowableSkip, outcome);
    }

    [Fact]
    public void Very_short_vs_very_long_is_UnfollowableSkip_by_length_ratio()
    {
        // length ratio > 3.0 default threshold → UnfollowableSkip.
        var sut = new StepSkippingTolerator();
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "x",
            ToCanonical: "x^2 + 3x + 2 = \\frac{-5 + \\sqrt{17}}{2}",
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.UnfollowableSkip, outcome);
    }

    [Fact]
    public void Empty_from_is_Wrong_not_Unfollowable()
    {
        // Degenerate input — we can't reason about "skipped intermediate
        // steps" when we have no starting point. Classify as Wrong so
        // the chain surfaces a concrete error.
        var sut = new StepSkippingTolerator();
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "",
            ToCanonical: "x = 5",
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.Wrong, outcome);
    }

    [Fact]
    public void Empty_to_is_Wrong()
    {
        var sut = new StepSkippingTolerator();
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "x + 2",
            ToCanonical: "",
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.Wrong, outcome);
    }

    [Fact]
    public void Thresholds_are_configurable()
    {
        // With an aggressive overlap threshold (80%) even similar strings
        // that CAS rejected get classified as UnfollowableSkip. Proves the
        // knob is live and respected.
        var aggressive = new StepSkippingToleratorOptions(
            OverlapUnfollowableThreshold: 0.80,
            LengthRatioUnfollowableThreshold: 3.0);
        var sut = new StepSkippingTolerator(aggressive);
        var outcome = sut.Classify(new StepSkippingContext(
            FromCanonical: "2x + 3",
            ToCanonical: "2x + 5",   // would be Wrong on default options
            CasResult: FailedCas));

        Assert.Equal(StepTransitionOutcome.UnfollowableSkip, outcome);
    }

    [Fact]
    public void Null_context_throws()
    {
        var sut = new StepSkippingTolerator();
        Assert.Throws<ArgumentNullException>(() =>
            sut.Classify(null!));
    }

    // ── ComputeOverlapRatio — pure helper ──────────────────────────────────

    [Fact]
    public void OverlapRatio_identical_strings_is_one()
    {
        Assert.Equal(1.0, StepSkippingTolerator.ComputeOverlapRatio("abcdef", "abcdef"));
    }

    [Fact]
    public void OverlapRatio_completely_disjoint_is_zero()
    {
        Assert.Equal(0.0, StepSkippingTolerator.ComputeOverlapRatio("abcdef", "xyzuvw"));
    }

    [Fact]
    public void OverlapRatio_null_side_is_zero()
    {
        Assert.Equal(0.0, StepSkippingTolerator.ComputeOverlapRatio(null!, "abc"));
        Assert.Equal(0.0, StepSkippingTolerator.ComputeOverlapRatio("abc", null!));
    }

    [Fact]
    public void OverlapRatio_short_strings_fall_back_to_character_set()
    {
        // "ab" (2 chars, below NGramSize=3) vs "abc" — all of {a,b}
        // appear in "abc" so ratio = 1.0.
        Assert.Equal(1.0, StepSkippingTolerator.ComputeOverlapRatio("ab", "abc"));
    }
}
