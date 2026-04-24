// =============================================================================
// Cena Platform — Layer 4 Confidence Gate (ADR-0033)
//
// Inspects per-region confidence. Regions below τ get rescued via the
// corresponding cloud runner (Mathpix for math, Gemini for text). To rescue,
// Layer 4 crops the bbox from the appropriate preprocessed page bytes via
// ImageSharp, then hands the crop to the runner.
//
// If a runner's circuit breaker is open (OcrCircuitOpenException) we pass
// the original block through untouched — a degraded cascade is still a
// functional one. Same for any transient runner exception.
//
// Runners are optional — if IMathpixRunner isn't registered, math rescue
// is skipped. Same for text.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Runners;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class Layer4ConfidenceGate : ILayer4ConfidenceGate
{
    private readonly ConfidenceGateOptions _options;
    private readonly IMathpixRunner? _mathpix;
    private readonly IGeminiVisionRunner? _gemini;
    private readonly ILogger<Layer4ConfidenceGate>? _log;

    public Layer4ConfidenceGate(
        ConfidenceGateOptions options,
        IMathpixRunner? mathpix = null,
        IGeminiVisionRunner? gemini = null,
        ILogger<Layer4ConfidenceGate>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mathpix = mathpix;
        _gemini = gemini;
        _log = log;
    }

    public async Task<Layer4Output> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<OcrTextBlock> textBlocks,
        IReadOnlyList<OcrMathBlock> mathBlocks,
        CascadeSurface surface,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var fallbacks = new List<string>();

        var rescuedMath = await RescueMathAsync(pageBytes, mathBlocks, fallbacks, ct).ConfigureAwait(false);
        var rescuedText = await RescueTextAsync(pageBytes, textBlocks, fallbacks, ct).ConfigureAwait(false);

        double avg = ComputeAverageConfidence(rescuedText, rescuedMath);
        bool catastrophic = avg < CatastrophicThresholdFor(surface);

        sw.Stop();
        _log?.LogDebug(
            "[OCR_CASCADE] Layer4 surface={Surface} tau={Tau} avg={Avg:F3} " +
            "fallbacks={Fallbacks} catastrophic={Catastrophic} latencyMs={Latency}",
            surface, _options.ConfidenceThreshold, avg, fallbacks.Count,
            catastrophic, sw.Elapsed.TotalMilliseconds);

        return new Layer4Output(
            TextBlocks: rescuedText,
            MathBlocks: rescuedMath,
            FallbacksFired: fallbacks,
            AverageConfidence: avg,
            CatastrophicFailure: catastrophic,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // Math rescue
    // -------------------------------------------------------------------------
    private async Task<IReadOnlyList<OcrMathBlock>> RescueMathAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<OcrMathBlock> blocks,
        List<string> fallbacks,
        CancellationToken ct)
    {
        if (blocks.Count == 0) return Array.Empty<OcrMathBlock>();

        var result = new List<OcrMathBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            if (block.Confidence >= _options.ConfidenceThreshold || _mathpix is null)
            {
                result.Add(block);
                continue;
            }

            if (!TryCropRegion(pageBytes, block.Bbox, out var crop))
            {
                // No crop available → nothing to send Mathpix. Pass original through.
                result.Add(block);
                _log?.LogDebug(
                    "[OCR_CASCADE] Layer4a skipping math rescue — bbox unavailable or page missing");
                continue;
            }

            try
            {
                var rescued = await _mathpix.RescueMathAsync(crop, block, ct).ConfigureAwait(false);
                result.Add(rescued);
                fallbacks.Add($"mathpix:{Truncate(block.Latex)}");
            }
            catch (OcrCircuitOpenException)
            {
                result.Add(block);
                _log?.LogInformation(
                    "[OCR_CASCADE] Layer4a Mathpix circuit open — passing math block through untouched");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Add(block);
                _log?.LogWarning(ex,
                    "[OCR_CASCADE] Layer4a Mathpix rescue failed; passing block through");
            }
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Text rescue
    // -------------------------------------------------------------------------
    private async Task<IReadOnlyList<OcrTextBlock>> RescueTextAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<OcrTextBlock> blocks,
        List<string> fallbacks,
        CancellationToken ct)
    {
        if (blocks.Count == 0) return Array.Empty<OcrTextBlock>();

        var result = new List<OcrTextBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            if (block.Confidence >= _options.ConfidenceThreshold || _gemini is null)
            {
                result.Add(block);
                continue;
            }

            if (!TryCropRegion(pageBytes, block.Bbox, out var crop))
            {
                result.Add(block);
                _log?.LogDebug(
                    "[OCR_CASCADE] Layer4b skipping text rescue — bbox unavailable or page missing");
                continue;
            }

            try
            {
                var rescued = await _gemini.RescueTextAsync(crop, block, ct).ConfigureAwait(false);
                result.Add(rescued);
                fallbacks.Add($"gemini:{Truncate(block.Text)}");
            }
            catch (OcrCircuitOpenException)
            {
                result.Add(block);
                _log?.LogInformation(
                    "[OCR_CASCADE] Layer4b Gemini circuit open — passing text block through untouched");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Add(block);
                _log?.LogWarning(ex,
                    "[OCR_CASCADE] Layer4b Gemini rescue failed; passing block through");
            }
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Image cropping — ImageSharp-based. Returns PNG bytes.
    // -------------------------------------------------------------------------
    private bool TryCropRegion(
        IReadOnlyList<byte[]> pageBytes,
        BoundingBox? bbox,
        out ReadOnlyMemory<byte> crop)
    {
        crop = default;
        if (bbox is null) return false;

        // bbox.Page is 1-indexed (0 = unknown); normalise to 0-indexed.
        int pageIdx = Math.Max(0, bbox.Page - 1);
        if (pageIdx >= pageBytes.Count) return false;

        var bytes = pageBytes[pageIdx];
        if (bytes.Length == 0) return false;

        try
        {
            using var img = Image.Load(bytes);

            // Clamp the crop rectangle to image bounds so malformed bboxes
            // don't blow up the rescue path.
            int x = Math.Clamp((int)bbox.X, 0, Math.Max(0, img.Width - 1));
            int y = Math.Clamp((int)bbox.Y, 0, Math.Max(0, img.Height - 1));
            int w = Math.Clamp((int)bbox.W, 1, Math.Max(1, img.Width - x));
            int h = Math.Clamp((int)bbox.H, 1, Math.Max(1, img.Height - y));

            img.Mutate(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

            using var ms = new MemoryStream();
            img.SaveAsPng(ms, new PngEncoder());
            crop = ms.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex,
                "[OCR_CASCADE] Layer4 crop failed for bbox {Bbox}", bbox);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static double ComputeAverageConfidence(
        IReadOnlyList<OcrTextBlock> textBlocks,
        IReadOnlyList<OcrMathBlock> mathBlocks)
    {
        int count = textBlocks.Count + mathBlocks.Count;
        if (count == 0) return 0.0;

        double sum = 0;
        foreach (var t in textBlocks) sum += t.Confidence;
        foreach (var m in mathBlocks) sum += m.Confidence;
        return sum / count;
    }

    private double CatastrophicThresholdFor(CascadeSurface surface) =>
        surface switch
        {
            CascadeSurface.StudentInteractive => _options.StudentCatastrophicThreshold,
            CascadeSurface.AdminBatch => _options.AdminCatastrophicThreshold,
            _ => _options.StudentCatastrophicThreshold,
        };

    private string Truncate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        int max = Math.Max(0, _options.FallbackLabelTruncation);
        return s.Length <= max ? s : s[..max];
    }
}
