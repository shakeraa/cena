// =============================================================================
// Cena Platform — Curator Metadata Auto-Extractor (RDY-019e-IMPL / Phase 1C)
//
// Populates the Admin ingestion CuratorMetadata before the curator opens the
// review panel. First-match-wins across three strategies:
//
//   1) filename        — regex map on "bagrut_math_2024_5u_hebrew.pdf" etc.
//   2) pdf_metadata    — PdfPig Author/Title/Subject/Keywords tokens
//   3) one_page_preview — rasterize page 1 only, hand to IOcrCascadeService
//                         with NO hints, classify language by Unicode block
//                         majority. Gated behind a feature flag so early
//                         rollouts (before full cascade stability is proven)
//                         can skip the preview.
//
// Each strategy returns a partial CuratorMetadata + per-field confidences.
// The service layer merges highest-confidence field-across-strategies.
//
// NO STUBS / NO MOCKS in production. Every strategy either runs its real
// implementation or returns null — the caller (CuratorMetadataService) fuses
// whatever it gets. If all strategies return nothing the state stays
// "pending" and the UI prompts the curator to fill manually.
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Api.Contracts.Admin.Ingestion;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Cena.Admin.Api.Ingestion;

public interface ICuratorMetadataExtractor
{
    /// <summary>
    /// Runs all enabled extraction strategies over the uploaded file bytes.
    /// Returns null if every strategy declined. The service layer persists
    /// the result as <c>AutoExtractedMetadata</c> and transitions the item
    /// to <c>auto_extracted</c>.
    /// </summary>
    Task<AutoExtractedMetadata?> ExtractAsync(
        string filename,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default);
}

public sealed class CuratorMetadataExtractor : ICuratorMetadataExtractor
{
    private readonly IOcrCascadeService? _cascade;
    private readonly bool _onePagePreviewEnabled;
    private readonly ILogger<CuratorMetadataExtractor> _logger;

    // --- Filename regexes -------------------------------------------------
    // Filenames use underscores/hyphens/dots as separators, and in .NET regex
    // `\b` does NOT fire across `_` (underscore is a word char). We normalize
    // the stem to a space-delimited string before matching so `\b` works
    // against spaces, which is what the patterns assume.
    private static readonly Regex NonAlphanumRx = new(@"[^A-Za-z0-9]+", RegexOptions.Compiled);

    private static readonly Regex SubjectRx = new(
        @"(?ix) \b(?<value> math (?:ematics)? | physics | chem(?:istry)? | biology | history | literature ) \b",
        RegexOptions.Compiled);

    private static readonly Regex LanguageRx = new(
        @"(?ix) \b(?<value> hebrew | arabic | english | he | ar | en ) \b",
        RegexOptions.Compiled);

    // Track patterns need to match "5u", "5_u", "5units", "5 units" after
    // normalization collapses separators to a single space → "5 u" / "5 units".
    private static readonly Regex TrackRx = new(
        @"(?ix) \b(?<value> (?: 3 | 4 | 5 ) \s* u (?:nits)? ) \b",
        RegexOptions.Compiled);

    private static readonly Regex SourceTypeRx = new(
        @"(?ix) \b(?<value> bagrut | psychometric | sat | cloud | batch ) \b",
        RegexOptions.Compiled);

    public CuratorMetadataExtractor(
        ILogger<CuratorMetadataExtractor> logger,
        IConfiguration? configuration = null,
        IOcrCascadeService? cascade = null)
    {
        _logger = logger;
        _cascade = cascade;
        _onePagePreviewEnabled = configuration?.GetValue<bool>(
            "Ingestion:CuratorMetadata:OnePagePreviewEnabled") ?? false;
    }

