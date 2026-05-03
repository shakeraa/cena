// =============================================================================
// Cena Platform — FigureCropper tests (vision-extractor branch)
//
// Pins the contract:
//   - Single bbox crops correctly to a non-empty PNG file.
//   - Out-of-bounds bbox is clamped (kept, not dropped — clamping behaviour
//     matches Layer2cFigureExtraction).
//   - Empty figures list → no files written, returns empty list.
//   - Cropped PNG is non-zero size and sane (< MaxFigureBytes default).
//   - Below-minimum-area bbox is dropped (a 1×1 pixel "figure" is an
//     artifact, not a real figure).
//   - Missing page PNG → returns empty list, no throw.
// =============================================================================

using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Ocr.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Cena.Admin.Api.Tests.Ingestion.Vision;

public sealed class FigureCropperTests : IDisposable
{
    private readonly string _scratchRoot;
    private readonly FigureCropper _cropper;

    public FigureCropperTests()
    {
        _scratchRoot = Path.Combine(
            Path.GetTempPath(),
            $"cena-cropper-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchRoot);

        _cropper = new FigureCropper(
            Options.Create(new FigureStorageOptions
            {
                OutputDirectory = Path.Combine(_scratchRoot, "figures"),
                MaxFigureBytes = 5_000_000,
            }),
            NullLogger<FigureCropper>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_scratchRoot)) Directory.Delete(_scratchRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SingleBbox_CropsCorrectly()
    {
        var pagePng = WriteSamplePagePng(width: 1000, height: 1400);
        var figures = new[]
        {
            new DetectedFigure(X: 100, Y: 200, Width: 300, Height: 250,
                Kind: "diagram", AltText: "axis"),
        };

        var cropped = await _cropper.CropAsync(pagePng, pageNumber: 1, "pdf-test", figures);

        Assert.Single(cropped);
        var rec = cropped[0];
        Assert.Equal(1, rec.PageNumber);
        Assert.Equal(0, rec.FigureIndex);
        Assert.Equal(100, rec.X);
        Assert.Equal(200, rec.Y);
        Assert.Equal(300, rec.Width);
        Assert.Equal(250, rec.Height);
        Assert.Equal("figure", rec.Kind); // 'diagram' normalises to 'figure'
        Assert.Equal("axis", rec.AltText);
        Assert.True(File.Exists(rec.CroppedPath));

        // Sanity: cropped image dimensions match the bbox (or close — we
        // clamp; this bbox fits inside the page so no clamp).
        using var img = Image.Load(rec.CroppedPath);
        Assert.Equal(300, img.Width);
        Assert.Equal(250, img.Height);

        var size = new FileInfo(rec.CroppedPath).Length;
        Assert.True(size > 0, "cropped PNG must not be zero bytes");
        Assert.True(size < 5_000_000, "cropped PNG must be under MaxFigureBytes");
    }

    [Fact]
    public async Task OutOfBoundsBbox_IsClampedToImageBounds_Kept()
    {
        var pagePng = WriteSamplePagePng(width: 1000, height: 1400);
        var figures = new[]
        {
            // Negative origin + huge size — clamps into the page bounds.
            new DetectedFigure(X: -50, Y: -50, Width: 99999, Height: 99999,
                Kind: "diagram", AltText: null),
        };

        var cropped = await _cropper.CropAsync(pagePng, pageNumber: 1, "pdf-clamp", figures);

        Assert.Single(cropped);
        var rec = cropped[0];
        // After clamping: x=0, y=0, w=1000, h=1400.
        Assert.Equal(0, rec.X);
        Assert.Equal(0, rec.Y);
        Assert.Equal(1000, rec.Width);
        Assert.Equal(1400, rec.Height);
        Assert.True(File.Exists(rec.CroppedPath));
    }

    [Fact]
    public async Task EmptyFiguresList_NoFilesWritten_ReturnsEmpty()
    {
        var pagePng = WriteSamplePagePng();
        var figuresBefore = Directory.Exists(Path.Combine(_scratchRoot, "figures"))
            ? Directory.GetFiles(Path.Combine(_scratchRoot, "figures")).Length
            : 0;

        var cropped = await _cropper.CropAsync(
            pagePng, pageNumber: 1, "pdf-empty",
            Array.Empty<DetectedFigure>());

        Assert.Empty(cropped);

        var figuresAfter = Directory.Exists(Path.Combine(_scratchRoot, "figures"))
            ? Directory.GetFiles(Path.Combine(_scratchRoot, "figures")).Length
            : 0;
        Assert.Equal(figuresBefore, figuresAfter);
    }

    [Fact]
    public async Task BelowMinAreaBbox_IsDropped()
    {
        var pagePng = WriteSamplePagePng();
        var figures = new[]
        {
            // 10×10 = 100 px² — below the 40×40 = 1600 px² threshold.
            new DetectedFigure(X: 100, Y: 100, Width: 10, Height: 10,
                Kind: "diagram", AltText: null),
        };

        var cropped = await _cropper.CropAsync(pagePng, pageNumber: 1, "pdf-tiny", figures);

        Assert.Empty(cropped);
    }

    [Fact]
    public async Task MissingPagePng_ReturnsEmptyList_NoThrow()
    {
        var cropped = await _cropper.CropAsync(
            Path.Combine(_scratchRoot, "definitely-not-a-real-file.png"),
            pageNumber: 1, "pdf-missing",
            new[] { new DetectedFigure(0, 0, 100, 100, "diagram", null) });

        Assert.Empty(cropped);
    }

    [Fact]
    public async Task MultipleFigures_CroppedInOrder_DistinctPaths()
    {
        var pagePng = WriteSamplePagePng(width: 1200, height: 1600);
        var figures = new[]
        {
            new DetectedFigure(X: 50,  Y: 50,  Width: 200, Height: 150, Kind: "diagram", AltText: "a"),
            new DetectedFigure(X: 300, Y: 50,  Width: 200, Height: 150, Kind: "chart",   AltText: "b"),
            new DetectedFigure(X: 50,  Y: 300, Width: 200, Height: 150, Kind: "table",   AltText: "c"),
        };

        var cropped = await _cropper.CropAsync(pagePng, pageNumber: 2, "pdf-multi", figures);

        Assert.Equal(3, cropped.Count);
        Assert.Equal("figure", cropped[0].Kind);  // diagram → figure
        Assert.Equal("plot",   cropped[1].Kind);  // chart   → plot
        Assert.Equal("table",  cropped[2].Kind);  // table   → table
        Assert.Equal(0, cropped[0].FigureIndex);
        Assert.Equal(1, cropped[1].FigureIndex);
        Assert.Equal(2, cropped[2].FigureIndex);
        Assert.NotEqual(cropped[0].CroppedPath, cropped[1].CroppedPath);
        Assert.NotEqual(cropped[1].CroppedPath, cropped[2].CroppedPath);
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    /// <summary>
    /// Write a synthetic PNG to disk. The image is solid white so the
    /// cropped regions are also valid PNGs (ImageSharp can encode/decode).
    /// </summary>
    private string WriteSamplePagePng(int width = 1000, int height = 1400)
    {
        var path = Path.Combine(_scratchRoot, $"page-{Guid.NewGuid():N}.png");
        using var img = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 255));
        img.SaveAsPng(path, new PngEncoder());
        return path;
    }
}
