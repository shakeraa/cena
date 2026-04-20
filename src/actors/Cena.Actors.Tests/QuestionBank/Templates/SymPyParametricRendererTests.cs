// =============================================================================
// Cena Platform — SymPyParametricRenderer tests (prr-200)
//
// Uses a stubbed ICasRouterService to exercise the render → CAS → shape gate
// pipeline WITHOUT the NATS sidecar. We verify:
//   * CAS-contradicted → RejectedCasContradicted
//   * Circuit-open → RejectedCasUnavailable
//   * Non-integer canonical on integer-only template → RejectedDisallowedShape
//   * Literal /0 short-circuits before CAS
//   * Distractor equal to canonical answer is dropped
//   * Happy path returns Accepted with populated variant
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.QuestionBank.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class SymPyParametricRendererTests
{
    private static ParametricTemplate Template(AcceptShape shapes = AcceptShape.Any) => new()
    {
        Id = "t1", Version = 1,
        Subject = "math", Topic = "algebra",
        Track = TemplateTrack.FourUnit,
        Difficulty = TemplateDifficulty.Easy,
        Methodology = TemplateMethodology.Halabi,
        StemTemplate = "Compute {a} + {b}",
        SolutionExpr = "a + b",
        VariableName = "x",
        AcceptShapes = shapes,
        Slots = new[]
        {
            new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 5 },
            new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 5 }
        }
    };

    private static IReadOnlyList<ParametricSlotValue> Slots(int a, int b) => new[]
    {
        ParametricSlotValue.Integer("a", a),
        ParametricSlotValue.Integer("b", b)
    };

    [Fact]
    public async Task Render_HappyPath_ReturnsAccepted()
    {
        var router = Substitute.For<ICasRouterService>();
        router.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(CasVerifyResult.Success(
                CasOperation.NormalForm, "SymPy", latencyMs: 5, simplifiedA: "5")));

        var renderer = new SymPyParametricRenderer(router, NullLogger<SymPyParametricRenderer>.Instance);
        var r = await renderer.RenderAsync(Template(), seed: 1, Slots(2, 3));

        Assert.Equal(RendererVerdict.Accepted, r.Verdict);
        Assert.NotNull(r.Variant);
        Assert.Equal("Compute 2 + 3", r.Variant!.RenderedStem);
        Assert.Equal("5", r.Variant.CanonicalAnswer);
    }

    [Fact]
    public async Task Render_CasContradicted_Rejects()
    {
        var router = Substitute.For<ICasRouterService>();
        router.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CasVerifyResult.Failure(
                CasOperation.NormalForm, "SymPy", 2, "cannot simplify")));

        var renderer = new SymPyParametricRenderer(router, NullLogger<SymPyParametricRenderer>.Instance);
        var r = await renderer.RenderAsync(Template(), seed: 2, Slots(1, 2));
        Assert.Equal(RendererVerdict.RejectedCasContradicted, r.Verdict);
    }

    [Fact]
    public async Task Render_CircuitOpen_Rejects()
    {
        var router = Substitute.For<ICasRouterService>();
        router.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CasVerifyResult.Error(
                CasOperation.NormalForm, "SymPy", 0, "circuit open",
                CasVerifyStatus.CircuitBreakerOpen)));

        var renderer = new SymPyParametricRenderer(router, NullLogger<SymPyParametricRenderer>.Instance);
        var r = await renderer.RenderAsync(Template(), seed: 3, Slots(1, 2));
        Assert.Equal(RendererVerdict.RejectedCasUnavailable, r.Verdict);
    }

    [Fact]
    public async Task Render_IntegerOnly_RejectsRationalCanonical()
    {
        var router = Substitute.For<ICasRouterService>();
        router.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CasVerifyResult.Success(
                CasOperation.NormalForm, "SymPy", 2, simplifiedA: "7/3")));

        var renderer = new SymPyParametricRenderer(router, NullLogger<SymPyParametricRenderer>.Instance);
        var r = await renderer.RenderAsync(
            Template(AcceptShape.Integer), seed: 4, Slots(2, 3));
        Assert.Equal(RendererVerdict.RejectedDisallowedShape, r.Verdict);
    }

    [Fact]
    public async Task Render_LiteralZeroDivisor_ShortCircuitsBeforeCas()
    {
        // Solution expression 1/(a-b) where a == b will literally substitute to
        // "1 / (3-3)" — the pre-CAS screen catches computed /0 only for
        // literal "/0" patterns; to exercise the screen we use solutionExpr "a/0".
        var template = Template() with { SolutionExpr = "a/0" };

        var router = Substitute.For<ICasRouterService>();
        var renderer = new SymPyParametricRenderer(router, NullLogger<SymPyParametricRenderer>.Instance);
        var r = await renderer.RenderAsync(template, seed: 5, Slots(2, 3));

        Assert.Equal(RendererVerdict.RejectedZeroDivisor, r.Verdict);
        await router.DidNotReceive().VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Render_DropsDistractorEqualToCanonicalAnswer()
    {
        var router = Substitute.For<ICasRouterService>();
        // Both solution and distractor normalise to "5" — the distractor must
        // be elided from the output.
        router.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CasVerifyResult.Success(
                CasOperation.NormalForm, "SymPy", 1, simplifiedA: "5")));

        var template = Template() with
        {
            DistractorRules = new[]
            {
                new DistractorRule("CANCEL-COMMON", "a + b", "same as answer — must be dropped")
            }
        };

        var renderer = new SymPyParametricRenderer(router, NullLogger<SymPyParametricRenderer>.Instance);
        var r = await renderer.RenderAsync(template, seed: 6, Slots(2, 3));

        Assert.Equal(RendererVerdict.Accepted, r.Verdict);
        Assert.NotNull(r.Variant);
        Assert.Empty(r.Variant!.Distractors);
    }
}
