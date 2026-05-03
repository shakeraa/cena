// =============================================================================
// Cena Platform — TesseractLocalRunner tests
//
// Two tiers:
//   1. TSV parser unit tests — pure function, no binary needed, deterministic.
//   2. Integration test — hits the real `tesseract` binary on a synthesized
//      image, skipped gracefully when the binary is absent (e.g. CI without
//      the system dep).
// =============================================================================

using System.Diagnostics;
using System.Text;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.Runners;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace Cena.Infrastructure.Tests.Ocr;

public class TesseractLocalRunnerTests
{
    // -------------------------------------------------------------------------
    // TSV parser tests — no external dependencies
    // -------------------------------------------------------------------------
    [Fact]
    public void ParseTsv_Empty_Returns_Empty()
    {
        var blocks = TesseractLocalRunner.ParseTsv(string.Empty, pageNumber: 1, languageCode: "eng");
        Assert.Empty(blocks);
    }

    [Fact]
    public void ParseTsv_Skips_Header_Row()
    {
        var tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";
        var blocks = TesseractLocalRunner.ParseTsv(tsv, 1, "eng");
        Assert.Empty(blocks);
    }

    [Fact]
    public void ParseTsv_Extracts_Word_Level_Rows_Only()
    {
        // Tesseract levels: 1=page, 2=block, 3=para, 4=line, 5=word.
        // Only level-5 rows carry the actual words + per-word bboxes.
        var tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "1\t1\t0\t0\t0\t0\t0\t0\t1000\t1000\t-1\t\n" +           // page — skip
            "2\t1\t1\t0\t0\t0\t50\t50\t900\t900\t-1\t\n" +           // block — skip
            "3\t1\t1\t1\t0\t0\t50\t50\t900\t200\t-1\t\n" +           // para — skip
            "4\t1\t1\t1\t1\t0\t50\t50\t900\t30\t-1\t\n" +            // line — skip
            "5\t1\t1\t1\t1\t1\t50\t50\t80\t30\t95\tHello\n" +       // word ✓
            "5\t1\t1\t1\t1\t2\t140\t50\t120\t30\t92\tworld\n";      // word ✓
        var blocks = TesseractLocalRunner.ParseTsv(tsv, pageNumber: 1, languageCode: "eng");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Hello", blocks[0].Text);
        Assert.Equal(0.95, blocks[0].Confidence, precision: 2);
        Assert.Equal(50, blocks[0].Bbox!.X);
        Assert.Equal(1, blocks[0].Bbox.Page);
        Assert.False(blocks[0].IsRtl);
        Assert.Equal(Language.English, blocks[0].Language);
    }

    [Fact]
    public void ParseTsv_Hebrew_Words_Marked_Rtl_And_Language_Hebrew()
    {
        var tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "5\t1\t1\t1\t1\t1\t200\t50\t80\t30\t88\tמתמטיקה\n";
        var blocks = TesseractLocalRunner.ParseTsv(tsv, 1, "heb+eng");
        var word = Assert.Single(blocks);
        Assert.True(word.IsRtl);
        Assert.Equal(Language.Hebrew, word.Language);
        Assert.Equal("מתמטיקה", word.Text);
    }

    [Fact]
    public void ParseTsv_Negative_Confidence_Is_Dropped()
    {
        // Tesseract emits conf=-1 for whitespace/no-text rows. We drop them.
        var tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t-1\t\n" +            // empty text — skip
            "5\t1\t1\t1\t1\t2\t20\t0\t10\t10\t0\tx\n" +           // conf=0 — skip (no signal)
            "5\t1\t1\t1\t1\t3\t40\t0\t10\t10\t75\tokay\n";        // conf=75 — keep
        var blocks = TesseractLocalRunner.ParseTsv(tsv, 1, "eng");

        var kept = Assert.Single(blocks);
        Assert.Equal("okay", kept.Text);
    }

    [Fact]
    public void ParseTsv_Arabic_Language_Code_Maps_To_Arabic_Enum()
    {
        var tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t90\tarabic\n";
        var blocks = TesseractLocalRunner.ParseTsv(tsv, 1, "ara");
        Assert.Equal(Language.Arabic, Assert.Single(blocks).Language);
    }

    [Fact]
    public void ParseTsv_Confidence_Clamped_Above_100_Unlikely_But_Safe()
    {
        // Defensive: if tesseract ever emits >100, we clamp at 1.0.
        var tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t150\tweird\n";
        var blocks = TesseractLocalRunner.ParseTsv(tsv, 1, "eng");
        Assert.Equal(1.0, Assert.Single(blocks).Confidence, precision: 2);
    }

