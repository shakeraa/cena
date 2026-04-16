// =============================================================================
// Cena Platform — OcrCascadeService end-to-end tests (real brain layers)
//
// Wires the real Layer3Reassemble + Layer4ConfidenceGate + Layer5CasValidation
// concretes and mocks only the wrapper layers (Layer0 preprocess, Layer1
// layout, Layer2a/b/c OCR). Proves the brain composes with the orchestrator
// and produces the shape downstream consumers expect from
// scripts/ocr-spike/dev-fixtures/cascade-results/.
//
// Complements OcrCascadeServiceTests (which mocks the brain) by catching
// integration bugs that single-layer tests can't see: wrong record mutation,
// wrong confidence averaging, RTL ordering off-by-one, Layer 5 after Layer 4
// sequencing.
// =============================================================================

using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Cas;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.DependencyInjection;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Infrastructure.Tests.Ocr;

public class OcrCascadeEndToEndTests
{
    private static readonly byte[] FakeBytes = { 0x89, 0x50, 0x4E, 0x47 };

    // -------------------------------------------------------------------------
    // Container builder — wires real brain + mocked wrappers
    // -------------------------------------------------------------------------
    private sealed record Wiring(
        IOcrCascadeService Service,
        ILayer0Preprocess Layer0,
        ILayer1Layout Layer1,
        ILayer2aTextOcr Layer2a,
        ILayer2bMathOcr Layer2b,
        ILayer2cFigureExtraction Layer2c,
        ILatexValidator Validator);

    private static Wiring BuildWiring(
        Func<ILayer0Preprocess>? layer0Factory = null,
        Func<ILayer1Layout>? layer1Factory = null,
        Func<ILayer2aTextOcr>? layer2aFactory = null,
        Func<ILayer2bMathOcr>? layer2bFactory = null,
        Func<ILayer2cFigureExtraction>? layer2cFactory = null,
        Func<ILatexValidator>? validatorFactory = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOcrCascadeCore();   // registers brain + NullLatexValidator

        // Override NullLatexValidator if the test supplies one
        if (validatorFactory is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<ILatexValidator>(_ => validatorFactory()));
        }

        // Register the mock wrapper layers
        var l0 = layer0Factory?.Invoke() ?? Substitute.For<ILayer0Preprocess>();
        var l1 = layer1Factory?.Invoke() ?? Substitute.For<ILayer1Layout>();
        var l2a = layer2aFactory?.Invoke() ?? Substitute.For<ILayer2aTextOcr>();
        var l2b = layer2bFactory?.Invoke() ?? Substitute.For<ILayer2bMathOcr>();
        var l2c = layer2cFactory?.Invoke() ?? Substitute.For<ILayer2cFigureExtraction>();

        services.AddSingleton(l0);
        services.AddSingleton(l1);
        services.AddSingleton(l2a);
        services.AddSingleton(l2b);
        services.AddSingleton(l2c);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOcrCascadeService>();
        var validator = scope.ServiceProvider.GetRequiredService<ILatexValidator>();

