// =============================================================================
// Cena Platform — Cascade OCR Client (Adapter)
//
// RDY-OCR-WIREUP-C / Phase 2.3.
//
// Bridges the legacy IOcrClient + IMathOcrClient contracts consumed by
// IngestionOrchestrator, BagrutPdfIngestionService, and
// IngestionPipelineService.UploadFromRequestAsync to the real OCR cascade
// (IOcrCascadeService, ADR-0033). Replaces the old direct GeminiOcrClient +
// MathpixClient registrations in the Actor Host DI graph.
//
// Design:
//   - IngestionOrchestrator (etc.) stays unchanged: it still injects
//     IOcrClient/IMathOcrClient and calls ProcessPageAsync/ProcessDocumentAsync/
//     ExtractLatexAsync. All the event emission, segmentation, dedup, and
//     CAS-gated persist logic keeps working.
//   - This adapter delegates to IOcrCascadeService with:
//         surface = CascadeSurface.AdminBatch (throughput-tuned; B-surface thresholds)
//         hints   = (subject=math, source_type=admin_upload, language=null→auto)
//     and re-shapes OcrCascadeResult back to the legacy OcrPageOutput /
//     OcrDocumentOutput records.
//   - Figures/math crops handled inside the cascade — figures land at
//     configured storage path via Layer 2c; we expose them through
//     MathExpressions + RawText placeholders so the downstream segmenter
//     keeps producing structured questions without code change.
//
// NO STUBS, NO MOCKS, per project rule:
//   - real cascade call, real exception mapping, no canned output
//   - if the cascade throws (OcrCircuitOpenException / OcrInputException),
//     the adapter rethrows — the IngestionOrchestrator's existing catch/fail
//     stage logic already handles it and marks the pipeline item "failed"
//     with the real error string.
// =============================================================================

using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging;
using InfraTextBlock = Cena.Infrastructure.Ocr.Contracts.OcrTextBlock;
using InfraLanguage  = Cena.Infrastructure.Ocr.Contracts.Language;

namespace Cena.Actors.Ingest;

public sealed class CascadeOcrClient : IOcrClient, IMathOcrClient
{
    private readonly IOcrCascadeService _cascade;
    private readonly ILogger<CascadeOcrClient> _logger;

    public string ProviderName => "cena-ocr-cascade-v1";

    public CascadeOcrClient(
        IOcrCascadeService cascade,
        ILogger<CascadeOcrClient> logger)
    {
        _cascade = cascade;
        _logger = logger;
    }

    // --------------------------------------------------------------------
    // IOcrClient — single page
    // --------------------------------------------------------------------
    public async Task<OcrPageOutput> ProcessPageAsync(
        Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(imageStream, ct);
        var hints = new OcrContextHints(
            Subject: "math",
            Language: null,
            Track: null,
            SourceType: Cena.Infrastructure.Ocr.Contracts.SourceType.AdminUpload,
            TaxonomyNode: null,
            ExpectedFigures: null);

        var result = await _cascade.RecognizeAsync(
            bytes: bytes,
            contentType: contentType,
            hints: hints,
            surface: CascadeSurface.AdminBatch,
            ct: ct);

        _logger.LogInformation(
            "CascadeOcrClient (page): conf={Confidence:F3}, math={Math}, fallbacks={Fallbacks}, review={Review}",
            result.OverallConfidence, result.MathBlocks.Count,
            string.Join(",", result.FallbacksFired), result.HumanReviewRequired);

        return ToPageOutput(result, pageNumber: 1);
    }

    // --------------------------------------------------------------------
    // IOcrClient — multi-page document
    // --------------------------------------------------------------------
    public async Task<OcrDocumentOutput> ProcessDocumentAsync(
        Stream pdfStream, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(pdfStream, ct);
        var hints = new OcrContextHints(
            Subject: "math",
            Language: null,
            Track: null,
            SourceType: Cena.Infrastructure.Ocr.Contracts.SourceType.AdminUpload,
            TaxonomyNode: null,
            ExpectedFigures: null);

        var result = await _cascade.RecognizeAsync(
            bytes: bytes,
            contentType: "application/pdf",
            hints: hints,
            surface: CascadeSurface.AdminBatch,
            ct: ct);

        _logger.LogInformation(
            "CascadeOcrClient (document): conf={Confidence:F3}, text={Text}, math={Math}, triage={Triage}",
            result.OverallConfidence, result.TextBlocks.Count, result.MathBlocks.Count, result.PdfTriage);

        // The cascade surface is page-agnostic (ADR-0033): it returns a flat
        // bag of text + math blocks covering the whole document. Expose one
        // synthesized page so the legacy document consumers still work. The
        // per-page split for figure spec/boundary heuristics runs downstream
        // (QuestionSegmenter + BagrutPdfIngestionService).
        var page = ToPageOutput(result, pageNumber: 1);
        var language = page.DetectedLanguage;
        var pageCount = result.PdfTriage == PdfTriageVerdict.Encrypted ? 0 : 1;

        return new OcrDocumentOutput(
            Pages: new List<OcrPageOutput> { page },
            DetectedLanguage: language,
            OverallConfidence: (float)result.OverallConfidence,
            PageCount: pageCount,
            EstimatedCostUsd: EstimateCost(result));
    }

