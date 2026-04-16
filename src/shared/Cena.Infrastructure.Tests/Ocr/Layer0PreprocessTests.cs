// =============================================================================
// Cena Platform — Layer0Preprocess tests
//
// Image path: fully deterministic — builds an ImageSharp PNG, runs the real
// preprocessor, asserts resize + grayscale.
//
// PDF path: runs `pdftoppm` when available. Skipped gracefully otherwise
// (dev has poppler via Homebrew; prod Docker has poppler-utils).
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Cena.Infrastructure.Tests.Ocr;

public class Layer0PreprocessTests
{
    private readonly IPdfTriage _triage = new Cena.Infrastructure.Ocr.PdfTriage.PdfTriage();

    private Layer0Preprocess BuildLayer(Layer0PreprocessOptions? options = null)
        => new(_triage, options, NullLogger<Layer0Preprocess>.Instance);

    private static byte[] BuildPng(int w, int h, Color? fill = null)
    {
        using var img = new Image<Rgba32>(w, h, fill ?? Color.CornflowerBlue);
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static byte[] BuildSimpleTextPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 14, new PdfPoint(50, 780), font);
        return builder.Build();
    }

    private static bool IsPdftoppmAvailable()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "pdftoppm",
                Arguments = "-v",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return false;
            return proc.WaitForExit(3000);
        }
        catch { return false; }
    }

    // -------------------------------------------------------------------------
    // Input validation
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Empty_Bytes_Throws_OcrInputException()
    {
        var layer = BuildLayer();
        await Assert.ThrowsAsync<OcrInputException>(() =>
            layer.RunAsync(
                ReadOnlyMemory<byte>.Empty,
                "image/png",
                CancellationToken.None));
    }

    [Fact]
    public async Task Unsupported_ContentType_Throws_OcrInputException()
    {
        var layer = BuildLayer();
        await Assert.ThrowsAsync<OcrInputException>(() =>
            layer.RunAsync(BuildPng(10, 10), "application/zip", CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // Image path
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Small_Image_Passes_Through_With_Grayscale()
    {
        var layer = BuildLayer();
        var result = await layer.RunAsync(
            BuildPng(300, 200),
            "image/png",
            CancellationToken.None);

        Assert.Null(result.Triage);
        Assert.Single(result.PreprocessedPageBytes);

        using var processed = Image.Load<Rgba32>(result.PreprocessedPageBytes[0]);
        Assert.Equal(300, processed.Width);
        Assert.Equal(200, processed.Height);

        // Grayscale: R == G == B everywhere on a plain-fill page.
        var px = processed[10, 10];
        Assert.Equal(px.R, px.G);
        Assert.Equal(px.G, px.B);
    }

    [Fact]
    public async Task Oversize_Image_Gets_Downsampled_To_Max_Long_Edge()
    {
        var layer = BuildLayer(new Layer0PreprocessOptions
        {
            MaxLongEdgePixels = 500,
            ConvertToGrayscale = false,   // keep check simple
        });

        var result = await layer.RunAsync(
            BuildPng(2000, 1000),
            "image/png",
            CancellationToken.None);

        using var processed = Image.Load<Rgba32>(result.PreprocessedPageBytes[0]);
        Assert.Equal(500, processed.Width);
        Assert.Equal(250, processed.Height);
    }

    [Fact]
    public async Task Image_Preprocess_Respects_Grayscale_Disabled()
    {
        var layer = BuildLayer(new Layer0PreprocessOptions
        {
            ConvertToGrayscale = false,
            MaxLongEdgePixels = 2200,
        });

        var result = await layer.RunAsync(
            BuildPng(50, 50, Color.Red),
            "image/png",
            CancellationToken.None);

        using var processed = Image.Load<Rgba32>(result.PreprocessedPageBytes[0]);
        var px = processed[5, 5];
        // R should dominate — grayscale would have flattened it.
        Assert.True(px.R > px.G, $"Red channel expected to dominate; got R={px.R} G={px.G}");
    }

    [Fact]
    public async Task Jpeg_ContentType_Also_Works()
    {
        var layer = BuildLayer();
        var bytes = BuildPng(100, 100);

        // We pass image/jpeg content type but bytes are PNG — ImageSharp
        // auto-detects format, content-type is only a routing hint for
        // PDF-vs-image discrimination.
        var result = await layer.RunAsync(bytes, "image/jpeg", CancellationToken.None);
        Assert.Single(result.PreprocessedPageBytes);
    }

    // -------------------------------------------------------------------------
    // PDF path
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Pdf_Rasterizes_And_Preprocesses_Pages()
    {
        if (!IsPdftoppmAvailable()) return;   // skip in envs without poppler

        // Text has to clear the triage minTextChars threshold (50 chars)
        // AND include common-token hits for the dictionary-sanity check.
        var pdfBytes = BuildSimpleTextPdf(
            "Problem 1: Solve the equation and find the value. " +
            "Compute with given constants and show your work.");
        var layer = BuildLayer();

        var result = await layer.RunAsync(
            pdfBytes,
            "application/pdf",
            CancellationToken.None);

        // Triage picks up on the text layer
        Assert.Equal(PdfTriageVerdict.Text, result.Triage);
        Assert.Single(result.PreprocessedPageBytes);

        using var page = Image.Load<Rgba32>(result.PreprocessedPageBytes[0]);
        Assert.True(page.Width > 0 && page.Height > 0);
    }

    [Fact]
    public async Task Empty_Input_PDF_Propagates_Input_Exception()
    {
        var layer = BuildLayer();
        await Assert.ThrowsAsync<OcrInputException>(() =>
            layer.RunAsync(ReadOnlyMemory<byte>.Empty, "application/pdf", CancellationToken.None));
    }

    [Fact]
    public void PreprocessSingleImage_Returns_Valid_Png()
    {
        var layer = BuildLayer();
        var bytes = BuildPng(100, 100);
        var processed = layer.PreprocessSingleImage(bytes);
        // PNG magic
        Assert.Equal(0x89, processed[0]);
        Assert.Equal(0x50, processed[1]);
        Assert.Equal(0x4E, processed[2]);
        Assert.Equal(0x47, processed[3]);
    }
}
