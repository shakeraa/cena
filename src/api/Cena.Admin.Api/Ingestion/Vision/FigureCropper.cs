// =============================================================================
// Cena Platform — FigureCropper (vision-extractor branch)
//
// Crops figures out of a rendered page PNG given the bounding boxes the
// vision-LLM returned. Mirrors the on-disk shape of the legacy
// Layer2cFigureExtraction (content-addressable hex filename, atomic
// tmp+Move write) so the existing FigureSpec-resolving endpoint
// (VisualReviewEndpoints) and the curator's figure thumbnail render
// without changes.
//
// Output layout:
//   {OutputDirectory}/p{N:D3}-fig{i:D3}-{hash[:16]}.png
// where N is the 1-based page number and i is the 0-based figure index
// on that page. The hash prefix prevents accidental collisions across
// re-ingestions of subtly-different uploads of the same exam (a re-scan
// at a different DPI yields different bytes → different hash → different
// filename → no overwrite of the prior version).
//
// Bounding-box hardening:
//   - Out-of-range coords (negative, larger than the page) are CLAMPED to
//     the image bounds. The vision-LLM occasionally hallucinates a region
//     that runs off the page; clamping is preferable to dropping because
//     a slightly-off box still gives the curator a usable crop. A box that
//     clamps to zero area is dropped (cropper returns false for that one).
//   - Boxes whose clamped area is < a few pixels are dropped — the model
//     "saw" an artifact, not a figure.
//
// Returns one record per cropped figure, in the same order as the input
// list, so the caller can build FigureSpecJson preserving the model's
// figure ordering.
// =============================================================================

using System.Security.Cryptography;
using Cena.Infrastructure.Ocr.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Cena.Admin.Api.Ingestion.Vision;

/// <summary>
/// One cropped figure's on-disk record. The path is absolute and stays under
/// the configured FigureStorageOptions root; the bbox is the clamped region
/// (may differ from the model's raw output if it was out-of-bounds).
/// </summary>
public sealed record CroppedFigureRecord(
    int PageNumber,
    int FigureIndex,
    int X, int Y, int Width, int Height,
    string CroppedPath,
    string Kind,
    string? AltText);

/// <summary>
/// Crops detected figures from rendered page PNGs and persists each crop.
/// Single-purpose so the test surface is tight (no Marten, no LLM, no
/// network).
/// </summary>
public interface IFigureCropper
{
    /// <summary>
    /// Crop each figure region out of <paramref name="pagePngPath"/> and
    /// persist under FigureStorageOptions.OutputDirectory.
    /// </summary>
    /// <returns>One record per successfully cropped figure (skipped boxes
    /// are silently omitted — the caller already knows they were requested
    /// because the input list is its own data).</returns>
    Task<IReadOnlyList<CroppedFigureRecord>> CropAsync(
        string pagePngPath,
        int pageNumber,
        string pdfId,
        IReadOnlyList<DetectedFigure> figures,
        CancellationToken ct = default);
}

public sealed class FigureCropper : IFigureCropper
{
    /// <summary>
    /// Minimum cropped area (in pixels²) for a figure to be persisted.
    /// Below this threshold the model usually "saw" an artifact (a thin
    /// rule, a stray glyph) — we drop rather than write zero-byte PNGs.
    /// </summary>
    private const int MinAreaPx = 40 * 40;

    private readonly FigureStorageOptions _options;
    private readonly ILogger<FigureCropper>? _log;
    private readonly object _dirGate = new();
    private bool _dirEnsured;

    public FigureCropper(
        IOptions<FigureStorageOptions>? options = null,
        ILogger<FigureCropper>? log = null)
    {
        _options = options?.Value ?? new FigureStorageOptions();
        _log = log;
    }

    public Task<IReadOnlyList<CroppedFigureRecord>> CropAsync(
        string pagePngPath,
        int pageNumber,
        string pdfId,
        IReadOnlyList<DetectedFigure> figures,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePngPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfId);
        ArgumentNullException.ThrowIfNull(figures);

        if (figures.Count == 0)
            return Task.FromResult<IReadOnlyList<CroppedFigureRecord>>(Array.Empty<CroppedFigureRecord>());

