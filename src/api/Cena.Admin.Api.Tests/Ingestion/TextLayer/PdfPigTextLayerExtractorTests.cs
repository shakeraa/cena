// =============================================================================
// Cena Platform — PdfPigTextLayerExtractor tests
//
// Pins the PdfPig-based text-layer extractor against the user's actual
// corpus (corpus/tests/35581-q.pdf and 35582-q.pdf — committed to the repo).
// Synthetic-PDF unit tests are deliberately limited to invariants the real
// PDFs can't cover (encrypted-input handling); the cover-page heuristic and
// multi-question split are validated against the real fixtures.
// =============================================================================

using Cena.Admin.Api.Ingestion.TextLayer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.Ingestion.TextLayer;

public sealed class PdfPigTextLayerExtractorTests
{
    /// <summary>
    /// Walk up from the test bin directory to the repo root and resolve
    /// corpus/tests/&lt;name&gt;. Mirrors the existing
    /// <c>AppContext.BaseDirectory + ../../../../</c> pattern used by the
    /// admin-test architecture tests for repo-relative source files.
    /// </summary>
    private static string CorpusFixture(string name)
    {
        var baseDir = AppContext.BaseDirectory;
        // bin/Debug/net9.0/ → src/api/Cena.Admin.Api.Tests → src/api → src → repo-root
        var path = Path.GetFullPath(Path.Combine(baseDir, "../../../../../../", "corpus", "tests", name));
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Corpus fixture missing: {path} (cwd={Environment.CurrentDirectory})");
        return path;
    }

    private static PdfPigTextLayerExtractor BuildSut() =>
        new(NullLogger<PdfPigTextLayerExtractor>.Instance);

    [Fact]
    public async Task RealCorpus_35581_Returns6PagesWithNonEmptyText()
    {
        var bytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));
        var sut = BuildSut();

        var result = await sut.ExtractAsync(bytes, "pdf-corpus-35581");

        Assert.True(result.HasTextLayer);
        Assert.Equal(6, result.Pages.Count);
        foreach (var page in result.Pages)
        {
            Assert.True(page.RawText.Length > 0, $"Page {page.PageNumber} should have text");
        }
    }

    [Fact]
    public async Task RealCorpus_35582_Returns5PagesWithNonEmptyText()
    {
        var bytes = await File.ReadAllBytesAsync(CorpusFixture("35582-q.pdf"));
        var sut = BuildSut();

        var result = await sut.ExtractAsync(bytes, "pdf-corpus-35582");

        Assert.True(result.HasTextLayer);
        Assert.Equal(5, result.Pages.Count);
        foreach (var page in result.Pages)
        {
            Assert.True(page.RawText.Length > 0, $"Page {page.PageNumber} should have text");
        }
    }

    [Fact]
    public async Task RealCorpus_35581_PreservesHebrewCharacters()
    {
        var bytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));
        var sut = BuildSut();

        var result = await sut.ExtractAsync(bytes, "pdf-corpus-35581");

        // Hebrew preservation — page 1 (cover) MUST contain "הוראות"
        // in LOGICAL order. PdfPigTextLayerExtractor routes per-page
        // text through `pdftotext -layout` (subprocess) which produces
        // logical-order Hebrew with proper bidi handling. Visual-
        // reversed ("תוארוה") is a regression we explicitly reject —
        // PdfPig's raw page.Text would surface visually-reversed Hebrew
        // on InDesign-tagged content streams, and that's the bug the
        // Poppler swap fixes.
        var page1Text = result.Pages[0].RawText;
        Assert.Contains("הוראות", page1Text);
        Assert.DoesNotContain("תוארוה", page1Text);
    }

    [Fact]
    public async Task RealCorpus_35581_BlocksAreExtractedPerPage()
    {
        var bytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));
        var sut = BuildSut();

        var result = await sut.ExtractAsync(bytes, "pdf-corpus-35581");

        // Block extraction is best-effort — but real Bagrut PDFs produce
        // dozens of distinct lines per page, far above the 5-line minimum
        // the task DoD requires.
        foreach (var page in result.Pages.Skip(1)) // page 1 cover may be sparser
        {
            Assert.True(page.Blocks.Count >= 5,
                $"Page {page.PageNumber} should have at least 5 blocks (got {page.Blocks.Count})");
        }
    }

    [Fact]
    public async Task RealCorpus_CoverPageHeuristic_TrueForPage1_FalseForPage5()
    {
        var bytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));
        var sut = BuildSut();
        var result = await sut.ExtractAsync(bytes, "pdf-corpus-35581");

        // Page 1 = cover (instructions + duration OR formula sheet markers).
        Assert.True(
            BagrutCoverPageHeuristic.IsCoverPage(1, result.Pages[0].RawText, out _),
            "Page 1 of 35581-q.pdf must be classified as cover");

        // Page 5 = real question (chapter 3 — calculus, problem .6 marker).
        Assert.False(
            BagrutCoverPageHeuristic.IsCoverPage(5, result.Pages[4].RawText, out _),
            "Page 5 of 35581-q.pdf must NOT be classified as cover");
    }

    [Fact]
    public async Task RealCorpus_35581_Page2_DetectsTwoQuestionMarkers()
    {
        // User-reported defect on 35581-q.pdf page 2: it contains TWO
        // `שאלה N` markers (Q1 walking-speed + Q2 geometric series) but the
        // legacy segmenter produced only one draft. The marker scanner MUST
        // surface both so the page splitter can produce two slices.
        var bytes = await File.ReadAllBytesAsync(CorpusFixture("35581-q.pdf"));
        var sut = BuildSut();
        var result = await sut.ExtractAsync(bytes, "pdf-corpus-35581");

        var page2 = result.Pages[1];
        var slices = BagrutPageQuestionSplitter.Split(page2.RawText);

        Assert.Equal(2, slices.Count);
        Assert.True(
            slices.All(s => s.QuestionNumber.HasValue),
            "Both slices on page 2 must carry a question number");

        // Question numbers must be consecutive (1, 2) for the first chapter.
        var numbers = slices.Where(s => s.QuestionNumber.HasValue)
                            .Select(s => s.QuestionNumber!.Value)
                            .OrderBy(n => n)
                            .ToList();
        Assert.Equal(new[] { 1, 2 }, numbers);
    }

    [Fact]
    public async Task EmptyBytes_Throws()
    {
        var sut = BuildSut();
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ExtractAsync(Array.Empty<byte>(), "pdf-empty"));
    }

    [Fact]
    public async Task NotAPdf_ReturnsHasTextLayerFalse_OrThrows()
    {
        // PdfPig may either throw (corrupt header) or return no text. We
        // accept either path — the orchestrator falls through to vision in
        // both cases. The contract that matters: NEVER produce a phantom
        // text layer on a non-PDF input.
        var sut = BuildSut();
        var bytes = System.Text.Encoding.UTF8.GetBytes("not a pdf — random bytes");

        try
        {
            var result = await sut.ExtractAsync(bytes, "pdf-bogus");
            Assert.False(result.HasTextLayer);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            // Non-PDF input → PdfPig throws → orchestrator falls back to
            // vision/cascade. This is the documented behaviour.
        }
    }
}
