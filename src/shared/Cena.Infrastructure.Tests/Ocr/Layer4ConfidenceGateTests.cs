// =============================================================================
// Cena Platform — Layer4ConfidenceGate tests (updated for pageBytes signature)
//
// Covers:
//   - Pass-through when all blocks ≥ τ
//   - Math rescue via IMathpixRunner (with real page-crop bytes via ImageSharp)
//   - Text rescue via IGeminiVisionRunner
//   - Tolerated failures: OcrCircuitOpenException → pass-through (not thrown)
//   - Generic runner exception → pass-through (not thrown)
//   - Cancellation propagates
//   - Catastrophic threshold differs between Surface A and Surface B
//   - Empty inputs handled cleanly
//   - Rescue skipped when bbox is null or page index out of range
// =============================================================================

using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.Runners;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Cena.Infrastructure.Tests.Ocr;

public class Layer4ConfidenceGateTests
{
    private static readonly ConfidenceGateOptions DefaultOptions = new()
    {
        ConfidenceThreshold = 0.65,
        StudentCatastrophicThreshold = 0.30,
        AdminCatastrophicThreshold = 0.40,
    };

    private static OcrTextBlock Text(double conf, string body = "hello", bool rtl = false, int page = 1)
        => new(body, new BoundingBox(0, 0, 100, 20, page),
            rtl ? Language.Hebrew : Language.English, conf, IsRtl: rtl);

    private static OcrMathBlock Math(double conf, string latex = "3x+5=14", int page = 1)
        => new(latex, new BoundingBox(0, 0, 100, 30, page), conf,
            SympyParsed: false, CanonicalForm: null);

    /// <summary>
    /// Builds a single 200×200 white PNG page so Layer 4's ImageSharp cropper
    /// has real bytes to slice. Block bboxes in these tests fit inside 200×200.
    /// </summary>
    private static IReadOnlyList<byte[]> OnePage()
    {
        using var img = new Image<Rgba32>(200, 200, Color.White);
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return new[] { ms.ToArray() };
    }

    [Fact]
    public async Task All_High_Confidence_No_Rescue_No_Catastrophic()
    {
        var gate = new Layer4ConfidenceGate(DefaultOptions);
        var result = await gate.RunAsync(
            OnePage(),
            new[] { Text(0.9), Text(0.85) },
            new[] { Math(0.88) },
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.Empty(result.FallbacksFired);
        Assert.False(result.CatastrophicFailure);
        Assert.InRange(result.AverageConfidence, 0.87, 0.88);
    }

    [Fact]
    public async Task Math_Below_Tau_Rescued_By_Mathpix_With_Real_Crop_Bytes()
    {
        var mathpix = Substitute.For<IMathpixRunner>();
        mathpix.RescueMathAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<OcrMathBlock>(),
                Arg.Any<CancellationToken>())
            .Returns(Math(conf: 0.95));

        var gate = new Layer4ConfidenceGate(DefaultOptions, mathpix: mathpix);

        var result = await gate.RunAsync(
            OnePage(),
            Array.Empty<OcrTextBlock>(),
            new[] { Math(conf: 0.40) },
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.Single(result.FallbacksFired, f => f.StartsWith("mathpix:"));
        Assert.Equal(0.95, result.MathBlocks[0].Confidence);

        // Verify Layer 4 actually produced non-empty crop bytes for the rescue.
        await mathpix.Received(1).RescueMathAsync(
            Arg.Is<ReadOnlyMemory<byte>>(b => !b.IsEmpty),
            Arg.Any<OcrMathBlock>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Text_Below_Tau_Rescued_By_Gemini()
    {
        var gemini = Substitute.For<IGeminiVisionRunner>();
        gemini.RescueTextAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<OcrTextBlock>(),
                Arg.Any<CancellationToken>())
            .Returns(Text(0.93, body: "rescued"));

        var gate = new Layer4ConfidenceGate(DefaultOptions, gemini: gemini);

        var result = await gate.RunAsync(
            OnePage(),
            new[] { Text(0.45, body: "garbled") },
            Array.Empty<OcrMathBlock>(),
            CascadeSurface.AdminBatch,
            CancellationToken.None);

        Assert.Single(result.FallbacksFired, f => f.StartsWith("gemini:"));
        Assert.Equal("rescued", result.TextBlocks[0].Text);
        Assert.Equal(0.93, result.TextBlocks[0].Confidence);
    }

    [Fact]
    public async Task No_Runner_Registered_Leaves_Low_Conf_Blocks_Alone()
    {
        var gate = new Layer4ConfidenceGate(DefaultOptions);

        var result = await gate.RunAsync(
            OnePage(),
            new[] { Text(0.40) },
            new[] { Math(0.40) },
            CascadeSurface.AdminBatch,
            CancellationToken.None);

        Assert.Empty(result.FallbacksFired);
        Assert.Equal(0.40, result.TextBlocks[0].Confidence);
        Assert.Equal(0.40, result.MathBlocks[0].Confidence);
        Assert.False(result.CatastrophicFailure);
    }

    [Fact]
    public async Task Mathpix_Circuit_Open_Is_Tolerated_Not_Propagated()
    {
        var mathpix = Substitute.For<IMathpixRunner>();
        mathpix.RescueMathAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<OcrMathBlock>(),
                Arg.Any<CancellationToken>())
            .Throws(new OcrCircuitOpenException());

        var gate = new Layer4ConfidenceGate(DefaultOptions, mathpix: mathpix);

        var original = Math(0.40);
        var result = await gate.RunAsync(
            OnePage(),
            Array.Empty<OcrTextBlock>(),
            new[] { original },
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.Empty(result.FallbacksFired);
        Assert.Equal(0.40, result.MathBlocks[0].Confidence);
        Assert.Equal(original.Latex, result.MathBlocks[0].Latex);
    }

    [Fact]
    public async Task Generic_Runner_Exception_Is_Tolerated()
    {
        var gemini = Substitute.For<IGeminiVisionRunner>();
        gemini.RescueTextAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<OcrTextBlock>(),
                Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("transient"));

        var gate = new Layer4ConfidenceGate(DefaultOptions, gemini: gemini);

        var original = Text(0.30);
        var result = await gate.RunAsync(
            OnePage(),
            new[] { original },
            Array.Empty<OcrMathBlock>(),
            CascadeSurface.AdminBatch,
            CancellationToken.None);

        Assert.Empty(result.FallbacksFired);
        Assert.Equal(original.Text, result.TextBlocks[0].Text);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var mathpix = Substitute.For<IMathpixRunner>();
        mathpix.RescueMathAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<OcrMathBlock>(),
                Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        var gate = new Layer4ConfidenceGate(DefaultOptions, mathpix: mathpix);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            gate.RunAsync(
                OnePage(),
                Array.Empty<OcrTextBlock>(),
                new[] { Math(0.30) },
                CascadeSurface.StudentInteractive,
                new CancellationToken(true)));
    }

