// =============================================================================
// Cena Platform — Layer5CasValidation tests
//
// Covers:
//   - Parsed result → SympyParsed=true with canonical form
//   - Failed parse → SympyParsed=false, CanonicalForm=null
//   - Empty LaTeX (nullable / whitespace) → failed without calling validator
//   - Validator throws on one block → that block flagged failed, rest continue
//   - Cancellation propagates
//   - Record mutation preserves bbox/confidence/is_rtl fields
// =============================================================================

using Cena.Infrastructure.Ocr.Cas;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Infrastructure.Tests.Ocr;

public class Layer5CasValidationTests
{
    private static OcrMathBlock Math(string? latex, double conf = 0.85)
        => new(latex, new BoundingBox(1, 2, 3, 4, 1), conf,
            SympyParsed: false, CanonicalForm: null);

    [Fact]
    public async Task Empty_Input_Yields_Zero_Counts()
    {
        var validator = Substitute.For<ILatexValidator>();
        var layer = new Layer5CasValidation(validator);

        var result = await layer.RunAsync(
            Array.Empty<OcrMathBlock>(),
            CancellationToken.None);

        Assert.Empty(result.MathBlocks);
        Assert.Equal(0, result.Validated);
        Assert.Equal(0, result.Failed);
        await validator.DidNotReceive().ValidateAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Parsed_Sets_SympyParsed_True_And_CanonicalForm()
    {
        var validator = Substitute.For<ILatexValidator>();
        validator.ValidateAsync("3x+5=14", Arg.Any<CancellationToken>())
            .Returns(new LatexValidationResult(
                Parsed: true, CanonicalForm: "3*x - 9"));

        var layer = new Layer5CasValidation(validator);

        var result = await layer.RunAsync(
            new[] { Math("3x+5=14", conf: 0.87) },
            CancellationToken.None);

        Assert.Equal(1, result.Validated);
        Assert.Equal(0, result.Failed);
        var block = result.MathBlocks.Single();
        Assert.True(block.SympyParsed);
        Assert.Equal("3*x - 9", block.CanonicalForm);
        // Preserves the other fields
        Assert.Equal(0.87, block.Confidence);
        Assert.Equal(1, block.Bbox!.Page);
    }

    [Fact]
    public async Task Failed_Parse_Sets_SympyParsed_False()
    {
        var validator = Substitute.For<ILatexValidator>();
        validator.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LatexValidationResult(
                Parsed: false, CanonicalForm: null,
                RejectionReason: "syntax"));

        var layer = new Layer5CasValidation(validator);

        var result = await layer.RunAsync(
            new[] { Math(@"\frac{x}{") },
            CancellationToken.None);

        Assert.Equal(0, result.Validated);
        Assert.Equal(1, result.Failed);
        Assert.False(result.MathBlocks.Single().SympyParsed);
        Assert.Null(result.MathBlocks.Single().CanonicalForm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  \r")]
    public async Task Whitespace_Latex_Is_Rejected_Without_Calling_Validator(string? latex)
    {
        var validator = Substitute.For<ILatexValidator>();
        var layer = new Layer5CasValidation(validator);

        var result = await layer.RunAsync(
            new[] { Math(latex) },
            CancellationToken.None);

        Assert.Equal(0, result.Validated);
        Assert.Equal(1, result.Failed);
        Assert.False(result.MathBlocks.Single().SympyParsed);
        await validator.DidNotReceive().ValidateAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validator_Throws_On_One_Block_Others_Still_Complete()
    {
        var validator = Substitute.For<ILatexValidator>();
        validator.ValidateAsync("ok", Arg.Any<CancellationToken>())
            .Returns(new LatexValidationResult(true, "ok-canonical"));
        validator.ValidateAsync("boom", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("transient"));
        validator.ValidateAsync("ok2", Arg.Any<CancellationToken>())
            .Returns(new LatexValidationResult(true, "ok2-canonical"));

        var layer = new Layer5CasValidation(validator);

        var result = await layer.RunAsync(
            new[] { Math("ok"), Math("boom"), Math("ok2") },
            CancellationToken.None);

        Assert.Equal(2, result.Validated);
        Assert.Equal(1, result.Failed);
        Assert.True(result.MathBlocks[0].SympyParsed);
        Assert.False(result.MathBlocks[1].SympyParsed);
        Assert.True(result.MathBlocks[2].SympyParsed);
    }

    [Fact]
    public async Task Cancellation_Before_Work_Throws()
    {
        var validator = Substitute.For<ILatexValidator>();
        var layer = new Layer5CasValidation(validator);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            layer.RunAsync(new[] { Math("x") }, new CancellationToken(true)));
    }

    [Fact]
    public async Task NullLatexValidator_Marks_Every_Block_Failed()
    {
        // Validates our fail-closed default.
        var layer = new Layer5CasValidation(new NullLatexValidator());

        var result = await layer.RunAsync(
            new[] { Math("3x+5=14"), Math(@"\sin(x)+\cos(x)") },
            CancellationToken.None);

        Assert.Equal(0, result.Validated);
        Assert.Equal(2, result.Failed);
        Assert.All(result.MathBlocks, b => Assert.False(b.SympyParsed));
    }
}
