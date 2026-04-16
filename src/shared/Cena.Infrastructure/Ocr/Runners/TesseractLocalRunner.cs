// =============================================================================
// Cena Platform — TesseractLocalRunner (ADR-0033 Layer 2a concrete)
//
// Real implementation of ILayer2aTextOcr that invokes the local `tesseract`
// binary via Process. NO mocks. NO stubs. Produces real Hebrew + English
// text from image bytes.
//
// Why Process-based and not P/Invoke:
//   - Platform-portable: tesseract is in PATH on dev (macOS arm64 via
//     Homebrew) AND in the Linux Docker image (apt-get tesseract-ocr).
//   - No native NuGet bindings to package per-OS.
//   - Overhead is ~150 ms per call; well within Surface A's 3 s budget.
//
// Uses tesseract's stdin image input + stdout TSV output to avoid touching
// the filesystem. TSV gives us per-word bboxes and confidences — the
// cascade needs per-region confidence for Layer 4's τ gate.
//
// Preconditions (validated at runner construction):
//   1. `tesseract` on PATH
//   2. `heb.traineddata` installed (or the user hints `language=en`, in
//      which case `eng.traineddata` is required)
// Failure to meet either throws InvalidOperationException at start-up —
// fail fast, no silent degradation.
// =============================================================================

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class TesseractLocalRunner : ILayer2aTextOcr
{
    private readonly TesseractOptions _options;
    private readonly ILogger<TesseractLocalRunner>? _log;

    public TesseractLocalRunner(
        TesseractOptions? options = null,
        ILogger<TesseractLocalRunner>? log = null)
    {
        _options = options ?? new TesseractOptions();
        _log = log;
        VerifyAvailability();
    }

    public async Task<Layer2aOutput> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<LayoutRegion> textRegions,
        OcrContextHints? hints,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var blocks = new List<OcrTextBlock>();
        var languageCode = LanguageCodeFor(hints);

        // If the cascade's Layer 1 produced no text regions (e.g. degraded
        // mode), we still run tesseract on each full page. Layer 3's row
        // bucketing handles the coarse layout downstream.
        for (int pageIdx = 0; pageIdx < pageBytes.Count; pageIdx++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var pageBlocks = await RunSinglePageAsync(
                    pageBytes[pageIdx],
                    pageNumber: pageIdx + 1,
                    languageCode: languageCode,
                    ct: ct).ConfigureAwait(false);
                blocks.AddRange(pageBlocks);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.LogError(ex,
                    "[OCR_CASCADE] Layer2a Tesseract failed on page {Page}; skipping that page",
                    pageIdx + 1);
            }
        }

        sw.Stop();
        return new Layer2aOutput(
            TextBlocks: blocks,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // Availability check — runs once at construction
    // -------------------------------------------------------------------------
    private void VerifyAvailability()
    {
        // Check `tesseract --version` to prove the binary is present.
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = _options.TesseractBinaryPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException(
                $"Could not start {_options.TesseractBinaryPath}");
            if (!proc.WaitForExit(5000))
                throw new InvalidOperationException("`tesseract --version` did not exit within 5s");
            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"`tesseract --version` exited with code {proc.ExitCode}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "TesseractLocalRunner requires `tesseract` on PATH. " +
                "Install via `brew install tesseract tesseract-lang` (macOS) or " +
                "`apt-get install tesseract-ocr tesseract-ocr-heb tesseract-ocr-eng` (Debian/Ubuntu).",
                ex);
        }

        // Check `tesseract --list-langs` contains all requested language packs.
        var listed = RunAndCaptureOutput(_options.TesseractBinaryPath, "--list-langs");
        foreach (var required in _options.RequiredLanguagePacks)
        {
            if (!listed.Contains(required, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"TesseractLocalRunner requires the `{required}` language pack. " +
                    $"Install via `brew install tesseract-lang` or download " +
                    $"`{required}.traineddata` into the tessdata directory.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Per-page execution
    // -------------------------------------------------------------------------
    private async Task<List<OcrTextBlock>> RunSinglePageAsync(
        byte[] imageBytes, int pageNumber, string languageCode, CancellationToken ct)
    {
        // tesseract stdin stdout -l heb+eng --psm 3 tsv
        //   stdin       → read image bytes from stdin
        //   stdout      → emit result to stdout
        //   --psm 3     → automatic page segmentation (default; best for doc scans)
        //   tsv         → produce TSV with per-word bbox + confidence
        var args = $"stdin stdout -l {languageCode} --psm {_options.PageSegMode} tsv";

        using var proc = new Process
        {
            StartInfo =
            {
                FileName = _options.TesseractBinaryPath,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };

        proc.Start();

        // Pipe image bytes to stdin. MUST close when done — tesseract waits
        // for EOF before processing.
        await proc.StandardInput.BaseStream.WriteAsync(imageBytes, ct).ConfigureAwait(false);
        proc.StandardInput.BaseStream.Flush();
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        // Honour the process timeout. WaitForExitAsync with a cancellation
        // token is the .NET 8+ way; we layer our own timeout on top.
        using var timeoutCts = new CancellationTokenSource(_options.PerPageTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"tesseract exceeded {_options.PerPageTimeout.TotalSeconds:F0}s on page {pageNumber}");
        }

        if (proc.ExitCode != 0)
        {
            var err = await stderrTask.ConfigureAwait(false);
            throw new InvalidOperationException(
                $"tesseract exit code {proc.ExitCode} on page {pageNumber}: {Truncate(err, 200)}");
        }

        var tsv = await stdoutTask.ConfigureAwait(false);
        return ParseTsv(tsv, pageNumber, languageCode);
    }

    // -------------------------------------------------------------------------
    // TSV parser — per-word rows into OcrTextBlock (one per non-empty word).
    //
    // TSV columns (tesseract docs):
    //   level, page_num, block_num, par_num, line_num, word_num,
    //   left, top, width, height, conf, text
    // -------------------------------------------------------------------------
    internal static List<OcrTextBlock> ParseTsv(string tsv, int pageNumber, string languageCode)
    {
        var blocks = new List<OcrTextBlock>();
        if (string.IsNullOrWhiteSpace(tsv)) return blocks;

        using var reader = new StringReader(tsv);
        string? header = reader.ReadLine();          // skip header row
        if (header is null) return blocks;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split('\t');
            if (parts.Length < 12) continue;

            // level 5 == word in tesseract TSV
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level != 5)
                continue;

            var text = parts[11];
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                !int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ||
                !int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
                !int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                continue;

            if (!double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var rawConf))
                continue;

            // tesseract emits confidence ∈ [-1, 100]; -1 = no confidence.
            // Normalise to [0, 1] and clamp negatives.
            double confidence = rawConf < 0 ? 0.0 : Math.Min(1.0, rawConf / 100.0);
            if (confidence <= 0.0) continue;

            bool isRtl = ContainsHebrew(text);
            var lang = isRtl
                ? Language.Hebrew
                : (languageCode.StartsWith("ara", StringComparison.OrdinalIgnoreCase)
                    ? Language.Arabic
                    : Language.English);

            blocks.Add(new OcrTextBlock(
                Text: text,
                Bbox: new BoundingBox(x, y, w, h, pageNumber),
                Language: lang,
                Confidence: confidence,
                IsRtl: isRtl));
        }

        return blocks;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private string LanguageCodeFor(OcrContextHints? hints)
    {
        if (hints?.Language == Language.English) return "eng";
        if (hints?.Language == Language.Arabic) return "ara";
        // Default: Hebrew + English (Cena's dominant content mix)
        return _options.DefaultLanguageCode;
    }

    private static bool ContainsHebrew(string text)
    {
        foreach (var c in text)
            if (c >= 0x0590 && c <= 0x05FF) return true;
        return false;
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty
            : s!.Length <= max ? s
            : s[..max];

    private static string RunAndCaptureOutput(string fileName, string arguments)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Could not start {fileName}");

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(5000);
        return stdout + "\n" + stderr;
    }
}

/// <summary>
/// Configuration for TesseractLocalRunner. Bind from "Ocr:Tesseract" section.
/// </summary>
public sealed class TesseractOptions
{
    public string TesseractBinaryPath { get; init; } = "tesseract";
    public string DefaultLanguageCode { get; init; } = "heb+eng";
    public IReadOnlyList<string> RequiredLanguagePacks { get; init; } = new[] { "heb", "eng" };
    public int PageSegMode { get; init; } = 3;       // tesseract --psm 3: auto page segmentation
    public TimeSpan PerPageTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
