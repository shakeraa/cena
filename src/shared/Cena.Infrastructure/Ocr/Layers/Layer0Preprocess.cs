// =============================================================================
// Cena Platform — Layer 0 Preprocess (ADR-0033)
//
// Real, no-stub implementation of ILayer0Preprocess. Handles two input shapes:
//
//   1. Image (image/png | image/jpeg | image/jpg | image/webp):
//        decode via ImageSharp → downsample if too large → optional grayscale
//        → re-encode as PNG
//
//   2. PDF (application/pdf):
//        run IPdfTriage first. If the triage verdict is Encrypted we stop
//        here and return empty pageBytes + PdfTriageVerdict.Encrypted —
//        the orchestrator uses that to emit a 422-shaped CascadeResult.
//
//        Otherwise rasterize every page via `pdftoppm` (poppler, Process-
//        invoked). Each rasterized page goes through the same image
//        pipeline as the image path above.
//
// Why pdftoppm and not a NuGet PDF renderer:
//   - Already installed on dev (Homebrew) and production Docker (apt
//     poppler-utils).
//   - PDFium-based NuGets ship platform-specific native libraries and
//     break on arm64 macOS or linux/amd64 depending on which one you pick.
//   - Process overhead is ~100 ms per page — negligible next to the
//     rendering work itself.
// =============================================================================