    public async Task<AutoExtractedMetadata?> ExtractAsync(
        string filename,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filename) && (fileBytes is null || fileBytes.Length == 0))
            return null;

        var accum = new Dictionary<string, (string Value, double Confidence, string Strategy)>();

        // Strategy 1 — filename (cheap, always runs)
        var fromFilename = ExtractFromFilename(filename);
        MergeBestFields(accum, fromFilename, strategy: "filename");

        // Strategy 2 — embedded PDF metadata
        if (IsPdf(contentType) && fileBytes is { Length: > 0 })
        {
            try
            {
                var fromPdf = ExtractFromPdfMetadata(fileBytes);
                MergeBestFields(accum, fromPdf, strategy: "pdf_metadata");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PdfPig metadata extraction failed for {Filename}", filename);
            }
        }

        // Strategy 3 — one-page OCR preview (opt-in via config)
        if (_onePagePreviewEnabled && _cascade is not null && fileBytes is { Length: > 0 })
        {
            try
            {
                var fromPreview = await ExtractFromOnePagePreviewAsync(fileBytes, contentType, ct);
                MergeBestFields(accum, fromPreview, strategy: "one_page_preview");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "One-page OCR preview extraction failed for {Filename}", filename);
            }
        }

        if (accum.Count == 0)
        {
            _logger.LogInformation("CuratorMetadata: no fields extracted for {Filename}", filename);
            return null;
        }

        var metadata = new CuratorMetadata(
            Subject:         accum.TryGetValue(nameof(CuratorMetadata.Subject), out var s) ? s.Value : null,
            Language:        accum.TryGetValue(nameof(CuratorMetadata.Language), out var l) ? l.Value : null,
            Track:           accum.TryGetValue(nameof(CuratorMetadata.Track), out var t) ? t.Value : null,
            SourceType:      accum.TryGetValue(nameof(CuratorMetadata.SourceType), out var st) ? st.Value : null,
            TaxonomyNode:    accum.TryGetValue(nameof(CuratorMetadata.TaxonomyNode), out var tn) ? tn.Value : null,
            ExpectedFigures: accum.TryGetValue(nameof(CuratorMetadata.ExpectedFigures), out var ef)
                ? ef.Value == "true" : null);

        var confidences = accum.ToDictionary(kv => kv.Key, kv => kv.Value.Confidence);
        var strategy = DescribeCombinedStrategy(accum);

        _logger.LogInformation(
            "CuratorMetadata extracted: filename={Filename} strategy={Strategy} fields={Fields}",
            filename, strategy, string.Join(",", accum.Keys));

        return new AutoExtractedMetadata(metadata, confidences, strategy);
    }

    // --------------------------------------------------------------------
    // Strategy 1: filename
    // --------------------------------------------------------------------
    internal static Dictionary<string, (string Value, double Confidence)> ExtractFromFilename(string? filename)
    {
        var map = new Dictionary<string, (string Value, double Confidence)>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(filename)) return map;

        var stem = System.IO.Path.GetFileNameWithoutExtension(filename);
        // Normalize "bagrut_mathematics_2024_hebrew_5u" → "bagrut mathematics 2024 hebrew 5u"
        var normalized = NonAlphanumRx.Replace(stem, " ").Trim();
        if (normalized.Length == 0) return map;

        var sub = SubjectRx.Match(normalized);
        if (sub.Success)
            map[nameof(CuratorMetadata.Subject)] = (NormalizeSubject(sub.Groups["value"].Value), 0.85);

        var lang = LanguageRx.Match(normalized);
        if (lang.Success)
            map[nameof(CuratorMetadata.Language)] = (NormalizeLanguage(lang.Groups["value"].Value), 0.88);

        var track = TrackRx.Match(normalized);
        if (track.Success)
            map[nameof(CuratorMetadata.Track)] = (NormalizeTrack(track.Groups["value"].Value), 0.9);

        var src = SourceTypeRx.Match(normalized);
        if (src.Success)
            map[nameof(CuratorMetadata.SourceType)] = (NormalizeSourceType(src.Groups["value"].Value), 0.8);

        return map;
    }

    // --------------------------------------------------------------------
    // Strategy 2: PDF embedded metadata (Author/Title/Subject/Keywords)
    // --------------------------------------------------------------------
    internal static Dictionary<string, (string Value, double Confidence)> ExtractFromPdfMetadata(byte[] pdfBytes)
    {
        var map = new Dictionary<string, (string Value, double Confidence)>(StringComparer.Ordinal);
        using var doc = PdfDocument.Open(pdfBytes);
        var info = doc.Information;
        var blob = string.Join(" ",
            info.Title ?? "",
            info.Subject ?? "",
            info.Keywords ?? "",
            info.Author ?? "").ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(blob)) return map;

        // Normalize non-alphanumeric → space so \b boundaries fire correctly.
        blob = NonAlphanumRx.Replace(blob, " ").Trim();

        var sub = SubjectRx.Match(blob);
        if (sub.Success)
            map[nameof(CuratorMetadata.Subject)] = (NormalizeSubject(sub.Groups["value"].Value), 0.75);

        var lang = LanguageRx.Match(blob);
        if (lang.Success)
            map[nameof(CuratorMetadata.Language)] = (NormalizeLanguage(lang.Groups["value"].Value), 0.78);

        var track = TrackRx.Match(blob);
        if (track.Success)
            map[nameof(CuratorMetadata.Track)] = (NormalizeTrack(track.Groups["value"].Value), 0.82);

        var src = SourceTypeRx.Match(blob);
        if (src.Success)
            map[nameof(CuratorMetadata.SourceType)] = (NormalizeSourceType(src.Groups["value"].Value), 0.7);

        return map;
    }

    // --------------------------------------------------------------------
    // Strategy 3: one-page OCR preview (cascade, no hints)
    // --------------------------------------------------------------------
    private async Task<Dictionary<string, (string Value, double Confidence)>> ExtractFromOnePagePreviewAsync(
        byte[] fileBytes, string contentType, CancellationToken ct)
    {
        var map = new Dictionary<string, (string Value, double Confidence)>(StringComparer.Ordinal);
        if (_cascade is null) return map;

        var result = await _cascade.RecognizeAsync(
            bytes: fileBytes,
            contentType: contentType,
            hints: null,                          // no hints — cascade infers
            surface: CascadeSurface.AdminBatch,
            ct: ct);

        if (result.TextBlocks.Count == 0 && result.MathBlocks.Count == 0) return map;

        var language = DetectLanguageFromBlocks(result.TextBlocks);
        if (language is not null)
            map[nameof(CuratorMetadata.Language)] = (language, 0.7);

        if (result.MathBlocks.Count > 0)
            map[nameof(CuratorMetadata.Subject)] = ("math", 0.65);

        if (result.Figures.Count > 0)
            map[nameof(CuratorMetadata.ExpectedFigures)] = ("true", 0.72);

        return map;
    }

    // --------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------
    private static bool IsPdf(string contentType) =>
        contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);

    private static void MergeBestFields(
        Dictionary<string, (string Value, double Confidence, string Strategy)> accum,
        Dictionary<string, (string Value, double Confidence)> incoming,
        string strategy)
    {
        foreach (var (field, entry) in incoming)
        {
            if (!accum.TryGetValue(field, out var existing) || entry.Confidence > existing.Confidence)
            {
                accum[field] = (entry.Value, entry.Confidence, strategy);
            }
        }
    }

    private static string DescribeCombinedStrategy(
        Dictionary<string, (string Value, double Confidence, string Strategy)> accum)
    {
        var used = accum.Values.Select(v => v.Strategy).Distinct().ToList();
        return used.Count switch
        {
            0 => "none",
            1 => used[0],
            _ => "combined",
        };
    }

    private static string? DetectLanguageFromBlocks(IReadOnlyList<OcrTextBlock> blocks)
    {
        if (blocks.Count == 0) return null;
        var byLang = blocks
            .Where(b => b.Language != Language.Unknown)
            .GroupBy(b => b.Language)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (byLang is null) return null;
        return byLang.Key switch
        {
            Language.Hebrew  => "he",
            Language.English => "en",
            Language.Arabic  => "ar",
            _                => null,
        };
    }

    internal static string NormalizeSubject(string raw) => raw.ToLowerInvariant() switch
    {
        "math" or "mathematics" => "math",
        "chem" or "chemistry"   => "chemistry",
        _                       => raw.ToLowerInvariant(),
    };

    internal static string NormalizeLanguage(string raw) => raw.ToLowerInvariant() switch
    {
        "hebrew"  or "he" => "he",
        "arabic"  or "ar" => "ar",
        "english" or "en" => "en",
        _ => raw.ToLowerInvariant(),
    };

    internal static string NormalizeTrack(string raw)
    {
        var compact = Regex.Replace(raw, @"[\s_\-]+", "").ToLowerInvariant();
        if (compact.StartsWith("3")) return "3u";
        if (compact.StartsWith("4")) return "4u";
        if (compact.StartsWith("5")) return "5u";
        return compact;
    }

    internal static string NormalizeSourceType(string raw) => raw.ToLowerInvariant() switch
    {
        "bagrut"       => "bagrut_reference",
        "psychometric" => "admin_upload",
        "sat"          => "admin_upload",
        "cloud"        => "cloud_dir",
        "batch"        => "admin_upload",
        _              => raw.ToLowerInvariant(),
    };
}
