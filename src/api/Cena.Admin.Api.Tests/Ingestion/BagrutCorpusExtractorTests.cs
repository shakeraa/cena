// =============================================================================
// Cena Platform — BagrutCorpusExtractor tests (prr-242)
//
// Pure-function tests for the extractor: PDF ingest drafts + context →
// BagrutCorpusItemDocument[]. No Marten, no HTTP.
//
// Scenarios:
//   (a) Basic projection — 3 drafts → 3 corpus items with stable ids
//       + deterministic normalisation.
//   (b) Year/season/moed heuristics read from filename and OCR text.
//   (c) Stream inference: "arab stream" / "מגזר ערבי" flags items as Arab.
//   (d) Units inference from the Ministry paper code prefix (035371→3,
//       035471→4, 035583→5).
//   (e) Empty drafts → empty output (no throw).
//   (f) Missing prompt text → item skipped (we don't persist empty rows).
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutCorpusExtractorTests
{
    private static BagrutCorpusIngestContext NewContext(
        string paperCode = "035581",
        int? year = null,
        BagrutCorpusSeason? season = null,
        BagrutCorpusStream? stream = null,
        string? filename = null)
        => new(
            ExamCode: "bagrut-math-5u",
            MinistrySubjectCode: "035",
            MinistryQuestionPaperCode: paperCode,
            Units: null,
            Year: year,
            Season: season,
            Moed: null,
            Stream: stream,
            DefaultTopicId: null,
            SourceFilename: filename,
            SourcePdfId: "pdf-deadbeef",
            UploadedBy: "admin-1",
            IngestedAt: new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero));

    private static IngestionDraftQuestion Draft(int page, string prompt, double conf = 0.88)
        => new(
            DraftId: $"d-{page}",
            SourcePage: page,
            Prompt: prompt,
            LatexContent: null,
            AnswerChoices: Array.Empty<string>(),
            CorrectAnswer: null,
            ExamCode: "bagrut-math-5u",
            FigureSpecJson: null,
            ExtractionConfidence: conf,
            ReviewNotes: Array.Empty<string>());

    [Fact]
    public void Extract_produces_one_corpus_item_per_non_empty_draft()
    {
        var drafts = new[]
        {
            Draft(1, "פתרו את המשוואה 2x + 3 = 7"),
            Draft(2, "נגזרת של f(x) = x^2"),
            Draft(3, "חשב את ∫x dx"),
        };

        var items = BagrutCorpusExtractor.Extract(drafts, NewContext());

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.StartsWith("bagrut-corpus:035:035581:", i.Id));
        Assert.Equal(new[] { 1, 2, 3 }, items.Select(i => i.QuestionNumber));

        // Deterministic ids — same input → same ids, so re-ingest is idempotent.
        var again = BagrutCorpusExtractor.Extract(drafts, NewContext());
        Assert.Equal(items.Select(i => i.Id), again.Select(i => i.Id));
    }

    [Fact]
    public void Extract_normalises_stem_for_similarity_checker()
    {
        var drafts = new[]
        {
            Draft(1, "  Solve:   x + 1 = 0.  "),
        };

        var items = BagrutCorpusExtractor.Extract(drafts, NewContext());
        var item = Assert.Single(items);
        // Lowercase + punctuation stripped + whitespace collapsed.
        Assert.Equal("solve x 1 0", item.NormalisedStem);
    }

    // (b) Year/season/moed heuristics.
    [Fact]
    public void Extract_infers_year_from_filename()
    {
        var drafts = new[] { Draft(1, "placeholder question text") };
        var items = BagrutCorpusExtractor.Extract(
            drafts,
            NewContext(filename: "bagrut-math-5u-2024-summer-moedA.pdf"));
        Assert.Equal(2024, Assert.Single(items).Year);
    }

    [Fact]
    public void Extract_infers_season_from_hebrew_text()
    {
        var drafts = new[] { Draft(1, "בחינת בגרות במתמטיקה — קיץ תשפ\"ד") };
        var items = BagrutCorpusExtractor.Extract(drafts, NewContext());
        Assert.Equal(BagrutCorpusSeason.Summer, Assert.Single(items).Season);
    }

    [Fact]
    public void Extract_infers_moed_from_hebrew_text()
    {
        var drafts = new[] { Draft(1, "מועד א שאלה 1") };
        var items = BagrutCorpusExtractor.Extract(drafts, NewContext());
        Assert.Equal("A", Assert.Single(items).Moed);
    }

    // (c) Stream inference.
    [Fact]
    public void Extract_infers_arab_stream_from_filename()
    {
        var drafts = new[] { Draft(1, "placeholder question text") };
        var items = BagrutCorpusExtractor.Extract(
            drafts,
            NewContext(filename: "bagrut-math-5u-arab-stream-2024.pdf"));
        Assert.Equal(BagrutCorpusStream.Arab, Assert.Single(items).Stream);
    }

    [Fact]
    public void Extract_defaults_to_hebrew_stream_when_unlabeled()
    {
        var drafts = new[] { Draft(1, "simple question") };
        var items = BagrutCorpusExtractor.Extract(drafts, NewContext());
        Assert.Equal(BagrutCorpusStream.Hebrew, Assert.Single(items).Stream);
    }

    // (d) Units inference from paper code.
    [Theory]
    [InlineData("035371", 3)]
    [InlineData("035372", 3)]
    [InlineData("035471", 4)]
    [InlineData("035581", 5)]
    [InlineData("035582", 5)]
    [InlineData("035583", 5)]
    [InlineData("035999", 0)]   // unknown unit digit — extractor returns 0; curator fills in.
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    public void InferUnitsFromPaperCode_extracts_prefix_digit(string code, int expected)
    {
        Assert.Equal(expected, BagrutCorpusExtractor.InferUnitsFromPaperCode(code));
    }

    // (e) Empty drafts.
    [Fact]
    public void Extract_empty_drafts_returns_empty()
    {
        var items = BagrutCorpusExtractor.Extract(Array.Empty<IngestionDraftQuestion>(), NewContext());
        Assert.Empty(items);
    }

    // (f) Empty-prompt drafts skipped.
    [Fact]
    public void Extract_skips_drafts_with_blank_prompt_and_no_latex()
    {
        var drafts = new[]
        {
            Draft(1, ""),
            Draft(2, "   "),
            Draft(3, "real question text here"),
        };
        var items = BagrutCorpusExtractor.Extract(drafts, NewContext());
        // Only the real-text item should have a normalised stem long enough.
        var item = Assert.Single(items);
        Assert.Equal(3, item.QuestionNumber);
    }

    [Fact]
    public void Extract_uses_context_units_when_provided_over_heuristic()
    {
        var drafts = new[] { Draft(1, "placeholder text") };
        var ctx = NewContext(paperCode: "035999") with { Units = 5 };
        var item = Assert.Single(BagrutCorpusExtractor.Extract(drafts, ctx));
        Assert.Equal(5, item.Units);
        Assert.Equal("5U", item.TrackKey);
    }

    [Fact]
    public void Extract_carries_ingest_metadata()
    {
        var drafts = new[] { Draft(1, "placeholder text") };
        var ctx = NewContext() with { DefaultTopicId = "algebra.quadratics" };
        var item = Assert.Single(BagrutCorpusExtractor.Extract(drafts, ctx));

        Assert.Equal("algebra.quadratics", item.TopicId);
        Assert.Equal("pdf-deadbeef", item.SourcePdfId);
        Assert.Equal("admin-1", item.IngestedBy);
        Assert.Equal(ctx.IngestedAt, item.IngestedAt);
    }
}