using System.Diagnostics;
using System.Globalization;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class Layer0Preprocess : ILayer0Preprocess
{
    private readonly IPdfTriage _pdfTriage;
    private readonly Layer0PreprocessOptions _options;
    private readonly ILogger<Layer0Preprocess>? _log;

    public Layer0Preprocess(
        IPdfTriage pdfTriage,
        Layer0PreprocessOptions? options = null,
        ILogger<Layer0Preprocess>? log = null)
    {
        _pdfTriage = pdfTriage ?? throw new ArgumentNullException(nameof(pdfTriage));
        _options = options ?? new Layer0PreprocessOptions();
        _log = log;
    }

    public async Task<Layer0Output> RunAsync(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (bytes.IsEmpty)
            throw new OcrInputException("Input byte buffer is empty.");

        var mime = (contentType ?? string.Empty).ToLowerInvariant();
        if (IsPdfMime(mime))
        {
            return await HandlePdfAsync(bytes, sw, ct).ConfigureAwait(false);
        }
        if (IsImageMime(mime))
        {
            var processed = PreprocessSingleImage(bytes.Span);
            sw.Stop();
            return new Layer0Output(
                PreprocessedPageBytes: new[] { processed },
                Triage: null,
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }

        throw new OcrInputException(
            $"Unsupported content type for OCR cascade: '{contentType}'. " +
            "Expected image/png, image/jpeg, image/webp, or application/pdf.");
    }

    // -------------------------------------------------------------------------
    // PDF path
    // -------------------------------------------------------------------------
    private async Task<Layer0Output> HandlePdfAsync(
        ReadOnlyMemory<byte> bytes, Stopwatch sw, CancellationToken ct)
    {
        if (bytes.Length > _options.MaxPdfBytes)
            throw new OcrInputException(
                $"PDF exceeds MaxPdfBytes={_options.MaxPdfBytes:N0}.");

        // Triage first — encrypted PDFs short-circuit the whole cascade.
        var triage = _pdfTriage.Classify(bytes);
        if (triage.Verdict == PdfTriageVerdict.Encrypted)
        {
            sw.Stop();
            return new Layer0Output(
                PreprocessedPageBytes: Array.Empty<byte[]>(),
                Triage: PdfTriageVerdict.Encrypted,
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }

        // Rasterize via pdftoppm. Write PDF to a tmp file (pdftoppm reads
        // from path — stdin mode exists but is less reliable across poppler
        // versions) and emit PNGs into a tmp output dir. Clean up after.
        var tmpPdf = Path.Combine(Path.GetTempPath(),
            $"cena-ocr-{Guid.NewGuid():N}.pdf");
        var outDir = Path.Combine(Path.GetTempPath(),
            $"cena-ocr-pages-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(tmpPdf, bytes.ToArray(), ct).ConfigureAwait(false);
            Directory.CreateDirectory(outDir);

            await RunPdftoppmAsync(tmpPdf, outDir, ct).ConfigureAwait(false);

            // pdftoppm emits {prefix}-{page}.png into outDir. Read them in
            // page order.
            var pagePngs = Directory.GetFiles(outDir, "page-*.png");
            Array.Sort(pagePngs, StringComparer.OrdinalIgnoreCase);

            if (pagePngs.Length == 0)
            {
                _log?.LogWarning(
                    "[OCR_CASCADE] Layer0 pdftoppm produced no pages for PDF of {Bytes} bytes",
                    bytes.Length);
                sw.Stop();
                return new Layer0Output(
                    PreprocessedPageBytes: Array.Empty<byte[]>(),
                    Triage: triage.Verdict,
                    LatencySeconds: sw.Elapsed.TotalSeconds);
            }

            var preprocessed = new List<byte[]>(pagePngs.Length);
            foreach (var png in pagePngs)
            {
                ct.ThrowIfCancellationRequested();
                var raw = await File.ReadAllBytesAsync(png, ct).ConfigureAwait(false);
                preprocessed.Add(PreprocessSingleImage(raw));
            }

            sw.Stop();
            return new Layer0Output(
                PreprocessedPageBytes: preprocessed,
                Triage: triage.Verdict,
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }
        finally
        {
            try { if (File.Exists(tmpPdf)) File.Delete(tmpPdf); } catch { /* best-effort */ }
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private async Task RunPdftoppmAsync(string pdfPath, string outDir, CancellationToken ct)
    {
        var prefix = Path.Combine(outDir, "page");
        var args = string.Format(CultureInfo.InvariantCulture,
            "-r {0} -png \"{1}\" \"{2}\"",
            _options.DpiForRasterization, pdfPath, prefix);

        using var proc = new Process
        {
            StartInfo =
            {
                FileName = _options.PdftoppmBinaryPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new OcrInputException(
                $"Failed to start {_options.PdftoppmBinaryPath}. " +
                "Poppler must be installed (brew install poppler / apt-get install poppler-utils).",
                ex);
        }

        // Drain stderr so pdftoppm doesn't block on a full pipe.
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = new CancellationTokenSource(_options.PdfRenderTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"pdftoppm exceeded {_options.PdfRenderTimeout.TotalSeconds:F0}s");
        }

        if (proc.ExitCode != 0)
        {
            var err = await stderrTask.ConfigureAwait(false);
            throw new OcrInputException(
                $"pdftoppm exit code {proc.ExitCode}: {Truncate(err, 200)}");
        }
    }

    // -------------------------------------------------------------------------
    // Single-image preprocessing — shared by image path + PDF rasterization path
    // -------------------------------------------------------------------------
    internal byte[] PreprocessSingleImage(ReadOnlySpan<byte> raw)
    {
        using var img = Image.Load<Rgba32>(raw);

        int longEdge = Math.Max(img.Width, img.Height);
        double scale = longEdge > _options.MaxLongEdgePixels
            ? (double)_options.MaxLongEdgePixels / longEdge
            : 1.0;

        img.Mutate(ctx =>
        {
            if (scale < 1.0)
            {
                int w = Math.Max(1, (int)(img.Width * scale));
                int h = Math.Max(1, (int)(img.Height * scale));
                ctx.Resize(w, h);
            }
            if (_options.ConvertToGrayscale)
            {
                ctx.Grayscale();
            }
        });

        using var ms = new MemoryStream();
        img.SaveAsPng(ms, new PngEncoder());
        return ms.ToArray();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static bool IsPdfMime(string mime) =>
        mime.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImageMime(string mime) =>
        mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Length <= max ? s : s[..max];
    }
}
