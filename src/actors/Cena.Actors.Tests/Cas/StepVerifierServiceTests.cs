// =============================================================================
// PP-009: Step Verifier — Hint Sanitization Tests
// =============================================================================

using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Cas;

public class StepVerifierServiceTests
{
    private readonly ICasRouterService _cas = Substitute.For<ICasRouterService>();
    private readonly ILogger<StepVerifierService> _logger = Substitute.For<ILogger<StepVerifierService>>();

    private StepVerifierService CreateSut() => new(_cas, _logger);

    // =========================================================================
    // PP-009: Hints must not leak canonical answer or intermediate expressions
    // =========================================================================

    [Fact]
    public async Task VerifyStepAsync_InvalidStep_HintDoesNotContainCanonicalAnswer()
    {
        // Canonical trace: solve 2x = 6 → x = 3
        var canonical = new CanonicalTrace(
            ProblemExpression: "2*x = 6",
            FinalAnswer: "3",
            Steps: new[]
            {
                new SolutionStep(1, "x = 6/2", "divide both sides by 2", null),
                new SolutionStep(2, "x = 3", "simplify", null)
            });

        // Student gives wrong step
        var studentStep = new SolutionStep(1, "x = 6 + 2", null, null);

        // CAS says step is invalid
        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(CasVerifyResult.Failure(CasOperation.StepValidity, "MathNet", 0.1,
                "Step does not preserve equality"));

        var sut = CreateSut();
        var result = await sut.VerifyStepAsync(studentStep, null, canonical);

        Assert.False(result.IsValid);
        // The hint must NOT contain the answer "3" or the expression "x = 3"
        if (result.SuggestedNextStep is not null)
        {
            Assert.DoesNotContain("3", result.SuggestedNextStep);
            Assert.DoesNotContain("x = 3", result.SuggestedNextStep);
            Assert.DoesNotContain("6/2", result.SuggestedNextStep);
        }
    }

    [Fact]
    public async Task VerifyStepAsync_InvalidStep_ReturnsOnlyOperationName()
    {
        var canonical = new CanonicalTrace(
            ProblemExpression: "x^2 - 4 = 0",
            FinalAnswer: "2",
            Steps: new[]
            {
                new SolutionStep(1, "(x-2)(x+2) = 0", "factor", null),
                new SolutionStep(2, "x = 2", "solve", null)
            });

        var studentStep = new SolutionStep(1, "x^2 = 4", null, null);

        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(CasVerifyResult.Failure(CasOperation.StepValidity, "MathNet", 0.1, "wrong"));

        var sut = CreateSut();
        var result = await sut.VerifyStepAsync(studentStep, null, canonical);

        // Should return "factor" (the operation), not the expression or justification
        Assert.Equal("factor", result.SuggestedNextStep);
    }

    [Fact]
    public async Task VerifyStepAsync_NoOperation_ReturnsNullHint()
    {
        var canonical = new CanonicalTrace(
            ProblemExpression: "2*x = 6",
            FinalAnswer: "3",
            Steps: new[]
            {
                new SolutionStep(1, "x = 3", null, "Divide both sides by 2 to get x = 3")
            });

        var studentStep = new SolutionStep(1, "x = 6 + 2", null, null);

        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(CasVerifyResult.Failure(CasOperation.StepValidity, "MathNet", 0.1, "wrong"));

        var sut = CreateSut();
        var result = await sut.VerifyStepAsync(studentStep, null, canonical);

        // Operation is null → hint is null (justification is NOT returned)
        Assert.Null(result.SuggestedNextStep);
    }

    [Fact]
    public async Task VerifyStepAsync_OperationContainsAnswer_AnswerIsSanitized()
    {
        // Edge case: poorly authored operation that includes the answer
        var canonical = new CanonicalTrace(
            ProblemExpression: "x + 5 = 8",
            FinalAnswer: "3",
            Steps: new[]
            {
                new SolutionStep(1, "x = 3", "subtract 5 to get 3", null)
            });

        var studentStep = new SolutionStep(1, "x = 8 + 5", null, null);

        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(CasVerifyResult.Failure(CasOperation.StepValidity, "MathNet", 0.1, "wrong"));

        var sut = CreateSut();
        var result = await sut.VerifyStepAsync(studentStep, null, canonical);

        // The answer "3" should be replaced with "[answer]"
        Assert.NotNull(result.SuggestedNextStep);
        Assert.DoesNotContain("3", result.SuggestedNextStep);
        Assert.Contains("[answer]", result.SuggestedNextStep);
    }
}
