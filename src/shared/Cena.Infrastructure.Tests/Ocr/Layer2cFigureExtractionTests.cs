// =============================================================================
// Cena Platform — Layer2cFigureExtraction tests
//
// Real crops are written to a per-test temp directory. No mocks. No stubs.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Cena.Infrastructure.Tests.Ocr;

public class Layer2cFigureExtractionTests : IDisposable
{
    private readonly string _tempDir;

    public Layer2cFigureExtractionTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "cena-layer2c-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private Layer2cFigureExtraction BuildLayer() =>
        new(new FigureStorageOptions { OutputDirectory = _tempDir }, NullLogger<Layer2cFigureExtraction>.Instance);

    private static byte[] BuildPage(int w = 200, int h = 200, Color? fill = null)
    {
        using var img = new Image<Rgba32>(w, h, fill ?? Color.White);
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task Empty_Regions_Produces_Empty_Output()
    {
        var layer = BuildLayer();
        var result = await layer.RunAsync(
            new[] { BuildPage() },
            Array.Empty<LayoutRegion>(),
            CancellationToken.None);

        Assert.Empty(result.Figures);
        Assert.True(result.LatencySeconds >= 0);
    }

    [Fact]
    public async Task Empty_Pages_Produces_Empty_Output()
    {
        var layer = BuildLayer();
        var result = await layer.RunAsync(
            Array.Empty<byte[]>(),
            new[] { new LayoutRegion("figure", new BoundingBox(10, 10, 50, 50, 1)) },
            CancellationToken.None);
        Assert.Empty(result.Figures);
    }

    [Fact]
    public async Task Extracts_Figure_Writes_To_Disk_And_Returns_Path()
    {
        var layer = BuildLayer();
        var regions = new[]
        {
            new LayoutRegion("figure", new BoundingBox(10, 10, 50, 50, 1)),
        };

        var result = await layer.RunAsync(
            new[] { BuildPage() },
            regions,
            CancellationToken.None);

        var figure = Assert.Single(result.Figures);
        Assert.Equal("figure", figure.Kind);
        Assert.NotNull(figure.CroppedPath);
        Assert.True(File.Exists(figure.CroppedPath!),
            $"Expected figure file to exist at {figure.CroppedPath}");

        // Verify the written file is a valid PNG with the expected dimensions.
        using var written = Image.Load<Rgba32>(figure.CroppedPath!);
        Assert.Equal(50, written.Width);
        Assert.Equal(50, written.Height);
    }

    [Fact]
    public async Task Clamps_BoundingBox_To_Image_Bounds()
    {
        var layer = BuildLayer();
        // bbox extends past 200×200 page — should clamp.
        var regions = new[]
        {
            new LayoutRegion("figure", new BoundingBox(180, 180, 100, 100, 1)),
        };

        var result = await layer.RunAsync(
            new[] { BuildPage() },
            regions,
            CancellationToken.None);

        var figure = Assert.Single(result.Figures);
        Assert.True(File.Exists(figure.CroppedPath!));
        // Clamped width/height should be page-remainder = 20×20.
        using var written = Image.Load<Rgba32>(figure.CroppedPath!);
        Assert.Equal(20, written.Width);
        Assert.Equal(20, written.Height);
    }

    [Fact]
    public async Task Skips_Region_When_Page_Index_Out_Of_Range()
    {
        var layer = BuildLayer();
        var regions = new[]
        {
            new LayoutRegion("figure", new BoundingBox(10, 10, 50, 50, Page: 99)),
        };

        var result = await layer.RunAsync(
            new[] { BuildPage() }, regions, CancellationToken.None);

        Assert.Empty(result.Figures);
    }

    [Fact]
    public async Task Multiple_Regions_Produce_Multiple_Files()
    {
        var layer = BuildLayer();
        var regions = new[]
        {
            new LayoutRegion("figure", new BoundingBox(5,   5, 40, 40, 1)),
            new LayoutRegion("table",  new BoundingBox(60, 60, 40, 40, 1)),
            new LayoutRegion("plot",   new BoundingBox(120, 10, 40, 40, 1)),
        };

        var result = await layer.RunAsync(
            new[] { BuildPage() }, regions, CancellationToken.None);

        Assert.Equal(3, result.Figures.Count);
        Assert.Equal(new[] { "figure", "table", "plot" }, result.Figures.Select(f => f.Kind));
        foreach (var f in result.Figures)
            Assert.True(File.Exists(f.CroppedPath!));
    }

    [Fact]
    public async Task Identical_Content_Produces_Same_Path_Safely_On_Reruns()
    {
        // Content-addressable filename: running twice with the same region
        // shouldn't duplicate the file, and both runs should agree on the path.
        var layer = BuildLayer();
        var region = new LayoutRegion("figure", new BoundingBox(5, 5, 40, 40, 1));
        var pages = new[] { BuildPage() };

        var first = await layer.RunAsync(pages, new[] { region }, CancellationToken.None);
        var second = await layer.RunAsync(pages, new[] { region }, CancellationToken.None);

        Assert.Equal(
            first.Figures[0].CroppedPath,
            second.Figures[0].CroppedPath);
        Assert.True(File.Exists(first.Figures[0].CroppedPath!));
    }

    [Fact]
    public async Task Cancellation_Before_Work_Throws()
    {
        var layer = BuildLayer();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            layer.RunAsync(
                new[] { BuildPage() },
                new[] { new LayoutRegion("figure", new BoundingBox(10, 10, 50, 50, 1)) },
                new CancellationToken(true)));
    }
}
