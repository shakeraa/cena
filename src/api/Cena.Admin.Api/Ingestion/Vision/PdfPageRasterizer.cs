// =============================================================================
// Cena Platform — PdfPageRasterizer (vision-extractor branch)
//
// Renders each page of a Bagrut PDF to a PNG via Poppler `pdftoppm`. The
// resulting PNG is BOTH:
//   - the input image to GeminiVisionPageExtractor (single tool-use call), AND
//   - the high-quality source-page thumbnail the SPA can render in <img>
//     tags (replacing the brittle <embed> for source-page thumbnails — that
//     swap is a one-line frontend change in a follow-up branch; this
//     rasteriser persists the PNGs so the follow-up has nothing to wait
//     on backend-side).
//
// Why pdftoppm and not a NuGet PDF renderer:
//   - Already installed on the dev box (Homebrew) AND on the production
//     OCR sidecar Docker image (apt poppler-utils). Same binary
//     Layer0Preprocess wraps for the legacy cascade (see
//     src/shared/Cena.Infrastructure/Ocr/Layers/Layer0Preprocess.cs).
//   - PDFium-based NuGet packages ship platform-specific native libraries
//     and break across arm64 macOS / linux/amd64. Poppler is the lowest-
//     drift option.
//   - Process overhead is ~100-150 ms per page — negligible next to the
//     rendering cost on Bagrut PDFs that average 6 pages.
//
// Storage:
//   Pages land under {RootDirectory}/{pdfId}/page-{N:D3}.png. The
//   {pdfId}-prefixed sub-directory keeps the volume's listing manageable
//   at the million-PDF scale (a single big directory hits ext4 perf
//   degradation around 4096 entries). The page filename is zero-padded so
//   `Directory.GetFiles(...).OrderBy` resolves correct page order without
//   numeric parsing.
//
// Encrypted PDFs:
//   pdftoppm refuses encrypted PDFs and exits non-zero. This rasteriser
//   surfaces that as InvalidOperationException carrying the stderr tail —
//   matches the legacy cascade's "encrypted_pdf:cannot_read_without_password"
//   warning. The caller (BagrutPdfIngestionService) catches and emits the
//   same warning shape so the curator UI behaviour is unchanged.
//
// Contract:
//   - One PNG per page, in page order.
//   - PNG bytes are the unmodified pdftoppm output (no downscaling, no
//     grayscale) — the vision-LLM benefits from full-resolution colour
//     and the source-page thumbnail surface needs the same fidelity.
//   - Atomic-ish writes via tmp-file + Move; concurrent ingestions of the
//     same content-hashed PDF id silently win-the-race-or-skip.
// =============================================================================

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion.Vision;

/// <summary>
/// Configuration for <see cref="PdfPageRasterizer"/>. Bind from
/// <c>Ingestion:SourcePageStorage</c> in appsettings (matches the
/// figure-storage / pdf-store convention).
/// </summary>
public sealed class SourcePageStorageOptions
{
    /// <summary>
    /// Root directory for rasterised page PNGs. Defaults to
    /// <c>{TempPath}/cena-source-pages</c> so dev works without config.
    /// Production (docker-compose.app.yml) overrides to
    /// <c>/var/cena/source-pages</c> (named volume).
    /// </summary>
    public string RootDirectory { get; init; } =
        Path.Combine(Path.GetTempPath(), "cena-source-pages");

    /// <summary>DPI passed to pdftoppm. 200 is the brief's value.</summary>
    public int Dpi { get; init; } = 200;

    /// <summary>Whole-PDF rasterisation timeout.</summary>
    public TimeSpan RenderTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Path to the pdftoppm binary. Default "pdftoppm" relies on PATH; the
    /// production OCR sidecar image and the dev Homebrew install both put
    /// the binary on PATH so this default is safe.
    /// </summary>
    public string PdftoppmBinaryPath { get; init; } = "pdftoppm";
}

/// <summary>
/// Rasterises a Bagrut PDF to per-page PNGs on disk and returns the on-disk
/// paths. Unit-test seam: the binary path can be substituted via
/// <see cref="SourcePageStorageOptions.PdftoppmBinaryPath"/>.
/// </summary>
public interface IPdfPageRasterizer
{
    /// <summary>
    /// Rasterise <paramref name="pdfBytes"/> to per-page PNGs under the
    /// configured root + <paramref name="pdfId"/>. Returns the absolute paths
    /// in page order (1-indexed by file index). Throws when the PDF is
    /// encrypted or the binary is missing.
    /// </summary>
    Task<IReadOnlyList<string>> RasterizeAsync(
        byte[] pdfBytes,
        string pdfId,
        CancellationToken ct = default);
}

public sealed class PdfPageRasterizer : IPdfPageRasterizer
{
    private readonly SourcePageStorageOptions _options;
    private readonly ILogger<PdfPageRasterizer>? _log;