    // --------------------------------------------------------------------
    // IMathOcrClient — LaTeX-only extraction
    // --------------------------------------------------------------------
    public async Task<string> ExtractLatexAsync(Stream imageStream, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(imageStream, ct);
        var hints = new OcrContextHints(
            Subject: "math",
            Language: null,
            Track: null,
            SourceType: Cena.Infrastructure.Ocr.Contracts.SourceType.AdminUpload,
            TaxonomyNode: null,
            ExpectedFigures: false);

        var result = await _cascade.RecognizeAsync(
            bytes: bytes,
            contentType: "image/png",
            hints: hints,
            surface: CascadeSurface.AdminBatch,
            ct: ct);

        // Pick the highest-confidence CAS-validated math block. Fall back to
        // highest-confidence math block if none validated. Return empty
        // string if no math found — caller decides what to do with empty.
        var best = result.MathBlocks
            .Where(m => !string.IsNullOrWhiteSpace(m.Latex))
            .OrderByDescending(m => m.SympyParsed)
            .ThenByDescending(m => m.Confidence)
            .FirstOrDefault();

        if (best is null)
        {
            _logger.LogInformation("CascadeOcrClient (math): no math blocks extracted");
            return string.Empty;
        }

        _logger.LogInformation(
            "CascadeOcrClient (math): conf={Conf:F3}, sympy_parsed={Parsed}, len={Len}",
            best.Confidence, best.SympyParsed, best.Latex?.Length ?? 0);

        return best.Latex ?? string.Empty;
    }

    // --------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------
    private static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var buf))
        {
            return buf.Array is null
                ? Array.Empty<byte>()
                : buf.Array.AsSpan(buf.Offset, buf.Count).ToArray();
        }
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        return copy.ToArray();
    }

    private static OcrPageOutput ToPageOutput(OcrCascadeResult result, int pageNumber)
    {
        // Synthesize a raw text body by concatenating text blocks with
        // newlines, then appending math placeholders keyed to each math
        // block. Matches the shape Gemini used to emit so the downstream
        // QuestionSegmenter keeps working verbatim.
        var raw = string.Join(
            "\n",
            result.TextBlocks
                .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                .Select(t => t.Text));

        var mathExpressions = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < result.MathBlocks.Count; i++)
        {
            var block = result.MathBlocks[i];
            if (string.IsNullOrWhiteSpace(block.Latex)) continue;
            mathExpressions[$"eq_{i + 1}"] = block.Latex!;
        }

        // If text body is empty but math was found, surface placeholders so
        // the segmenter doesn't drop the page as "no content".
        if (string.IsNullOrWhiteSpace(raw) && mathExpressions.Count > 0)
        {
            raw = string.Join("\n", mathExpressions.Keys.Select(k => $"{{math:{k}}}"));
        }

        var textBlocks = result.TextBlocks
            .Select(MapTextBlock)
            .Concat(result.MathBlocks.Select(MapMathBlock))
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .ToList();

        var language = ResolveLanguage(result.TextBlocks);

        return new OcrPageOutput(
            PageNumber: pageNumber,
            RawText: raw,
            DetectedLanguage: language,
            MathExpressions: mathExpressions,
            Confidence: (float)result.OverallConfidence,
            TextBlocks: textBlocks);
    }

    private static Cena.Actors.Ingest.OcrTextBlock MapTextBlock(InfraTextBlock block) =>
        new(
            Text: block.Text ?? string.Empty,
            BoundingBox: MapBbox(block.Bbox),
            Confidence: (float)block.Confidence,
            IsMath: false);

    private static Cena.Actors.Ingest.OcrTextBlock MapMathBlock(OcrMathBlock block) =>
        new(
            Text: block.Latex ?? string.Empty,
            BoundingBox: MapBbox(block.Bbox),
            Confidence: (float)block.Confidence,
            IsMath: true);

    private static OcrBoundingBox? MapBbox(BoundingBox? bbox)
    {
        if (bbox is null) return null;
        return new OcrBoundingBox(
            X: (int)Math.Round(bbox.X),
            Y: (int)Math.Round(bbox.Y),
            Width: (int)Math.Round(bbox.W),
            Height: (int)Math.Round(bbox.H));
    }

    private static string ResolveLanguage(IReadOnlyList<InfraTextBlock> blocks)
    {
        if (blocks.Count == 0) return "unknown";
        var byLang = blocks
            .GroupBy(b => b.Language)
            .OrderByDescending(g => g.Count())
            .First();

        return byLang.Key switch
        {
            InfraLanguage.Hebrew  => "he",
            InfraLanguage.English => "en",
            InfraLanguage.Arabic  => "ar",
            _                     => "unknown",
        };
    }

    private static decimal EstimateCost(OcrCascadeResult result)
    {
        // Local layers are free; cloud fallbacks each cost ~$0.0003/page
        // (Mathpix) or ~$0.0005/page (Gemini Vision). We don't know actual
        // billing here — approximate from FallbacksFired, per ADR-0033.
        var fallbacks = result.FallbacksFired;
        if (fallbacks.Count == 0) return 0m;
        var cost = 0m;
        foreach (var f in fallbacks)
        {
            cost += f.Contains("gemini", StringComparison.OrdinalIgnoreCase) ? 0.0005m
                  : f.Contains("mathpix", StringComparison.OrdinalIgnoreCase) ? 0.0003m
                  : 0.0001m;
        }
        return cost;
    }
}
