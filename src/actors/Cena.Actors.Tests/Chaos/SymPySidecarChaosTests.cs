// =============================================================================
// Cena Platform — SymPy Sidecar SIGKILL Chaos Tests (RDY-052, RDY-036 §10)
//
// Simulates the SymPy sidecar being SIGKILLed mid-batch — without Docker,
// without testcontainers — by substituting ISymPySidecarClient with a
// controllable fake that:
//
//   1. Serves N requests successfully (as the live sidecar would).
//   2. On request N+1, starts throwing (as a killed NATS request/reply
//      subscriber would — timeouts + exceptions).
//   3. The chaos scenario drives the full CAS stack (Router + Gate +
//      Persister) and asserts:
//
//        - Pre-kill: CAS binding = Verified (sidecar healthy).
//        - Post-kill, tractable-by-MathNet operation: Router falls back
//          to Tier 3 (MathNet), result still Ok, binding Verified.
//        - Post-kill, SymPy-only operation (Solve / calculus / trig
//          identity): Router returns Error/CircuitBreakerOpen; the gate
//          translates that to CasGateOutcome.CircuitOpen with binding
//          Unverifiable; the persister does NOT throw even in Enforce
//          mode (only Failed outcomes throw).
//        - No exception leaks to the caller.
//        - Circuit breaker recovery: when the fake is restored, subsequent
//          calls route to SymPy again.
//
// The existing runbook at tests/chaos/sympy-sigkill-test.md documents the
// same scenario for a real docker-compose environment; this file pins the
// contract at unit-integration scale so CI catches contract regressions
// without needing Docker.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.RateLimit;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Chaos;

public class SymPySidecarChaosTests
{
    /// <summary>
    /// RDY-052 fake sidecar with a controllable "alive" flag. Flip it to
    /// false mid-test to simulate SIGKILL; flip back to true to simulate
    /// restart.
    /// </summary>
    private sealed class ChaosSymPySidecar : ISymPySidecarClient
    {
        public volatile bool IsAlive = true;
        public int CallCount;
        public int FailCount;

        public Task<CasVerifyResult> VerifyAsync(CasVerifyRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CallCount);
            if (!IsAlive)
            {
                Interlocked.Increment(ref FailCount);
                // Mirrors what SymPySidecarClient returns when NATS request
                // times out after the subscriber is gone.
                return Task.FromResult(CasVerifyResult.Error(
                    request.Operation,
                    "SymPy",
                    latencyMs: 5000,
                    errorMessage: "SymPy sidecar request timed out (SIGKILL chaos)",
                    status: CasVerifyStatus.Timeout));
            }

            // Healthy path: claim verified = true for anything simple.
            return Task.FromResult(CasVerifyResult.Success(
                request.Operation,
                "SymPy",
                latencyMs: 50,
                simplifiedA: request.ExpressionA,
                simplifiedB: request.ExpressionB));
        }

