// =============================================================================
// Cena Platform — Layer 3 Reassembly (ADR-0033)
//
// Pure-C# implementation of the RTL-aware reading-order stitch. Ports
// scripts/ocr-spike/pipeline_prototype.py layer_3_reassemble():
//
//   1. Bucket text blocks by y-coordinate (40-pixel rows)
//   2. Sort rows top-to-bottom
//   3. Within each row, if the majority of blocks are RTL (Hebrew), reverse
//      the x-order so they read right-to-left
//   4. Math blocks and figures pass through unchanged — they already carry
//      bounding boxes the downstream renderer uses for positioning
//
// No external deps. Stateless. Testable in isolation.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class Layer3Reassemble : ILayer3Reassemble
{
    /// <summary>
    /// Height of each y-bucket in the row-grouping pass. 40 px matches the
    /// Python reference; tuned for 11–14 pt body text at 300 DPI. Don't
    /// change without re-tuning against the fixture set.
    /// </summary>
    internal const int RowBucketHeight = 40;

    public Layer3Output Run(
        IReadOnlyList<OcrTextBlock> textBlocks,
        IReadOnlyList<OcrMathBlock> mathBlocks,
        IReadOnlyList<OcrFigureRef> figures)
    {
        var sw = Stopwatch.StartNew();

        var ordered = BucketAndOrder(textBlocks);

        sw.Stop();
        return new Layer3Output(
            OrderedTextBlocks: ordered,
            OrderedMathBlocks: mathBlocks,
            Figures: figures,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // Internal helpers — exposed as internal so the test project can exercise
    // them directly without going through the ILayer3Reassemble contract.
    // -------------------------------------------------------------------------
    internal static IReadOnlyList<OcrTextBlock> BucketAndOrder(
        IReadOnlyList<OcrTextBlock> blocks)
    {
        if (blocks.Count == 0)
            return Array.Empty<OcrTextBlock>();

        // SortedDictionary keeps the bucket keys in ascending y order so we
        // walk rows top-to-bottom without an extra sort pass.
        var rows = new SortedDictionary<int, List<OcrTextBlock>>();
        foreach (var block in blocks)
        {
            int bucket = BucketKey(block.Bbox);
            if (!rows.TryGetValue(bucket, out var row))
            {
                row = new List<OcrTextBlock>();
                rows[bucket] = row;
            }
            row.Add(block);
        }

        var ordered = new List<OcrTextBlock>(blocks.Count);
        foreach (var row in rows.Values)
        {
            OrderRow(row);
            ordered.AddRange(row);
        }
        return ordered;
    }

    private static int BucketKey(BoundingBox? bbox)
    {
        if (bbox is null) return 0;
        // Floor-divide-by-bucket * bucket — matches Python `(y // 40) * 40`.
        // Works for negative y too, though bboxes should be non-negative.
        return (int)Math.Floor(bbox.Y / (double)RowBucketHeight) * RowBucketHeight;
    }

    /// <summary>
    /// In-place sort of a single row by x. If the majority of blocks in the
    /// row are RTL, we flip the x-ordering so Hebrew reads right-to-left.
    /// Ties broken stably (OrderBy is stable via LINQ, but List.Sort isn't —
    /// we use a deterministic secondary key off the original list index).
    /// </summary>
    internal static void OrderRow(List<OcrTextBlock> row)
    {
        if (row.Count <= 1) return;

        int rtlCount = 0;
        foreach (var b in row) if (b.IsRtl) rtlCount++;
        bool reverseX = rtlCount * 2 > row.Count;  // strictly > half

        // Capture original indices for stable tie-breaking
        var indexed = new (OcrTextBlock block, int idx)[row.Count];
        for (int i = 0; i < row.Count; i++) indexed[i] = (row[i], i);

        Array.Sort(indexed, (a, b) =>
        {
            double ax = a.block.Bbox?.X ?? 0;
            double bx = b.block.Bbox?.X ?? 0;
            int cmp = reverseX ? bx.CompareTo(ax) : ax.CompareTo(bx);
            return cmp != 0 ? cmp : a.idx.CompareTo(b.idx);
        });

        for (int i = 0; i < row.Count; i++) row[i] = indexed[i].block;
    }
}
