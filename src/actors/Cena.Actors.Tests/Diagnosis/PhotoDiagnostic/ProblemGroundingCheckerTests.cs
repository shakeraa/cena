// =============================================================================
// Cena Platform — ProblemGroundingChecker tests (EPIC-PRR-J PRR-353, ADR-0002)
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class ProblemGroundingCheckerTests
{
    [Fact]
    public async Task Matching_problem_accepts()
    {
        var router = new FakeCasRouter(
            CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 10));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync(
            posedExpressionLatex: "x^2 + 3x + 2",
            extractedStep0Latex: "(x+1)(x+2)",
            ct: CancellationToken.None);

        Assert.Equal(GroundingDecision.Accept, result.Decision);
        Assert.Null(result.RejectReason);
        Assert.NotNull(result.CasResult);
    }

    [Fact]
    public async Task Mismatched_problem_rejects_with_stable_code()
    {
        // Router returns Ok + Verified=false — the expressions aren't
        // equivalent (SymPy says so).
        var router = new FakeCasRouter(
            CasVerifyResult.Failure(CasOperation.Equivalence, "SymPy", 12, "not equal"));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync(
            "x^2 + 3x + 2",
            "3y - 1",   // completely unrelated
            CancellationToken.None);

        Assert.Equal(GroundingDecision.Reject, result.Decision);
        Assert.Equal(
            ProblemGroundingChecker.RejectCodeNotEquivalent,
            result.RejectReason);
    }

    [Fact]
    public async Task Equivalent_restatement_accepts()
    {
        // Student factored on paper. SymPy says these ARE equivalent.
        var router = new FakeCasRouter(
            CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 10));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync(
            "2x^2 - 8",
            "2(x-2)(x+2)",
            CancellationToken.None);

        Assert.Equal(GroundingDecision.Accept, result.Decision);
    }

    [Fact]
    public async Task Cas_router_error_returns_Undetermined_not_Reject()
    {
        // ADR-0002 SymPy-is-the-oracle discipline: when the oracle is
        // unavailable, we DO NOT reject the student's upload — we record
        // the gap and let the full chain verifier run. Falsely rejecting
        // during a SymPy outage would accuse real students of abuse
        // during an infra incident (memory "Honest not complimentary").
        var router = new FakeCasRouter(
            CasVerifyResult.Error(CasOperation.Equivalence, "SymPy", 5000, "timeout"));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync(
            "x + 1", "x + 1", CancellationToken.None);

        Assert.Equal(GroundingDecision.Undetermined, result.Decision);
        Assert.Null(result.RejectReason);
    }

    [Fact]
    public async Task Circuit_breaker_open_returns_Undetermined()
    {
        var router = new FakeCasRouter(
            new CasVerifyResult(
                Verified: false,
                Operation: CasOperation.Equivalence,
                Engine: "SymPy",
                SimplifiedA: null,
                SimplifiedB: null,
                ErrorMessage: "breaker open",
                LatencyMs: 0,
                Status: CasVerifyStatus.CircuitBreakerOpen));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync(
            "x", "x", CancellationToken.None);

        Assert.Equal(GroundingDecision.Undetermined, result.Decision);
    }

    [Fact]
    public async Task Empty_posed_expression_rejects_with_empty_input_code()
    {
        // Empty-input path never calls the router — the reject code is
        // separable so UI can render a different message.
        var router = new FakeCasRouter(
            CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 10));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync("", "x + 1", CancellationToken.None);

        Assert.Equal(GroundingDecision.Reject, result.Decision);
        Assert.Equal(
            ProblemGroundingChecker.RejectCodeEmptyInput,
            result.RejectReason);
        Assert.Null(result.CasResult);   // router was not called
    }

    [Fact]
    public async Task Empty_extracted_step_rejects_with_empty_input_code()
    {
        var router = new FakeCasRouter(
            CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 10));
        var checker = new ProblemGroundingChecker(router);

        var result = await checker.CheckAsync(
            "x + 1", "   ", CancellationToken.None);

        Assert.Equal(GroundingDecision.Reject, result.Decision);
        Assert.Equal(
            ProblemGroundingChecker.RejectCodeEmptyInput,
            result.RejectReason);
    }

    [Fact]
    public async Task Router_is_called_with_posed_as_A_and_extracted_as_B()
    {
        // Lock the argument order. A flip would make the test "student's
        // step is equivalent to the posed expression" pass correctly, but
        // the router's SimplifiedA/B side-output would be swapped in logs
        // — observability-only bug, but worth catching.
        var router = new FakeCasRouter(
            CasVerifyResult.Success(CasOperation.Equivalence, "SymPy", 10));
        var checker = new ProblemGroundingChecker(router);

        await checker.CheckAsync(
            posedExpressionLatex: "POSED",
            extractedStep0Latex: "EXTRACTED",
            ct: CancellationToken.None);

        Assert.NotNull(router.LastRequest);
        Assert.Equal("POSED", router.LastRequest!.ExpressionA);
        Assert.Equal("EXTRACTED", router.LastRequest.ExpressionB);
        Assert.Equal(CasOperation.Equivalence, router.LastRequest.Operation);
    }

    [Fact]
    public void Constructor_rejects_null_router()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ProblemGroundingChecker(null!));
    }

    private sealed class FakeCasRouter : ICasRouterService
    {
        private readonly CasVerifyResult _fixed;
        public CasVerifyRequest? LastRequest { get; private set; }

        public FakeCasRouter(CasVerifyResult fixedResult)
        {
            _fixed = fixedResult;
        }

        public Task<CasVerifyResult> VerifyAsync(
            CasVerifyRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(_fixed);
        }
    }
}
