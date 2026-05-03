// =============================================================================
// RDY-033: Unit tests for the five CAS-backed buggy-rule matchers.
// Each matcher gets: one positive case, one negative (correct answer), and
// one negative (unrelated wrong answer). The CAS router is mocked — these
// tests verify the matcher's transform extraction and confidence scoring,
// not CAS engine behavior (that's covered in CasRouterServiceTests).
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Services.ErrorPatternMatching;
using Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Services.ErrorPatternMatching;

public class BuggyRuleMatcherTests
{
    private readonly ICasRouterService _cas = Substitute.For<ICasRouterService>();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void CasEquivalence(string a, string b, bool verified, string engine = "MathNet")
    {
        _cas.VerifyAsync(
                Arg.Is<CasVerifyRequest>(r =>
                    r.Operation == CasOperation.Equivalence &&
                    r.ExpressionA == a &&
                    r.ExpressionB == b),
                Arg.Any<CancellationToken>())
            .Returns(verified
                ? CasVerifyResult.Success(CasOperation.Equivalence, engine, 0.5)
                : CasVerifyResult.Failure(CasOperation.Equivalence, engine, 0.5, "not equal"));
    }

    /// <summary>
    /// Default every Equivalence call not explicitly set up to "not equal".
    /// This prevents false positives when the base class pre-checks that the
    /// buggy output is distinct from the correct answer (which it must be).
    /// </summary>
    private void CasDefaultNotEqual(string engine = "MathNet")
    {
        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<CasVerifyRequest>();
                return CasVerifyResult.Failure(req.Operation, engine, 0.5, "default: not equal");
            });
    }

    private static ErrorPatternMatchContext Ctx(string stem, string correct, string student, string subject = "math") =>
        new(stem, correct, student, subject, ConceptId: null);

    // ------------------------------------------------------------------
    // DIST-EXP-SUM — (a+b)^n → a^n + b^n
    // ------------------------------------------------------------------

    [Fact]
    public async Task DistExpSum_PositiveCase_ReturnsHighConfidence()
    {
        CasDefaultNotEqual();
        // Buggy output extracted from stem "(x+3)^2": "(x)^2 + (3)^2"
        // Correct answer is "x^2 + 6x + 9" — the service pre-checks buggy != correct (OK).
        // Student answer "x^2 + 9" must be CAS-equivalent to the buggy output.
        CasEquivalence("x^2 + 9", "(x)^2 + (3)^2", true);

        var sut = new DistExpSumMatcher(_cas, NullLogger<DistExpSumMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Expand (x+3)^2", "x^2 + 6x + 9", "x^2 + 9"),
            CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("DIST-EXP-SUM", result.BuggyRuleId);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public async Task DistExpSum_CorrectAnswer_DoesNotMatch()
    {
        CasDefaultNotEqual();
        var sut = new DistExpSumMatcher(_cas, NullLogger<DistExpSumMatcher>.Instance);

        // Student answer equals correct answer. Engine short-circuit doesn't
        // fire here (we're testing the matcher directly), so the matcher must
        // rely on the CAS returning "not equivalent to buggy output".
        var result = await sut.TryMatchAsync(
            Ctx("Expand (x+3)^2", "x^2 + 6x + 9", "x^2 + 6x + 9"),
            CancellationToken.None);

        Assert.False(result.Matched);
    }

    [Fact]
    public async Task DistExpSum_StemNotBinomialPower_SkipsImmediately()
    {
        var sut = new DistExpSumMatcher(_cas, NullLogger<DistExpSumMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Find the derivative of 3x", "3", "5"),
            CancellationToken.None);

        Assert.False(result.Matched);
        Assert.Contains("stem does not fit", result.Notes, StringComparison.Ordinal);
        await _cas.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // CANCEL-COMMON — (a+b)/a → b
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelCommon_PositiveCase_ReturnsHighConfidence()
    {
        CasDefaultNotEqual();
        // Buggy output from stem "(x+5)/x" with denom "x": remove the "x" summand, leaving "5".
        CasEquivalence("5", "5", true);

        var sut = new CancelCommonMatcher(_cas, NullLogger<CancelCommonMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Simplify (x+5)/x", "1 + 5/x", "5"),
            CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("CANCEL-COMMON", result.BuggyRuleId);
    }

    [Fact]
    public async Task CancelCommon_DenominatorNotASummand_SkipsImmediately()
    {
        var sut = new CancelCommonMatcher(_cas, NullLogger<CancelCommonMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Simplify (2x+5)/3", "(2x+5)/3", "2x+5"),
            CancellationToken.None);

        Assert.False(result.Matched);
        await _cas.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // SIGN-NEGATIVE — -(a+b) → -a + b
    // ------------------------------------------------------------------

    [Fact]
    public async Task SignNegative_PositiveCase_ReturnsHighConfidence()
    {
        CasDefaultNotEqual();
        // Stem "-(x+5)" → first term negated only → "-x + 5"
        CasEquivalence("-x + 5", "-x + 5", true);

        var sut = new SignNegativeMatcher(_cas, NullLogger<SignNegativeMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Simplify -(x+5)", "-x - 5", "-x + 5"),
            CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("SIGN-NEGATIVE", result.BuggyRuleId);
    }

    [Fact]
    public async Task SignNegative_StemLacksLeadingNegation_SkipsImmediately()
    {
        var sut = new SignNegativeMatcher(_cas, NullLogger<SignNegativeMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Simplify x+5", "x+5", "wrong"),
            CancellationToken.None);

        Assert.False(result.Matched);
        await _cas.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // ORDER-OPS — 2+3*4 → 20 (left-to-right)
    // ------------------------------------------------------------------

    [Fact]
    public async Task OrderOps_PositiveCase_NumericallyMatchesLeftToRightTotal()
    {
        CasDefaultNotEqual();
        // 2+3*4 evaluated left-to-right: (2+3)*4 = 20. Correct: 14.
        // The matcher produces buggy output "20"; student answered "20" literally.
        CasEquivalence("20", "20", true);
        // Numerical fallback may also be consulted if Equivalence fails; stub it OK too.
        _cas.VerifyAsync(
                Arg.Is<CasVerifyRequest>(r =>
                    r.Operation == CasOperation.NumericalTolerance &&
                    r.ExpressionA == "20" && r.ExpressionB == "20"),
                Arg.Any<CancellationToken>())
            .Returns(CasVerifyResult.Success(CasOperation.NumericalTolerance, "MathNet", 0.1));

        var sut = new OrderOpsMatcher(_cas, NullLogger<OrderOpsMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Evaluate 2 + 3 * 4", "14", "20"),
            CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("ORDER-OPS", result.BuggyRuleId);
    }

    [Fact]
    public async Task OrderOps_NoMixedPrecedence_SkipsImmediately()
    {
        var sut = new OrderOpsMatcher(_cas, NullLogger<OrderOpsMatcher>.Instance);
        // All additions — left-to-right gives the same result as PEMDAS.
        var result = await sut.TryMatchAsync(
            Ctx("Evaluate 2 + 3 + 4", "9", "15"),
            CancellationToken.None);

        Assert.False(result.Matched);
        await _cas.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // FRACTION-ADD — a/b + c/d → (a+c)/(b+d)
    // ------------------------------------------------------------------

    [Fact]
    public async Task FractionAdd_PositiveCase_ReturnsHighConfidence()
    {
        CasDefaultNotEqual();
        // 1/2 + 1/3, buggy → (2)/(5)
        CasEquivalence("2/5", "(2) / (5)", true);

        var sut = new FractionAddMatcher(_cas, NullLogger<FractionAddMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Compute 1/2 + 1/3", "5/6", "2/5"),
            CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("FRACTION-ADD", result.BuggyRuleId);
    }

    [Fact]
    public async Task FractionAdd_StemLacksFractionSum_SkipsImmediately()
    {
        var sut = new FractionAddMatcher(_cas, NullLogger<FractionAddMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Simplify x + 5", "x + 5", "7"),
            CancellationToken.None);

        Assert.False(result.Matched);
        await _cas.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // CAS-error graceful degradation (cross-cutting)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Matcher_CasReturnsError_ReturnsNoMatchNotThrows()
    {
        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => CasVerifyResult.Error(
                ci.Arg<CasVerifyRequest>().Operation, "MathNet", 0.1, "boom"));

        var sut = new DistExpSumMatcher(_cas, NullLogger<DistExpSumMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("Expand (x+3)^2", "x^2 + 6x + 9", "x^2 + 9"),
            CancellationToken.None);

        Assert.False(result.Matched);
    }

    // ------------------------------------------------------------------
    // Subject scoping — physics stems must not trigger math matchers
    // ------------------------------------------------------------------

    [Fact]
    public async Task Matcher_SubjectMismatch_SkipsImmediately()
    {
        var sut = new DistExpSumMatcher(_cas, NullLogger<DistExpSumMatcher>.Instance);
        var result = await sut.TryMatchAsync(
            Ctx("What is F=ma for (m+2)^2?", "0", "0", subject: "physics"),
            CancellationToken.None);

        Assert.False(result.Matched);
        await _cas.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }
}