        if (!File.Exists(pagePngPath))
        {
            _log?.LogWarning(
                "FigureCropper: page PNG missing pdf={PdfId} page={Page} path={Path}",
                pdfId, pageNumber, pagePngPath);
            return Task.FromResult<IReadOnlyList<CroppedFigureRecord>>(Array.Empty<CroppedFigureRecord>());
        }

        EnsureOutputDirectory();

        var results = new List<CroppedFigureRecord>(figures.Count);
        using var img = Image.Load(pagePngPath);

        for (var i = 0; i < figures.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var fig = figures[i];
            try
            {
                if (TryCropOne(img, pageNumber, i, pdfId, fig, out var record))
                    results.Add(record);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log?.LogWarning(ex,
                    "FigureCropper: crop failed pdf={PdfId} page={Page} figIndex={FigIndex}",
                    pdfId, pageNumber, i);
            }
        }

        return Task.FromResult<IReadOnlyList<CroppedFigureRecord>>(results);
    }

    private bool TryCropOne(
        Image sourceImg,
        int pageNumber,
        int figureIndex,
        string pdfId,
        DetectedFigure fig,
        out CroppedFigureRecord record)
    {
        record = null!;

        // Clamp the bbox into image coords. The model produces doubles;
        // round-toward-bbox-shrink so a fractional value cannot push the
        // region a pixel beyond the page.
        int rawX = (int)Math.Floor(fig.X);
        int rawY = (int)Math.Floor(fig.Y);
        int rawW = (int)Math.Ceiling(fig.Width);
        int rawH = (int)Math.Ceiling(fig.Height);

        int x = Math.Clamp(rawX, 0, Math.Max(0, sourceImg.Width - 1));
        int y = Math.Clamp(rawY, 0, Math.Max(0, sourceImg.Height - 1));
        int w = Math.Clamp(rawW, 1, Math.Max(1, sourceImg.Width - x));
        int h = Math.Clamp(rawH, 1, Math.Max(1, sourceImg.Height - y));

        if ((long)w * h < MinAreaPx)
        {
            _log?.LogDebug(
                "FigureCropper: dropping figure (area below threshold) pdf={PdfId} page={Page} figIndex={FigIndex} area={Area}",
                pdfId, pageNumber, figureIndex, (long)w * h);
            return false;
        }

        // Clone so we don't mutate the shared image used by other figures.
        using var crop = sourceImg.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

        using var ms = new MemoryStream();
        crop.SaveAsPng(ms, new PngEncoder());
        var cropBytes = ms.ToArray();
        if (cropBytes.Length == 0) return false;
        if (cropBytes.Length > _options.MaxFigureBytes)
        {
            _log?.LogDebug(
                "FigureCropper: figure exceeds MaxFigureBytes pdf={PdfId} page={Page} figIndex={FigIndex} size={Size}",
                pdfId, pageNumber, figureIndex, cropBytes.Length);
            return false;
        }

        var hash = Convert.ToHexString(SHA256.HashData(cropBytes)).ToLowerInvariant()[..16];
        var filename = $"p{pageNumber:D3}-fig{figureIndex:D3}-{hash}.png";
        var fullPath = Path.Combine(_options.OutputDirectory, filename);

        var tmp = fullPath + ".tmp";
        File.WriteAllBytes(tmp, cropBytes);
        try
        {
            File.Move(tmp, fullPath, overwrite: true);
        }
        catch (IOException)
        {
            // Concurrent writer raced and won — bytes are identical
            // (content-addressable); discard the duplicate.
            try { File.Delete(tmp); } catch { /* best-effort */ }
        }

        record = new CroppedFigureRecord(
            PageNumber: pageNumber,
            FigureIndex: figureIndex,
            X: x, Y: y, Width: w, Height: h,
            CroppedPath: fullPath,
            Kind: NormaliseKind(fig.Kind),
            AltText: string.IsNullOrWhiteSpace(fig.AltText) ? null : fig.AltText!.Trim());
        return true;
    }

    private static string NormaliseKind(string kind) =>
        kind?.ToLowerInvariant() switch
        {
            "diagram" or "figure" or "picture" or "image" => "figure",
            "chart" or "plot" or "graph" => "plot",
            "table" => "table",
            _ => "figure",
        };

    private void EnsureOutputDirectory()
    {
        if (_dirEnsured) return;
        lock (_dirGate)
        {
            if (_dirEnsured) return;
            Directory.CreateDirectory(_options.OutputDirectory);
            _dirEnsured = true;
        }
    }
}
