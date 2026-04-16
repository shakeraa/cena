// =============================================================================
// Cena Platform — Layer3Reassemble tests
//
// Pure-C# logic — no external deps. Exercises the RTL-aware row ordering,
// the y-bucket grouping, and the null-bbox fallback.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;

namespace Cena.Infrastructure.Tests.Ocr;

public class Layer3ReassembleTests
{
    private readonly ILayer3Reassemble _layer = new Layer3Reassemble();

    private static OcrTextBlock Text(
        string body, double x, double y,
        bool isRtl = false,
        double confidence = 0.9,
        double w = 40, double h = 20)
        => new(
            Text: body,
            Bbox: new BoundingBox(x, y, w, h),
            Language: isRtl ? Language.Hebrew : Language.English,
            Confidence: confidence,
            IsRtl: isRtl);

    [Fact]
    public void Empty_Input_Returns_Empty_Output()
    {
        var result = _layer.Run(
            Array.Empty<OcrTextBlock>(),
            Array.Empty<OcrMathBlock>(),
            Array.Empty<OcrFigureRef>());

        Assert.Empty(result.OrderedTextBlocks);
        Assert.Empty(result.OrderedMathBlocks);
        Assert.Empty(result.Figures);
        Assert.True(result.LatencySeconds >= 0);
    }

    [Fact]
    public void LTR_Row_Reads_Left_To_Right()
    {
        var blocks = new[]
        {
            Text("world", x: 100, y: 50),
            Text("hello", x: 10, y: 50),
            Text("there", x: 60, y: 50),
        };
        var result = _layer.Run(blocks, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        var order = result.OrderedTextBlocks.Select(b => b.Text).ToArray();
        Assert.Equal(new[] { "hello", "there", "world" }, order);
    }

    [Fact]
    public void RTL_Row_Reads_Right_To_Left()
    {
        // All three blocks are Hebrew → majority RTL → reverse x order
        var blocks = new[]
        {
            Text("ראשון", x: 200, y: 50, isRtl: true),    // rightmost (reads first)
            Text("שני", x: 120, y: 50, isRtl: true),
            Text("שלישי", x: 40, y: 50, isRtl: true),    // leftmost (reads last)
        };
        var result = _layer.Run(blocks, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        var order = result.OrderedTextBlocks.Select(b => b.Text).ToArray();
        Assert.Equal(new[] { "ראשון", "שני", "שלישי" }, order);
    }

    [Fact]
    public void Mixed_Row_With_RTL_Majority_Flips_Order()
    {
        // Two RTL + one LTR → majority RTL → whole row flips
        var blocks = new[]
        {
            Text("LTR", x: 250, y: 50, isRtl: false),
            Text("ראשון", x: 150, y: 50, isRtl: true),
            Text("שני", x: 50, y: 50, isRtl: true),
        };
        var result = _layer.Run(blocks, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        var order = result.OrderedTextBlocks.Select(b => b.Text).ToArray();
        Assert.Equal(new[] { "LTR", "ראשון", "שני" }, order);
    }

    [Fact]
    public void Mixed_Row_With_LTR_Majority_Keeps_Ltr_Order()
    {
        // Two LTR + one RTL → majority LTR → read left-to-right
        var blocks = new[]
        {
            Text("third", x: 200, y: 50, isRtl: false),
            Text("ראשון", x: 100, y: 50, isRtl: true),
            Text("first", x: 10, y: 50, isRtl: false),
        };
        var result = _layer.Run(blocks, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        var order = result.OrderedTextBlocks.Select(b => b.Text).ToArray();
        Assert.Equal(new[] { "first", "ראשון", "third" }, order);
    }

    [Fact]
    public void Rows_Are_Ordered_Top_To_Bottom()
    {
        var blocks = new[]
        {
            Text("row2-a", x: 10, y: 120),   // bucket 120
            Text("row1-a", x: 10, y: 40),    // bucket 40
            Text("row3-a", x: 10, y: 200),   // bucket 200
        };
        var result = _layer.Run(blocks, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        var order = result.OrderedTextBlocks.Select(b => b.Text).ToArray();
        Assert.Equal(new[] { "row1-a", "row2-a", "row3-a" }, order);
    }

    [Fact]
    public void Blocks_Within_40px_Are_Grouped_Into_Same_Row()
    {
        // y=10 and y=35 fall in bucket 0; y=55 falls in bucket 40.
        var blocks = new[]
        {
            Text("A", x: 200, y: 10),   // bucket 0
            Text("B", x: 100, y: 35),   // bucket 0 (same row)
            Text("C", x: 50, y: 55),    // bucket 40 (next row)
        };
        var result = _layer.Run(blocks, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        var order = result.OrderedTextBlocks.Select(b => b.Text).ToArray();
        // Row 1 (bucket 0, LTR): B (x=100) comes before A (x=200)
        // Row 2 (bucket 40): C alone
        Assert.Equal(new[] { "B", "A", "C" }, order);
    }

    [Fact]
    public void Null_Bboxes_Fall_Into_Bucket_Zero_And_Preserve_Insertion_Order()
    {
        // Stable tie-breaking on original index keeps input order for ties.
        var a = new OcrTextBlock("A", Bbox: null, Language.English, 0.9, IsRtl: false);
        var b = new OcrTextBlock("B", Bbox: null, Language.English, 0.9, IsRtl: false);
        var c = new OcrTextBlock("C", Bbox: null, Language.English, 0.9, IsRtl: false);

        var result = _layer.Run(new[] { a, b, c }, Array.Empty<OcrMathBlock>(), Array.Empty<OcrFigureRef>());

        Assert.Equal(new[] { "A", "B", "C" }, result.OrderedTextBlocks.Select(x => x.Text));
    }

    [Fact]
    public void Math_Blocks_And_Figures_Pass_Through_Unchanged()
    {
        var math = new[]
        {
            new OcrMathBlock("3x+5=14", null, 0.85, SympyParsed: false, CanonicalForm: null),
        };
        var figures = new[]
        {
            new OcrFigureRef(new BoundingBox(10, 10, 100, 100), "figure", null, null),
        };

        var result = _layer.Run(Array.Empty<OcrTextBlock>(), math, figures);

        Assert.Equal(math.Length, result.OrderedMathBlocks.Count);
        Assert.Equal("3x+5=14", result.OrderedMathBlocks[0].Latex);
        Assert.Equal(figures.Length, result.Figures.Count);
        Assert.Equal(figures[0].Bbox.W, result.Figures[0].Bbox.W);
    }
}