    [Fact]
    public async Task Student_Surface_Catastrophic_Below_0_30()
    {
        var gate = new Layer4ConfidenceGate(DefaultOptions);

        var result = await gate.RunAsync(
            OnePage(),
            new[] { Text(0.15), Text(0.25) },
            Array.Empty<OcrMathBlock>(),
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.True(result.CatastrophicFailure);
        Assert.True(result.AverageConfidence < 0.30);
    }

    [Fact]
    public async Task Admin_Surface_Catastrophic_Below_0_40_Not_0_30()
    {
        var gate = new Layer4ConfidenceGate(DefaultOptions);
        var blocks = new[] { Text(0.30), Text(0.40) };
        var pages = OnePage();

        var adminResult = await gate.RunAsync(
            pages, blocks, Array.Empty<OcrMathBlock>(),
            CascadeSurface.AdminBatch, CancellationToken.None);

        var studentResult = await gate.RunAsync(
            pages, blocks, Array.Empty<OcrMathBlock>(),
            CascadeSurface.StudentInteractive, CancellationToken.None);

        Assert.True(adminResult.CatastrophicFailure);
        Assert.False(studentResult.CatastrophicFailure);
    }

    [Fact]
    public async Task Empty_Input_Returns_Zero_Avg_Catastrophic_Student()
    {
        var gate = new Layer4ConfidenceGate(DefaultOptions);
        var result = await gate.RunAsync(
            OnePage(),
            Array.Empty<OcrTextBlock>(),
            Array.Empty<OcrMathBlock>(),
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.Equal(0.0, result.AverageConfidence);
        Assert.True(result.CatastrophicFailure);
        Assert.Empty(result.FallbacksFired);
    }

    [Fact]
    public async Task High_Conf_Blocks_Skip_Runner_Entirely()
    {
        var mathpix = Substitute.For<IMathpixRunner>();
        var gate = new Layer4ConfidenceGate(DefaultOptions, mathpix: mathpix);

        await gate.RunAsync(
            OnePage(),
            Array.Empty<OcrTextBlock>(),
            new[] { Math(0.90), Math(0.85) },
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        await mathpix.DidNotReceive().RescueMathAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<OcrMathBlock>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_PageBytes_Skips_Rescue_Instead_Of_Crashing()
    {
        // When Layer 0 returns zero pages (e.g. cascade degraded mode), Layer 4
        // must cleanly skip rescue rather than throw index-out-of-range.
        var mathpix = Substitute.For<IMathpixRunner>();
        var gate = new Layer4ConfidenceGate(DefaultOptions, mathpix: mathpix);

        var result = await gate.RunAsync(
            Array.Empty<byte[]>(),
            Array.Empty<OcrTextBlock>(),
            new[] { Math(0.30) },
            CascadeSurface.AdminBatch,
            CancellationToken.None);

        Assert.Empty(result.FallbacksFired);
        Assert.Single(result.MathBlocks);
        Assert.Equal(0.30, result.MathBlocks[0].Confidence);
        await mathpix.DidNotReceive().RescueMathAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<OcrMathBlock>(),
            Arg.Any<CancellationToken>());
    }
}
