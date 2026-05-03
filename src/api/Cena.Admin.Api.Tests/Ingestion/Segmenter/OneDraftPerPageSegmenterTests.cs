// =============================================================================
// Cena Platform — OneDraftPerPageSegmenter tests
//
// Pins the legacy fallback behaviour: one segment per populated page,
// blank pages skipped, label=null, confidence inherited from the OCR page.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Segmenter;

namespace Cena.Admin.Api.Tests.Ingestion.Segmenter;

public sealed class OneDraftPerPageSegmenterTests
{
    [Fact]
    public async Task Segment_OnePerPopulatedPage_SkipsBlankPages()
    {
        var pages = new[]
        {
            new ExtractedPage(1, "page 1 text", null, Array.Empty<ExtractedFigure>(), 0.9),
            new ExtractedPage(2, "", null, Array.Empty<ExtractedFigure>(), 0.7),       // blank — skipped
            new ExtractedPage(3, "   ", "   ", Array.Empty<ExtractedFigure>(), 0.8), // empty text + whitespace-only latex — skipped
            new ExtractedPage(4, "", "x^2 + 1", Array.Empty<ExtractedFigure>(), 0.85),  // latex-only — kept
            new ExtractedPage(5, "page 5 text", null, Array.Empty<ExtractedFigure>(), 0.95),
        };

        var seg = new OneDraftPerPageSegmenter();
        var segments = await seg.SegmentAsync(pages, "exam-x", "pdf-y");

        // Pages 2 and 3 are skipped; pages 1, 4, 5 each yield one segment.
        Assert.Equal(3, segments.Count);
        Assert.Equal(1, segments[0].StartPage);
        Assert.Equal(1, segments[0].EndPage);
        Assert.Null(segments[0].QuestionLabel);
        Assert.Equal(0.9, segments[0].Confidence, 3);

        Assert.Equal(4, segments[1].StartPage);
        Assert.Equal(0.85, segments[1].Confidence, 3);

        Assert.Equal(5, segments[2].StartPage);
    }

    [Fact]
    public async Task Segment_NoPages_ReturnsEmpty()
    {
        var seg = new OneDraftPerPageSegmenter();
        var segments = await seg.SegmentAsync(Array.Empty<ExtractedPage>(), "exam-x", "pdf-y");
        Assert.Empty(segments);
    }
}
