// =============================================================================
// Cena Platform — Layer 2c Figure Extraction (ADR-0033)
//
// Crops figure/table/plot regions from preprocessed pages using ImageSharp,
// writes each crop to the configured output directory, and returns
// OcrFigureRef entries carrying bbox + path. Real implementation — no
// stubs, no mocks.
//
// The output directory contract:
//   - Each figure is written as a deterministic filename derived from a
//     content hash of its bytes (SHA-256 prefix).
//   - Callers (the admin UI review queue, the student tutor step-solver)
//     must treat these paths as transient — they live as long as the
//     orchestrating process. Hosts that need durable storage should replace
//     this layer with an S3/GCS-backed Layer2c implementation.
//
// Every layer invocation is stateless and thread-safe: the only shared
// resource is the output directory, which is created on first use and
// only appended to (never read).
// =============================================================================

using System.Diagnostics;
using System.Security.Cryptography;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class Layer2cFigureExtraction : ILayer2cFigureExtraction
{
    private readonly FigureStorageOptions _options;
    private readonly ILogger<Layer2cFigureExtraction>? _log;
    private readonly object _dirGate = new();
    private bool _dirEnsured;

    public Layer2cFigureExtraction(
        FigureStorageOptions? options = null,
        ILogger<Layer2cFigureExtraction>? log = null)
    {
        _options = options ?? new FigureStorageOptions();
        _log = log;
    }

    public async Task<Layer2cOutput> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<LayoutRegion> figureRegions,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (figureRegions.Count == 0 || pageBytes.Count == 0)
        {
            sw.Stop();
            return new Layer2cOutput(
                Figures: Array.Empty<OcrFigureRef>(),
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }

        EnsureOutputDirectory();

        var figures = new List<OcrFigureRef>(figureRegions.Count);
        foreach (var region in figureRegions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (TryCropAndPersist(pageBytes, region, out var fig))
                    figures.Add(fig);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log?.LogWarning(ex,
                    "[OCR_CASCADE] Layer2c failed to extract figure at bbox {Bbox}",
                    region.Bbox);
            }
        }

        // Async boundary — satisfies CA2007 and keeps the interface async-ready
        // for a future blob-storage impl where writing is IO-bound.
        await Task.CompletedTask.ConfigureAwait(false);

        sw.Stop();
        return new Layer2cOutput(
            Figures: figures,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------
    private bool TryCropAndPersist(
        IReadOnlyList<byte[]> pageBytes,
        LayoutRegion region,
        out OcrFigureRef figure)
    {
        figure = null!;

        int pageIdx = Math.Max(0, region.Bbox.Page - 1);
        if (pageIdx >= pageBytes.Count) return false;

        var bytes = pageBytes[pageIdx];
        if (bytes.Length == 0) return false;

        using var img = Image.Load(bytes);

        int x = Math.Clamp((int)region.Bbox.X, 0, Math.Max(0, img.Width - 1));
        int y = Math.Clamp((int)region.Bbox.Y, 0, Math.Max(0, img.Height - 1));
        int w = Math.Clamp((int)region.Bbox.W, 1, Math.Max(1, img.Width - x));
        int h = Math.Clamp((int)region.Bbox.H, 1, Math.Max(1, img.Height - y));

        img.Mutate(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

        using var ms = new MemoryStream();
        img.SaveAsPng(ms, new PngEncoder());
        var cropBytes = ms.ToArray();
        if (cropBytes.Length == 0) return false;
        if (cropBytes.Length > _options.MaxFigureBytes)
        {
            _log?.LogDebug(
                "[OCR_CASCADE] Layer2c figure exceeds MaxFigureBytes; dropping (size={Size})",
                cropBytes.Length);
            return false;
        }

        var hash = Convert.ToHexString(SHA256.HashData(cropBytes)).ToLowerInvariant()[..16];
        var filename = $"figure_{region.Bbox.Page:D3}_{hash}.png";
        var fullPath = Path.Combine(_options.OutputDirectory, filename);

        // Atomic write — tmp file + rename. Different tasks writing the
        // same hash never collide (content addressable).
        var tmp = fullPath + ".tmp";
        File.WriteAllBytes(tmp, cropBytes);
        try
        {
            File.Move(tmp, fullPath, overwrite: true);
        }
        catch (IOException)
        {
            // Concurrent writer raced us and won — safe to discard our copy.
            try { File.Delete(tmp); } catch { /* best-effort */ }
        }

        figure = new OcrFigureRef(
            Bbox: new BoundingBox(x, y, w, h, region.Bbox.Page),
            Kind: NormaliseKind(region.Kind),
            CroppedPath: fullPath,
            Caption: null);
        return true;
    }

    private static string NormaliseKind(string kind) =>
        kind switch
        {
            "figure" or "Figure" or "picture" or "image" => "figure",
            "table" or "Table" => "table",
            "plot" or "Plot" or "chart" => "plot",
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