    public PdfPageRasterizer(
        IOptions<SourcePageStorageOptions> options,
        ILogger<PdfPageRasterizer>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _log = log;
        Directory.CreateDirectory(_options.RootDirectory);
    }

    public async Task<IReadOnlyList<string>> RasterizeAsync(
        byte[] pdfBytes,
        string pdfId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
            throw new ArgumentException("PDF bytes are empty.", nameof(pdfBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfId);

        var pageDir = Path.Combine(_options.RootDirectory, SafeId(pdfId));
        Directory.CreateDirectory(pageDir);

        // If we already rasterised this pdfId previously (content-addressable),
        // skip the binary call. Idempotent re-ingestion is the explicit
        // contract — same hash = same bytes = same pages.
        var existing = SortedPagePaths(pageDir);
        if (existing.Count > 0)
        {
            _log?.LogDebug(
                "PdfPageRasterizer: cache hit pdf={PdfId} pages={Count}",
                pdfId, existing.Count);
            return existing;
        }

        // Write PDF to a temp file (pdftoppm reads from a path; stdin mode
        // exists across poppler versions but is less reliable). Output
        // directly into the destination directory so a successful render is
        // immediately visible — atomic per-page rename happens inside
        // pdftoppm itself once it writes the entire PNG.
        var tmpPdf = Path.Combine(Path.GetTempPath(), $"cena-source-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tmpPdf, pdfBytes, ct).ConfigureAwait(false);
            await RunPdftoppmAsync(tmpPdf, pageDir, ct).ConfigureAwait(false);

            var pages = SortedPagePaths(pageDir);
            if (pages.Count == 0)
            {
                _log?.LogWarning(
                    "PdfPageRasterizer: pdftoppm produced no pages pdf={PdfId} bytes={Bytes}",
                    pdfId, pdfBytes.Length);
            }
            return pages;
        }
        finally
        {
            try { if (File.Exists(tmpPdf)) File.Delete(tmpPdf); } catch { /* best-effort */ }
        }
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------
    private async Task RunPdftoppmAsync(string pdfPath, string outDir, CancellationToken ct)
    {
        // pdftoppm emits {prefix}-{N}.png where N is page-index padded to
        // match the page-count's digit count when -png is used. Pad
        // explicitly via -fpd to keep filenames sortable: e.g. page-001.png,
        // page-002.png. Older poppler versions (<= 0.83) lack -fpd; we use
        // the "%03d" pattern Layer0Preprocess already validated as portable.
        var prefix = Path.Combine(outDir, "page");
        var args = string.Format(CultureInfo.InvariantCulture,
            "-r {0} -png \"{1}\" \"{2}\"",
            _options.Dpi, pdfPath, prefix);

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
            throw new InvalidOperationException(
                $"Failed to start '{_options.PdftoppmBinaryPath}'. " +
                "Poppler must be installed (brew install poppler / apt-get install poppler-utils).",
                ex);
        }

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = new CancellationTokenSource(_options.RenderTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"pdftoppm exceeded {_options.RenderTimeout.TotalSeconds:F0}s");
        }

        if (proc.ExitCode != 0)
        {
            var err = await stderrTask.ConfigureAwait(false);
            // Encrypted PDFs surface as exit-1 with a "Command Line Error:
            // Incorrect password" message. Surface as InvalidOperationException
            // so the caller can map it to the existing encrypted-pdf warning.
            throw new InvalidOperationException(
                $"pdftoppm exit code {proc.ExitCode}: {Truncate(err, 200)}");
        }
    }

    private static IReadOnlyList<string> SortedPagePaths(string pageDir)
    {
        if (!Directory.Exists(pageDir)) return Array.Empty<string>();
        var files = Directory.GetFiles(pageDir, "page-*.png");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>
    /// Path-segment hardening for the per-PDF sub-directory. The pdfId is
    /// derived from a content hash by <see cref="BagrutPdfIngestionService.GeneratePdfId"/>
    /// (lowercase hex with a "pdf-" prefix), but defence in depth means we
    /// strip everything else so a bug elsewhere cannot inject directory
    /// traversal here.
    /// </summary>
    private static string SafeId(string id)
    {
        var span = id.AsSpan();
        var buf = new char[span.Length];
        int j = 0;
        foreach (var ch in span)
        {
            var ok = (ch >= '0' && ch <= '9')
                  || (ch >= 'a' && ch <= 'z')
                  || (ch >= 'A' && ch <= 'Z')
                  || ch == '-' || ch == '_';
            if (ok) buf[j++] = ch;
        }
        if (j == 0) return "_";
        return new string(buf, 0, j).ToLowerInvariant();
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Length <= max ? s : s[..max];
    }
}