    // -------------------------------------------------------------------------
    // Integration test — requires real `tesseract` binary on PATH + the
    // `eng.traineddata` pack. Skipped gracefully when either is missing.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Integration_Recognises_English_Text_From_Synthesized_Image()
    {
        if (!IsTesseractAvailable())
        {
            // Skip instead of failing — this keeps the test suite portable.
            // On dev + in Docker the binary is always present.
            return;
        }

        var pngBytes = RenderTextPng("Hello world 42", width: 400, height: 120);

        var runner = new TesseractLocalRunner(new TesseractOptions
        {
            DefaultLanguageCode = "eng",
            RequiredLanguagePacks = new[] { "eng" },
        });

        var output = await runner.RunAsync(
            pageBytes: new[] { pngBytes },
            textRegions: Array.Empty<LayoutRegion>(),
            hints: new OcrContextHints(
                Subject: null, Language: Language.English, Track: null,
                SourceType: null, TaxonomyNode: null, ExpectedFigures: null),
            ct: CancellationToken.None);

        // Tesseract should recognise at least one of the words; don't require
        // exact match because antialiasing + typography can flip a character.
        var text = string.Join(" ", output.TextBlocks.Select(b => b.Text)).ToLowerInvariant();
        Assert.Contains("hello", text);
        Assert.All(output.TextBlocks, b => Assert.InRange(b.Confidence, 0, 1));
    }

    [Fact]
    public async Task Integration_Recognises_Hebrew_When_Heb_Pack_Available()
    {
        if (!IsTesseractAvailable() || !HasLanguagePack("heb"))
        {
            return;
        }

        // Use tofu-safe font fallback so `מתמטיקה` actually rasterizes.
        // If no Hebrew-capable font is installed the render step will draw
        // empty boxes and the test becomes a no-op — same graceful skip.
        if (!TryResolveHebrewFont(out _))
        {
            return;
        }

        var pngBytes = RenderTextPng("מתמטיקה 2024", width: 600, height: 120, language: "he");

        var runner = new TesseractLocalRunner(new TesseractOptions
        {
            DefaultLanguageCode = "heb+eng",
        });

        var output = await runner.RunAsync(
            pageBytes: new[] { pngBytes },
            textRegions: Array.Empty<LayoutRegion>(),
            hints: new OcrContextHints(
                Subject: null, Language: Language.Hebrew, Track: null,
                SourceType: null, TaxonomyNode: null, ExpectedFigures: null),
            ct: CancellationToken.None);

        // Assert we got *something* classified as Hebrew. Don't require exact
        // character match — Tesseract on synthesized renders is lossy.
        Assert.NotEmpty(output.TextBlocks);
        var anyHebrew = output.TextBlocks.Any(b => b.IsRtl);
        Assert.True(anyHebrew,
            $"Expected at least one RTL block; got: {string.Join(" | ", output.TextBlocks.Select(b => b.Text))}");
    }

    // -------------------------------------------------------------------------
    // Helpers — environment probes + synthetic image renderer
    // -------------------------------------------------------------------------
    private static bool IsTesseractAvailable()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return false;
            return proc.WaitForExit(3000) && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasLanguagePack(string code)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = "--list-langs",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return false;
            var combined = proc.StandardOutput.ReadToEnd() + "\n" + proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);
            return combined.Split('\n', '\r')
                .Any(l => string.Equals(l.Trim(), code, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static byte[] RenderTextPng(string text, int width, int height, string language = "en")
    {
        using var img = new Image<Rgba32>(width, height, Color.White);

        if (!TryResolveFont(language, size: 48, out var font))
        {
            // Return a blank PNG — tests that can't render will skip on the
            // Tesseract-finds-nothing side rather than crashing here.
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            return ms.ToArray();
        }

        img.Mutate(ctx => ctx.DrawText(
            text,
            font,
            Color.Black,
            new PointF(20, 20)));

        using var buffer = new MemoryStream();
        img.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static bool TryResolveFont(string language, float size, out Font font)
    {
        if (language == "he" && TryResolveHebrewFont(out var hebrewFamily))
        {
            font = hebrewFamily.CreateFont(size, FontStyle.Regular);
            return true;
        }

        // Pick any Latin-capable font available on the system.
        foreach (var candidate in new[] { "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans" })
        {
            if (SystemFonts.TryGet(candidate, out var family))
            {
                font = family.CreateFont(size, FontStyle.Regular);
                return true;
            }
        }

        if (SystemFonts.Families.Any())
        {
            font = SystemFonts.Families.First().CreateFont(size, FontStyle.Regular);
            return true;
        }

        font = default!;
        return false;
    }

    private static bool TryResolveHebrewFont(out FontFamily family)
    {
        foreach (var candidate in new[] { "Arial Hebrew", "Arial", "Helvetica", "Noto Sans Hebrew", "DejaVu Sans" })
        {
            if (SystemFonts.TryGet(candidate, out family)) return true;
        }
        family = default;
        return false;
    }
}
