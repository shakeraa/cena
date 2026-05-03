// =============================================================================
// Cena Platform — BagrutPdfIngestionService × IBagrutQuestionSegmenter
// integration tests.
//
// Pins the wiring contract: the segmenter is the SINGLE point of policy for
// question-boundary detection; the ingestion service materialises one draft
// per segment (not per page).
//
// Two integration tests:
//   1. 6-page PDF + segmenter returns 4 segments (covering pages 3..6) →
//      ingestion produces exactly 4 drafts; pages 1-2 (cover + "answer 5
//      of 8" preamble) produce NO draft. This is the user-reported defect
//      from 2026-05-03 (35581-q.pdf) inverted into a passing test.
//   2. Multi-page segment (pages 3-4 belong to the same question) →
//      ingestion produces ONE draft whose Prompt concatenates text from
//      both pages; figures from both pages land in figureSpec.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Segmenter;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion.Segmenter;

public sealed class BagrutPdfIngestionServiceSegmenterIntegrationTests
{
    [Fact]
    public async Task SegmenterReturns4SegmentsFor6PagePdf_IngestionCreates4Drafts()
    {
        // 6-page PDF: pages 1-2 are cover + "answer N of M", pages 3-6 are
        // the actual questions. Segmenter returns 4 single-page segments.
        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        // OCR result: 6 pages of text (covering pages 1..6 each populated).
        var blocks = new List<OcrTextBlock>();
        for (var p = 1; p <= 6; p++)
        {
            blocks.Add(new OcrTextBlock(
                $"Page {p} text — שאלה {p}",
                new BoundingBox(0, 0, 500, 100, Page: p),
                Language.Hebrew,
                0.92,
                IsRtl: true));
        }
        var ocr = new OcrCascadeResult(
            SchemaVersion: "1.0",
            Runner: "test", Source: "unit", Hints: null, PdfTriage: null,
            TextBlocks: blocks,
            MathBlocks: Array.Empty<OcrMathBlock>(),
            Figures: Array.Empty<OcrFigureRef>(),
            OverallConfidence: 0.92,
            FallbacksFired: Array.Empty<string>(),
            CasValidatedMath: 0, CasFailedMath: 0,
            HumanReviewRequired: false,
            ReasonsForReview: Array.Empty<string>(),
            LayerTimingsSeconds: new Dictionary<string, double>(),
            TotalLatencySeconds: 0.5,
            CapturedAt: "2026-05-03T00:00:00Z");

        cascade.RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>())
            .Returns(ocr);

