// =============================================================================
// Cena Platform — CasConformanceSuiteRunner Tests (RDY-036 §7 / RDY-037)
//
// Verifies the runner logic (filtering, aggregation, threshold, per-pair
// error handling) using substituted engines. An integration test that
// exercises the real SymPy sidecar ships separately and runs nightly.
// =============================================================================

using Cena.Actors.Cas;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class CasConformanceSuiteRunnerTests
{
    private readonly IMathNetVerifier _mathNet = Substitute.For<IMathNetVerifier>();
    private readonly ISymPySidecarClient _sympy = Substitute.For<ISymPySidecarClient>();

    private CasConformanceSuiteRunner Sut() =>
        new(_mathNet, _sympy, NullLogger<CasConformanceSuiteRunner>.Instance);

    private static CasVerifyResult Ok(bool verified, string engine, string? simplifiedA = null) =>
        new(verified, CasOperation.Equivalence, engine, simplifiedA, null, null, 5, CasVerifyStatus.Ok);

    // ── Filtering ────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceholderPairs_AreExcluded_FromRun()
    {
        // Return "both agree" for every call so placeholder exclusion is
        // visible as a total-count difference.
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(Ok(true, "MathNet"));
        _sympy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
              .Returns(Ok(true, "SymPy"));

        var result = await Sut().RunAsync();

        var totalPairs = ConformancePairs.All.Count;
        var nonPlaceholder = ConformancePairs.All.Count(p => p.Category != "placeholder");
        Assert.Equal(nonPlaceholder, result.TotalPairs);
        Assert.True(result.TotalPairs < totalPairs,
            $"Expected placeholders excluded; ran {result.TotalPairs} of {totalPairs}");
    }

    [Fact]
    public async Task RunCategoryAsync_OnlyRunsMatchingCategory()
    {
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(Ok(true, "MathNet"));
        _sympy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
              .Returns(Ok(true, "SymPy"));

        var result = await Sut().RunCategoryAsync("trig");

        var expected = ConformancePairs.All.Count(p => p.Category == "trig");
        Assert.Equal(expected, result.TotalPairs);
        Assert.True(result.TotalPairs > 0, "Expected at least one trig pair in corpus");
    }

    // ── Aggregation + threshold ──────────────────────────────────────

    [Fact]
    public async Task BothEnginesAgreeAlways_ProducesFullAgreement()
    {
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(Ok(true, "MathNet"));
        _sympy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
              .Returns(Ok(true, "SymPy"));

        var result = await Sut().RunAsync();

        Assert.Equal(result.TotalPairs, result.AgreementCount);
        Assert.Equal(1.0, result.AgreementRate);
        Assert.Empty(result.Disagreements);
        Assert.True(result.PassesThreshold);
    }

    [Fact]
    public async Task EnginesDisagreeOnHalf_ProducesFiftyPercent_BelowThreshold()
    {
        // Toggle SymPy: false, true, false, true... so agreement ≈ 50%.
        int call = 0;
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(Ok(true, "MathNet"));
        _sympy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
              .Returns(_ => Ok(call++ % 2 == 0, "SymPy"));

        var result = await Sut().RunAsync();

        Assert.InRange(result.AgreementRate, 0.45, 0.55);
        Assert.False(result.PassesThreshold);
        Assert.NotEmpty(result.Disagreements);
    }

    [Fact]
    public async Task SymPyThrows_OnePair_IsRecordedAsDisagreement_DoesNotAbort()
    {
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(Ok(true, "MathNet"));
        int call = 0;
        _sympy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
              .Returns(_ =>
              {
                  if (call++ == 0) throw new InvalidOperationException("sidecar 500");
                  return Ok(true, "SymPy");
              });

        var result = await Sut().RunAsync();

        // Run completes; the single failing pair is a disagreement (SymPy=false vs MathNet=true).
        Assert.True(result.TotalPairs > 1);
        Assert.True(result.AgreementCount == result.TotalPairs - 1,
            $"Expected exactly 1 disagreement, got {result.DisagreementCount}");
        Assert.Contains(result.Disagreements, d => d.SymPyError != null);
    }

    // ── Threshold boundary ────────────────────────────────────────────

    [Fact]
    public void PassesThreshold_Boundary_IsInclusiveAtNinetyNinePercent()
    {
        var atExactly99 = new ConformanceSuiteResult { TotalPairs = 100, AgreementCount = 99 };
        Assert.True(atExactly99.PassesThreshold);

        var just_below = new ConformanceSuiteResult { TotalPairs = 100, AgreementCount = 98 };
        Assert.False(just_below.PassesThreshold);
    }

    // ── Cancellation ──────────────────────────────────────────────────

    [Fact]
    public async Task CancellationToken_StopsRunEarly()
    {
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(Ok(true, "MathNet"));
        _sympy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
              .Returns(Ok(true, "SymPy"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Sut().RunAsync(cts.Token));
    }
}