        return new Wiring(svc, l0, l1, l2a, l2b, l2c, validator);
    }

    // -------------------------------------------------------------------------
    // Standard happy-path fixture: 3-block Hebrew row + one math expression,
    // high confidence throughout, valid LaTeX.
    // -------------------------------------------------------------------------
    private static void WireHappyPathLayers(Wiring w)
    {
        w.Layer0.RunAsync(default, "", default)
            .ReturnsForAnyArgs(new Layer0Output(
                new List<byte[]> { FakeBytes },
                Triage: null,
                LatencySeconds: 0.45));

        w.Layer1.RunAsync(default!, null, default)
            .ReturnsForAnyArgs(new Layer1Output(
                Regions: new List<LayoutRegion>
                {
                    new("text", new BoundingBox(10, 50, 200, 20)),
                    new("math", new BoundingBox(10, 150, 200, 30)),
                },
                IsDegradedMode: false,
                LatencySeconds: 0.78));

        // Three Hebrew text blocks in row y=50 — will be RTL-ordered by Layer 3
        w.Layer2a.RunAsync(default!, default!, null, default)
            .ReturnsForAnyArgs(new Layer2aOutput(
                TextBlocks: new List<OcrTextBlock>
                {
                    new("שלישי", new BoundingBox(40, 50, 60, 20),
                        Language.Hebrew, Confidence: 0.91, IsRtl: true),
                    new("ראשון", new BoundingBox(200, 50, 60, 20),
                        Language.Hebrew, Confidence: 0.93, IsRtl: true),
                    new("שני", new BoundingBox(120, 50, 60, 20),
                        Language.Hebrew, Confidence: 0.92, IsRtl: true),
                },
                LatencySeconds: 1.15));

        w.Layer2b.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(new Layer2bOutput(
                MathBlocks: new List<OcrMathBlock>
                {
                    new("3x+5=14", new BoundingBox(10, 150, 200, 30),
                        Confidence: 0.87, SympyParsed: false, CanonicalForm: null),
                },
                LatencySeconds: 0.32));

        w.Layer2c.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(new Layer2cOutput(
                Figures: Array.Empty<OcrFigureRef>(),
                LatencySeconds: 0.01));
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------
    [Fact]
    public async Task End_To_End_Happy_Path_Composes_All_Real_Brain_Layers()
    {
        var w = BuildWiring(validatorFactory: () =>
        {
            var v = Substitute.For<ILatexValidator>();
            v.ValidateAsync("3x+5=14", Arg.Any<CancellationToken>())
                .Returns(new LatexValidationResult(Parsed: true, CanonicalForm: "3*x - 9"));
            return v;
        });
        WireHappyPathLayers(w);

        var result = await w.Service.RecognizeAsync(
            FakeBytes,
            "image/png",
            hints: new OcrContextHints("math", Language.Hebrew, Track.Units5,
                SourceType.StudentPhoto, null, false),
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        // Layer 3 real: RTL majority → reverse x order
        var order = result.TextBlocks.Select(b => b.Text).ToArray();
        Assert.Equal(new[] { "ראשון", "שני", "שלישי" }, order);

        // Layer 5 real: SympyParsed=true + canonical form set
        Assert.Single(result.MathBlocks);
        Assert.True(result.MathBlocks[0].SympyParsed);
        Assert.Equal("3*x - 9", result.MathBlocks[0].CanonicalForm);
        Assert.Equal(1, result.CasValidatedMath);
        Assert.Equal(0, result.CasFailedMath);

        // Layer 4 real: avg confidence across all blocks, no catastrophic
        Assert.InRange(result.OverallConfidence, 0.87, 0.93);
        Assert.False(result.HumanReviewRequired);
        Assert.Empty(result.FallbacksFired);

        // Timings for all layers present
        Assert.Contains("layer_0_preprocess", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_3_reassemble", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_4_gate", result.LayerTimingsSeconds.Keys);
        Assert.Contains("layer_5_cas", result.LayerTimingsSeconds.Keys);
    }

    [Fact]
    public async Task End_To_End_With_Low_Confidence_Student_Surface_Marks_Review()
    {
        // All blocks below catastrophic threshold → Layer 4 says catastrophic,
        // orchestrator says human_review_required with "low_overall_confidence".
        var w = BuildWiring();

        w.Layer0.RunAsync(default, "", default)
            .ReturnsForAnyArgs(new Layer0Output(
                new List<byte[]> { FakeBytes }, null, 0.4));

        w.Layer1.RunAsync(default!, null, default)
            .ReturnsForAnyArgs(new Layer1Output(
                new List<LayoutRegion> { new("text", new BoundingBox(0, 0, 100, 20)) },
                IsDegradedMode: true, LatencySeconds: 0.01));

        w.Layer2a.RunAsync(default!, default!, null, default)
            .ReturnsForAnyArgs(new Layer2aOutput(
                new List<OcrTextBlock>
                {
                    new("noisy", new BoundingBox(0, 0, 50, 20),
                        Language.English, Confidence: 0.15, IsRtl: false),
                },
                LatencySeconds: 0.1));

        w.Layer2b.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(new Layer2bOutput(Array.Empty<OcrMathBlock>(), 0));
        w.Layer2c.RunAsync(default!, default!, default)
            .ReturnsForAnyArgs(new Layer2cOutput(Array.Empty<OcrFigureRef>(), 0));

        var result = await w.Service.RecognizeAsync(
            FakeBytes, "image/png", hints: null,
            CascadeSurface.StudentInteractive,
            CancellationToken.None);

        Assert.True(result.HumanReviewRequired);
        Assert.Contains("low_overall_confidence", result.ReasonsForReview);
    }

    [Fact]
    public async Task End_To_End_With_NullLatexValidator_Marks_CAS_Failures()
    {
        // Uses the AddOcrCascadeCore() default (NullLatexValidator) — proves
        // the fail-closed wiring actually reaches Layer 5.
        var w = BuildWiring();
        WireHappyPathLayers(w);

        var result = await w.Service.RecognizeAsync(
            FakeBytes, "image/png", hints: null,
            CascadeSurface.AdminBatch, CancellationToken.None);

        Assert.Equal(0, result.CasValidatedMath);
        Assert.Equal(1, result.CasFailedMath);
        Assert.False(result.MathBlocks[0].SympyParsed);
        Assert.True(result.HumanReviewRequired);
        Assert.Contains("majority_math_failed_cas", result.ReasonsForReview);
    }

    [Fact]
    public async Task End_To_End_Encrypted_Pdf_Short_Circuits_At_Layer_0()
    {
        var w = BuildWiring();
        w.Layer0.RunAsync(default, "", default)
            .ReturnsForAnyArgs(new Layer0Output(
                Array.Empty<byte[]>(),
                Triage: PdfTriageVerdict.Encrypted,
                LatencySeconds: 0.03));

        var result = await w.Service.RecognizeAsync(
            FakeBytes, "application/pdf", hints: null,
            CascadeSurface.AdminBatch, CancellationToken.None);

        Assert.Equal(PdfTriageVerdict.Encrypted, result.PdfTriage);
        Assert.True(result.HumanReviewRequired);
        Assert.Contains("preprocess_failed_or_encrypted", result.ReasonsForReview);

        // Later layers never reached
        await w.Layer1.DidNotReceiveWithAnyArgs().RunAsync(
            Arg.Any<IReadOnlyList<byte[]>>(),
            Arg.Any<OcrContextHints?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DI_AddOcrCascadeCore_Registers_Expected_Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOcrCascadeCore();

        // Must register stubs for wrapper layers to build the service
        services.AddSingleton(Substitute.For<ILayer0Preprocess>());
        services.AddSingleton(Substitute.For<ILayer1Layout>());
        services.AddSingleton(Substitute.For<ILayer2aTextOcr>());
        services.AddSingleton(Substitute.For<ILayer2bMathOcr>());
        services.AddSingleton(Substitute.For<ILayer2cFigureExtraction>());

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPdfTriage>());
        Assert.NotNull(provider.GetRequiredService<ILayer3Reassemble>());
        Assert.NotNull(provider.GetRequiredService<ILayer4ConfidenceGate>());
        Assert.NotNull(provider.GetRequiredService<ILayer5CasValidation>());
        Assert.NotNull(provider.GetRequiredService<ILatexValidator>());
        Assert.NotNull(provider.GetRequiredService<ConfidenceGateOptions>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOcrCascadeService>());
    }
}
