// =============================================================================
// Cena Platform — OcrCascadeService orchestrator tests
//
// Mocks every layer interface so the orchestration flow can be exercised
// without OpenCV / gRPC / Tesseract / Surya / pix2tex installed. Verifies:
//
//   1. Flow order: Layer 0 → Layer 1 → {2a, 2b, 2c parallel} → 3 → 4 → 5
//   2. PDF triage=encrypted short-circuits everything past Layer 0
//   3. Timings aggregate into layer_timings_seconds
//   4. Layer 4 catastrophic + Layer 5 majority-failed both mark human_review
//   5. Contracts: empty bytes throw OcrInputException; result shape matches
//      dev-fixture contract
// =============================================================================

using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Infrastructure.Tests.Ocr;

public class OcrCascadeServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────
    private static (OcrCascadeService svc,
                    IPdfTriage triage,
                    ILayer0Preprocess l0,
                    ILayer1Layout l1,
                    ILayer2aTextOcr l2a,
                    ILayer2bMathOcr l2b,
                    ILayer2cFigureExtraction l2c,
                    ILayer3Reassemble l3,
                    ILayer4ConfidenceGate l4,
                    ILayer5CasValidation l5) Build()
    {
        var triage = Substitute.For<IPdfTriage>();
        var l0 = Substitute.For<ILayer0Preprocess>();
        var l1 = Substitute.For<ILayer1Layout>();
        var l2a = Substitute.For<ILayer2aTextOcr>();
        var l2b = Substitute.For<ILayer2bMathOcr>();
        var l2c = Substitute.For<ILayer2cFigureExtraction>();
        var l3 = Substitute.For<ILayer3Reassemble>();
        var l4 = Substitute.For<ILayer4ConfidenceGate>();
        var l5 = Substitute.For<ILayer5CasValidation>();

        var svc = new OcrCascadeService(
            triage, l0, l1, l2a, l2b, l2c, l3, l4, l5,
            NullLogger<OcrCascadeService>.Instance);

        return (svc, triage, l0, l1, l2a, l2b, l2c, l3, l4, l5);
    }

    private static readonly byte[] FakeBytes = { 0x89, 0x50, 0x4E, 0x47 };  // PNG magic
    private static readonly List<byte[]> FakePages = new() { FakeBytes };

    private static void WireHappyPath(
        ILayer0Preprocess l0,
        ILayer1Layout l1,
        ILayer2aTextOcr l2a,
        ILayer2bMathOcr l2b,
        ILayer2cFigureExtraction l2c,
        ILayer3Reassemble l3,
        ILayer4ConfidenceGate l4,
        ILayer5CasValidation l5,
        PdfTriageVerdict? triage = null,
        double avgConf = 0.88,
        bool catastrophic = false,
        int validated = 1,
        int failed = 0)
    {
        l0.RunAsync(default, "", default)
            .ReturnsForAnyArgs(new Layer0Output(FakePages, triage, 0.42));

        l1.RunAsync(default!, null, default)
            .ReturnsForAnyArgs(new Layer1Output(
                new[] { new LayoutRegion("text", new BoundingBox(0, 0, 100, 50)) },
                IsDegradedMode: false,
                LatencySeconds: 0.78));

        var textBlocks = new[]
        {
            new OcrTextBlock("מתמטיקה", new BoundingBox(0, 0, 100, 20),
                Language.Hebrew, Confidence: 0.91, IsRtl: true),
        };
        var mathBlocks = new[]
        {
            new OcrMathBlock("3x+5=14", new BoundingBox(0, 30, 100, 20),
                Confidence: 0.87, SympyParsed: true, CanonicalForm: "3*x - 9"),
        };
        var figures = Array.Empty<OcrFigureRef>();

        l2a.RunAsync(default!, default!, null, default)
            .ReturnsForAnyArgs(new Layer2aOutput(textBlocks, 1.15));
        l2b.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(new Layer2bOutput(mathBlocks, 0.32));
        l2c.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(new Layer2cOutput(figures, 0.01));

        l3.Run(default!, default!, default!)
            .ReturnsForAnyArgs(new Layer3Output(textBlocks, mathBlocks, figures, 0.01));

        l4.RunAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(new Layer4Output(
                textBlocks, mathBlocks,
                FallbacksFired: Array.Empty<string>(),
                AverageConfidence: avgConf,
                CatastrophicFailure: catastrophic,
                LatencySeconds: 0.02));

        l5.RunAsync(default!, default)
            .ReturnsForAnyArgs(new Layer5Output(mathBlocks, validated, failed, 0.03));
    }

    // ── Tests ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Empty_Bytes_Throws_OcrInputException()
    {
        var (svc, _, _, _, _, _, _, _, _, _) = Build();

        await Assert.ThrowsAsync<OcrInputException>(() =>
            svc.RecognizeAsync(
                ReadOnlyMemory<byte>.Empty,
                "image/png",
                hints: null,
                CascadeSurface.StudentInteractive,
                CancellationToken.None));
    }

    [Fact]
    public async Task Missing_ContentType_Throws_OcrInputException()
    {
        var (svc, _, _, _, _, _, _, _, _, _) = Build();

        await Assert.ThrowsAsync<OcrInputException>(() =>
            svc.RecognizeAsync(
                FakeBytes, "",
                hints: null,
                CascadeSurface.StudentInteractive,
                CancellationToken.None));
    }

    [Fact]
    public async Task Happy_Path_Runs_All_Six_Layers_And_Aggregates_Timings()
    {
        var (svc, _, l0, l1, l2a, l2b, l2c, l3, l4, l5) = Build();
        WireHappyPath(l0, l1, l2a, l2b, l2c, l3, l4, l5);

        var result = await svc.RecognizeAsync(
            FakeBytes, "image/png",
            hints: new OcrContextHints("math", Language.Hebrew, Track.Units5,
                SourceType.StudentPhoto, null, false),
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.Equal("cascade", result.Runner);
        Assert.Equal(1, result.TextBlocks.Count);
        Assert.Equal(1, result.MathBlocks.Count);
        Assert.Equal(1, result.CasValidatedMath);
        Assert.Equal(0, result.CasFailedMath);
        Assert.False(result.HumanReviewRequired);
        Assert.InRange(result.OverallConfidence, 0.87, 0.89);

        Assert.Contains("layer_0_preprocess", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_1_layout", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_2a_text", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_2b_math", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_2c_figures", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_3_reassemble", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_4_gate", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_5_cas", result.LayerTimingsSeconds.Keys);

        await l0.Received(1).RunAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await l1.Received(1).RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<OcrContextHints?>(), Arg.Any<CancellationToken>());
        await l2a.Received(1).RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<IReadOnlyList<LayoutRegion>>(),
            Arg.Any<OcrContextHints?>(), Arg.Any<CancellationToken>());
        await l2b.Received(1).RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<IReadOnlyList<LayoutRegion>>(),
            Arg.Any<CancellationToken>());
        await l2c.Received(1).RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<IReadOnlyList<LayoutRegion>>(),
            Arg.Any<CancellationToken>());
        l3.Received(1).Run(
            Arg.Any<IReadOnlyList<OcrTextBlock>>(),
            Arg.Any<IReadOnlyList<OcrMathBlock>>(),
            Arg.Any<IReadOnlyList<OcrFigureRef>>());
        await l4.Received(1).RunAsync(
            Arg.Any<IReadOnlyList<OcrTextBlock>>(),
            Arg.Any<IReadOnlyList<OcrMathBlock>>(),
            Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>());
        await l5.Received(1).RunAsync(
            Arg.Any<IReadOnlyList<OcrMathBlock>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Encrypted_Triage_ShortCircuits_After_Layer0()
    {
        var (svc, _, l0, l1, l2a, l2b, l2c, l3, l4, l5) = Build();

        l0.RunAsync(default, "", default)
            .ReturnsForAnyArgs(new Layer0Output(
                Array.Empty<byte[]>(),
                PdfTriageVerdict.Encrypted,
                LatencySeconds: 0.04));

        var result = await svc.RecognizeAsync(
            FakeBytes, "application/pdf",
            hints: null,
            CascadeSurface.AdminBatch,
            CancellationToken.None);

        Assert.Equal(PdfTriageVerdict.Encrypted, result.PdfTriage);
        Assert.True(result.HumanReviewRequired);
        Assert.Contains("preprocess_failed_or_encrypted", result.ReasonsForReview);
        Assert.Empty(result.TextBlocks);
        Assert.Empty(result.MathBlocks);

        // Later layers must NOT have been invoked.
        await l1.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<OcrContextHints?>(), Arg.Any<CancellationToken>());
        await l2a.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<IReadOnlyList<LayoutRegion>>(),
            Arg.Any<OcrContextHints?>(), Arg.Any<CancellationToken>());
        await l2b.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<IReadOnlyList<LayoutRegion>>(),
            Arg.Any<CancellationToken>());
        await l2c.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<IReadOnlyList<LayoutRegion>>(),
            Arg.Any<CancellationToken>());
        l3.DidNotReceiveWithAnyArgs().Run(
            Arg.Any<IReadOnlyList<OcrTextBlock>>(),
            Arg.Any<IReadOnlyList<OcrMathBlock>>(),
            Arg.Any<IReadOnlyList<OcrFigureRef>>());
        await l4.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<OcrTextBlock>>(),
            Arg.Any<IReadOnlyList<OcrMathBlock>>(),
            Arg.Any<CascadeSurface>(), Arg.Any<CancellationToken>());
        await l5.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<OcrMathBlock>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Layer4_Catastrophic_Marks_HumanReview_With_Reason()
    {
        var (svc, _, l0, l1, l2a, l2b, l2c, l3, l4, l5) = Build();
        WireHappyPath(l0, l1, l2a, l2b, l2c, l3, l4, l5,
            avgConf: 0.21, catastrophic: true);

        var result = await svc.RecognizeAsync(
            FakeBytes, "image/png", hints: null,
            CascadeSurface.StudentInteractive, CancellationToken.None);

        Assert.True(result.HumanReviewRequired);
        Assert.Contains("low_overall_confidence", result.ReasonsForReview);
    }

    [Fact]
    public async Task Layer5_Majority_CAS_Failure_Marks_HumanReview()
    {
        var (svc, _, l0, l1, l2a, l2b, l2c, l3, l4, l5) = Build();
        WireHappyPath(l0, l1, l2a, l2b, l2c, l3, l4, l5,
            validated: 0, failed: 3);

        var result = await svc.RecognizeAsync(
            FakeBytes, "image/png", hints: null,
            CascadeSurface.AdminBatch, CancellationToken.None);

        Assert.True(result.HumanReviewRequired);
        Assert.Contains("majority_math_failed_cas", result.ReasonsForReview);
    }

    [Fact]
    public async Task Layer2a_2b_2c_Run_In_Parallel()
    {
        var (svc, _, l0, l1, l2a, l2b, l2c, l3, l4, l5) = Build();
        WireHappyPath(l0, l1, l2a, l2b, l2c, l3, l4, l5);

        // Slowest layer (2a) takes 200ms; total parallel time should be ~200,
        // not 600 (which is what sequential execution would give).
        var parallelDelays = new Dictionary<string, int>
        {
            ["2a"] = 200, ["2b"] = 200, ["2c"] = 200,
        };

        l2a.RunAsync(default!, default!, null, default)
            .ReturnsForAnyArgs(async _ =>
            {
                await Task.Delay(parallelDelays["2a"]);
                return new Layer2aOutput(Array.Empty<OcrTextBlock>(), 0.2);
            });
        l2b.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(async _ =>
            {
                await Task.Delay(parallelDelays["2b"]);
                return new Layer2bOutput(Array.Empty<OcrMathBlock>(), 0.2);
            });
        l2c.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(async _ =>
            {
                await Task.Delay(parallelDelays["2c"]);
                return new Layer2cOutput(Array.Empty<OcrFigureRef>(), 0.2);
            });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await svc.RecognizeAsync(
            FakeBytes, "image/png", hints: null,
            CascadeSurface.StudentInteractive, CancellationToken.None);
        sw.Stop();

        // Generous ceiling (500ms) covers CI jitter; the point is <<600ms,
        // which is what sequential execution would force.
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Expected Layers 2a/2b/2c to run in parallel (< 500ms total); " +
            $"actual total = {sw.ElapsedMilliseconds}ms (sequential would be ~600ms).");
    }
}
