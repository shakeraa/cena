// =============================================================================
// PP-008: CAS Router Service — Typed Status Routing Tests
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.RateLimit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Cas;

public class CasRouterServiceTests
{
    private readonly IMathNetVerifier _mathNet = Substitute.For<IMathNetVerifier>();
    private readonly ISymPySidecarClient _symPy = Substitute.For<ISymPySidecarClient>();
    private readonly ICostCircuitBreaker _costBreaker = Substitute.For<ICostCircuitBreaker>();
    private readonly ILogger<CasRouterService> _logger = Substitute.For<ILogger<CasRouterService>>();

    private CasRouterService CreateSut() => new(_mathNet, _symPy, _costBreaker, _logger);

    private static CasVerifyRequest SimpleRequest =>
        new(CasOperation.Equivalence, "x+1", "1+x", null);

    // =========================================================================
    // Error status routes to fallback (PP-008 acceptance criterion)
    // =========================================================================

    [Fact]
    public async Task VerifyAsync_MathNetError_FallsThrough_ToSymPy()
    {
        _costBreaker.IsOpenAsync(Arg.Any<CancellationToken>()).Returns(false);
        _mathNet.CanHandle(Arg.Any<CasVerifyRequest>()).Returns(true);
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(
            CasVerifyResult.Error(CasOperation.Equivalence, "MathNet", 0.1, "parse failed"));

        var symPyOk = CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 50);
        _symPy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(symPyOk);

        var sut = CreateSut();
        var result = await sut.VerifyAsync(SimpleRequest);

        Assert.Equal("SymPy", result.Engine);
        Assert.Equal(CasVerifyStatus.Ok, result.Status);
        Assert.True(result.Verified);
    }

    [Fact]
    public async Task VerifyAsync_SymPyError_FallsBack_ToMathNet()
    {
        _costBreaker.IsOpenAsync(Arg.Any<CancellationToken>()).Returns(false);
        _mathNet.CanHandle(Arg.Any<CasVerifyRequest>()).Returns(false); // skip Tier 1

        _symPy.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(
            CasVerifyResult.Error(CasOperation.Equivalence, "SymPy", 100, "timeout",
                CasVerifyStatus.Timeout));

        // Now MathNet can handle on fallback
        _mathNet.CanHandle(Arg.Any<CasVerifyRequest>()).Returns(false, true);
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(
            CasVerifyResult.Success(CasOperation.Equivalence, "MathNet", 0.5));

        var sut = CreateSut();
        var result = await sut.VerifyAsync(SimpleRequest);

        Assert.Equal("MathNet", result.Engine);
        Assert.Equal(CasVerifyStatus.Ok, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_MathNetFailure_NotError_ReturnsDirectly()
    {
        // A Failure (math doesn't match) is NOT an error — should return directly, not fall through
        _costBreaker.IsOpenAsync(Arg.Any<CancellationToken>()).Returns(false);
        _mathNet.CanHandle(Arg.Any<CasVerifyRequest>()).Returns(true);
        _mathNet.Verify(Arg.Any<CasVerifyRequest>()).Returns(
            CasVerifyResult.Failure(CasOperation.Equivalence, "MathNet", 0.1, "x+1 ≠ x+2"));

        var sut = CreateSut();
        var result = await sut.VerifyAsync(SimpleRequest);

        Assert.Equal("MathNet", result.Engine);
        Assert.Equal(CasVerifyStatus.Ok, result.Status);
        Assert.False(result.Verified);
        // SymPy should NOT have been called
        await _symPy.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_CostBreakerOpen_ReturnsCircuitBreakerStatus()
    {
        _costBreaker.IsOpenAsync(Arg.Any<CancellationToken>()).Returns(true);

        var sut = CreateSut();
        var result = await sut.VerifyAsync(SimpleRequest);

        Assert.Equal(CasVerifyStatus.CircuitBreakerOpen, result.Status);
        Assert.False(result.Verified);
    }

    // =========================================================================
    // CasVerifyStatus enum values on factory methods
    // =========================================================================

    [Fact]
    public void Success_HasOkStatus()
    {
        var r = CasVerifyResult.Success(CasOperation.Equivalence, "test", 1);
        Assert.Equal(CasVerifyStatus.Ok, r.Status);
    }

    [Fact]
    public void Failure_HasOkStatus()
    {
        var r = CasVerifyResult.Failure(CasOperation.Equivalence, "test", 1, "nope");
        Assert.Equal(CasVerifyStatus.Ok, r.Status);
    }

    [Fact]
    public void Error_HasErrorStatus()
    {
        var r = CasVerifyResult.Error(CasOperation.Equivalence, "test", 1, "boom");
        Assert.Equal(CasVerifyStatus.Error, r.Status);
    }

    [Fact]
    public void Error_WithTimeout_HasTimeoutStatus()
    {
        var r = CasVerifyResult.Error(CasOperation.Equivalence, "test", 1, "timed out",
            CasVerifyStatus.Timeout);
        Assert.Equal(CasVerifyStatus.Timeout, r.Status);
    }

    [Fact]
    public void Error_NoLongerPrefixesErrorMessage()
    {
        var r = CasVerifyResult.Error(CasOperation.Equivalence, "test", 1, "something broke");
        Assert.Equal("something broke", r.ErrorMessage);
        Assert.DoesNotContain("[ERROR]", r.ErrorMessage!);
    }
}