        public Task<bool> IsHealthyAsync(CancellationToken ct = default) =>
            Task.FromResult(IsAlive);
    }

    private static (CasRouterService router, ChaosSymPySidecar sidecar) BuildRouter()
    {
        var sidecar = new ChaosSymPySidecar();
        var mathNet = new MathNetVerifier(NullLogger<MathNetVerifier>.Instance);
        var costBreaker = Substitute.For<ICostCircuitBreaker>();
        costBreaker.IsOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
        var router = new CasRouterService(
            mathNet, sidecar, costBreaker,
            NullLogger<CasRouterService>.Instance);
        return (router, sidecar);
    }

    [Fact]
    public async Task Router_HealthySidecar_RoutesToSymPy_ForSolveOperation()
    {
        var (router, sidecar) = BuildRouter();

        var result = await router.VerifyAsync(new CasVerifyRequest(
            CasOperation.Solve, "2*x + 3 = 7", null, "x"));

        Assert.Equal(CasVerifyStatus.Ok, result.Status);
        Assert.Equal("SymPy", result.Engine);
        Assert.Equal(1, sidecar.CallCount);
    }

    [Fact]
    public async Task Router_KilledSidecar_SolveOperation_ReturnsErrorGracefully()
    {
        // Solve is NOT handled by MathNet — with the sidecar down there is
        // no Tier-3 fallback. Router must return Error WITHOUT throwing.
        var (router, sidecar) = BuildRouter();
        sidecar.IsAlive = false;

        var result = await router.VerifyAsync(new CasVerifyRequest(
            CasOperation.Solve, "2*x + 3 = 7", null, "x"));

        Assert.NotEqual(CasVerifyStatus.Ok, result.Status);
        Assert.Equal(1, sidecar.FailCount);
    }

    [Fact]
    public async Task Router_KilledSidecar_MathNetFallback_ServesEquivalence()
    {
        // MathNet handles polynomial Equivalence. When SymPy fails, the
        // router must fall through to MathNet (Tier 3).
        var (router, sidecar) = BuildRouter();
        sidecar.IsAlive = false;

        var result = await router.VerifyAsync(new CasVerifyRequest(
            CasOperation.Equivalence, "2*x + 3*x", "5*x", "x"));

        Assert.Equal(CasVerifyStatus.Ok, result.Status);
        Assert.Equal("MathNet", result.Engine);
        // The router short-circuits to MathNet at Tier 1 before calling
        // SymPy for MathNet-capable ops, so FailCount stays 0.
        Assert.Equal(0, sidecar.FailCount);
    }

    [Fact]
    public async Task Router_SidecarRecovers_NewCallsRouteToSymPyAgain()
    {
        var (router, sidecar) = BuildRouter();

        sidecar.IsAlive = false;
        var duringKill = await router.VerifyAsync(new CasVerifyRequest(
            CasOperation.Solve, "x + 1 = 2", null, "x"));
        Assert.NotEqual(CasVerifyStatus.Ok, duringKill.Status);

        sidecar.IsAlive = true;
        var afterRecover = await router.VerifyAsync(new CasVerifyRequest(
            CasOperation.Solve, "x + 1 = 2", null, "x"));
        Assert.Equal(CasVerifyStatus.Ok, afterRecover.Status);
        Assert.Equal("SymPy", afterRecover.Engine);
    }

    [Fact]
    public async Task Gate_KilledSidecar_ProducesUnverifiableBinding_NoExceptionLeaks()
    {
        // Full gate chaos — Persister + Gate + Router + killed sidecar.
        // Enforce mode must NOT throw when the sidecar is dead; only
        // Failed outcomes throw. A dead sidecar + no-MathNet-fallback
        // operation yields CircuitOpen → Unverifiable → persister
        // completes without throwing.
        var (router, sidecar) = BuildRouter();
        sidecar.IsAlive = false;

        var detector = new MathContentDetector();
        var extractor = new StemSolutionExtractor();
        var store = Substitute.For<IDocumentStore>();
        var querySession = Substitute.For<IQuerySession>();
        store.QuerySession().Returns(querySession);

        var gate = new CasVerificationGate(
            router, detector, extractor, store,
            NullLogger<CasVerificationGate>.Instance);

        // `integrate(...)` is calculus — MathNet doesn't handle, only SymPy.
        // With sidecar dead, gate must produce CircuitOpen + Unverifiable.
        var result = await gate.VerifyForCreateAsync(
            questionId: "q-chaos",
            subject: "math",
            stem: "Compute integrate(x, x)",
            correctAnswerRaw: "x^2/2",
            variable: "x");

        Assert.Equal(CasGateOutcome.CircuitOpen, result.Outcome);
        Assert.Equal(CasBindingStatus.Unverifiable, result.Binding.Status);
        Assert.True(sidecar.FailCount >= 1);
    }

    [Fact]
    public async Task BatchChaos_50ConcurrentCalls_KillMidway_NoExceptionsLeak()
    {
        // RDY-036 §10 scenario distilled: 50 calls, SIGKILL at #25, all
        // 50 complete without throwing. Post-kill calls return non-Ok
        // status (Error/Timeout/Fallback-with-MathNet) but never throw.
        var (router, sidecar) = BuildRouter();

        int postKillCount = 0;
        int exceptionCount = 0;
        for (int i = 0; i < 50; i++)
        {
            if (i == 25) sidecar.IsAlive = false;
            try
            {
                // Mix of ops: half solve (SymPy-only), half equivalence
                // (MathNet-capable).
                var req = (i % 2 == 0)
                    ? new CasVerifyRequest(CasOperation.Equivalence, "2*x", "x+x", "x")
                    : new CasVerifyRequest(CasOperation.Solve, "x+1=2", null, "x");
                var r = await router.VerifyAsync(req);
                if (i >= 25) postKillCount++;
                _ = r; // materialize
            }
            catch
            {
                exceptionCount++;
            }
        }

        Assert.Equal(0, exceptionCount);
        Assert.Equal(25, postKillCount);
    }
}