        var segmenter = new StaticSegmenter(new[]
        {
            new BagrutQuestionSegment(3, 3, "שאלה 1", 0.95),
            new BagrutQuestionSegment(4, 4, "שאלה 2", 0.95),
            new BagrutQuestionSegment(5, 5, "שאלה 3", 0.95),
            new BagrutQuestionSegment(6, 6, "שאלה 4", 0.92),
        });

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter, NullLogger<BagrutPdfIngestionService>.Instance);

        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.7\nfake-bagrut");
        var result = await service.IngestAsync(pdfBytes, "math-5u-2024-summer", "curator@cena.dev");

        // 6 OCR pages → 4 drafts (cover + preamble pages dropped).
        Assert.Equal(6, result.TotalPages);
        Assert.Equal(4, result.QuestionsExtracted);
        Assert.Equal(4, result.Drafts.Count);

        // First draft maps to page 3 (the first real question), NOT page 1.
        Assert.Equal(3, result.Drafts[0].SourcePage);
        Assert.Equal("draft-math-5u-2024-summer-p3", result.Drafts[0].DraftId);
        Assert.Contains("שאלה 1", result.Drafts[0].ReviewNotes.First(n => n.StartsWith("segmenter-label:", StringComparison.Ordinal)));

        Assert.Equal(6, result.Drafts[3].SourcePage);
        Assert.Equal("draft-math-5u-2024-summer-p6", result.Drafts[3].DraftId);
    }

    [Fact]
    public async Task SegmenterReturnsMultiPageSegment_IngestionConcatenatesPagesIntoOneDraft()
    {
        // Question 1 spans pages 3-4 (problem statement + figure on p3,
        // sub-parts on p4). Segmenter returns ONE segment with start=3,
        // end=4. Materialiser concatenates raw text from both pages into a
        // single draft.
        var cascade = Substitute.For<IOcrCascadeService>();
        var pdfStore = Substitute.For<IBagrutPdfStore>();

        var blocks = new List<OcrTextBlock>
        {
            new("שאלה 1: ניתנת הפונקציה",
                new BoundingBox(0, 0, 500, 100, Page: 3), Language.Hebrew, 0.93, IsRtl: true),
            new("א. מצא את ערך מקסימום",
                new BoundingBox(0, 100, 500, 100, Page: 4), Language.Hebrew, 0.91, IsRtl: true),
            new("ב. הוכח כי",
                new BoundingBox(0, 200, 500, 100, Page: 4), Language.Hebrew, 0.90, IsRtl: true),
        };
        var figures = new[]
        {
            new OcrFigureRef(
                new BoundingBox(50, 50, 200, 200, Page: 3),
                "graph",
                "/tmp/p3-fig.png",
                null),
        };
        var ocr = new OcrCascadeResult(
            SchemaVersion: "1.0", Runner: "test", Source: "unit", Hints: null, PdfTriage: null,
            TextBlocks: blocks,
            MathBlocks: Array.Empty<OcrMathBlock>(),
            Figures: figures,
            OverallConfidence: 0.92,
            FallbacksFired: Array.Empty<string>(),
            CasValidatedMath: 0, CasFailedMath: 0,
            HumanReviewRequired: false,
            ReasonsForReview: Array.Empty<string>(),
            LayerTimingsSeconds: new Dictionary<string, double>(),
            TotalLatencySeconds: 0.5,
            CapturedAt: "2026-05-03T00:00:00Z");

        cascade.RecognizeAsync(
            Arg.Any<ReadOnlyMemory<byte>>(), "application/pdf",
            Arg.Any<OcrContextHints?>(), CascadeSurface.AdminBatch,
            Arg.Any<CancellationToken>())
            .Returns(ocr);

        var segmenter = new StaticSegmenter(new[]
        {
            new BagrutQuestionSegment(3, 4, "שאלה 1", 0.85),
        });

        var service = new BagrutPdfIngestionService(
            cascade, pdfStore, segmenter, NullLogger<BagrutPdfIngestionService>.Instance);

        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.7\nfake-bagrut-multipage");
        var result = await service.IngestAsync(pdfBytes, "math-5u-2024-summer", "curator@cena.dev");

        var draft = Assert.Single(result.Drafts);
        Assert.Equal(3, draft.SourcePage);
        Assert.Equal("draft-math-5u-2024-summer-p3", draft.DraftId);

        // Prompt contains text from BOTH p3 and p4.
        Assert.Contains("שאלה 1", draft.Prompt);
        Assert.Contains("מצא את ערך מקסימום", draft.Prompt);
        Assert.Contains("הוכח כי", draft.Prompt);

        // Multi-page note attached.
        Assert.Contains(draft.ReviewNotes, n => n.Contains("multi-page-segment:3-4"));

        // Figure from p3 lands in figureSpec.
        Assert.NotNull(draft.FigureSpecJson);
        Assert.Contains("/tmp/p3-fig.png", draft.FigureSpecJson);
    }

    /// <summary>
    /// Test double — returns a fixed segment list regardless of input. Used
    /// by the integration tests to drive the materialiser without standing
    /// up the full LLM-tier segmenter or its fakes.
    /// </summary>
    private sealed class StaticSegmenter : IBagrutQuestionSegmenter
    {
        private readonly IReadOnlyList<BagrutQuestionSegment> _segments;
        public StaticSegmenter(IReadOnlyList<BagrutQuestionSegment> segments) { _segments = segments; }
        public Task<IReadOnlyList<BagrutQuestionSegment>> SegmentAsync(
            IReadOnlyList<ExtractedPage> pages, string examCode, string pdfId,
            CancellationToken ct = default)
            => Task.FromResult(_segments);
    }
}
