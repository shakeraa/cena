// =============================================================================
// Cena Platform — OcrCascadeService orchestrator (ADR-0033)
//
// Implements IOcrCascadeService by sequencing Layers 0–5. Each layer is a
// mockable interface so this orchestrator is fully testable without
// OpenCV / gRPC / Tesseract / Surya installed.
//
// Flow mirrors scripts/ocr-spike/pipeline_prototype.py run_cascade():
//
//   Layer 0 preprocess (PDF triage shortcut → skip 1–3)
//     ├─ triage == text → pypdf-style shortcut, jump to Layer 5
//     └─ else → rasterize → Layer 1
//   Layer 1 layout detect
//   Layer 2a text + 2b math + 2c figures — parallel (Task.WhenAll)
//   Layer 3 RTL reassembly
//   Layer 4 τ gate + cloud fallback
//   Layer 5 SymPy CAS validation
//
// The orchestrator owns the [OCR_CASCADE] structured log. Individual layers
// log at their own level; the orchestrator aggregates timings and verdicts
// into the single OcrCascadeResult callers consume.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.Observability;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Ocr;

public sealed class OcrCascadeService : IOcrCascadeService
{
    private const string SchemaVersion = "1.0";
    private const string RunnerName = "cascade";

    private readonly IPdfTriage _pdfTriage;
    private readonly ILayer0Preprocess _layer0;
    private readonly ILayer1Layout _layer1;
    private readonly ILayer2aTextOcr _layer2a;
    private readonly ILayer2bMathOcr _layer2b;
    private readonly ILayer2cFigureExtraction _layer2c;
    private readonly ILayer3Reassemble _layer3;
    private readonly ILayer4ConfidenceGate _layer4;
    private readonly ILayer5CasValidation _layer5;
    private readonly ILogger<OcrCascadeService> _log;
    private readonly TimeProvider _time;
    private readonly OcrMetrics? _metrics;

    public OcrCascadeService(
        IPdfTriage pdfTriage,
        ILayer0Preprocess layer0,
        ILayer1Layout layer1,
        ILayer2aTextOcr layer2a,
        ILayer2bMathOcr layer2b,
        ILayer2cFigureExtraction layer2c,
        ILayer3Reassemble layer3,
        ILayer4ConfidenceGate layer4,
        ILayer5CasValidation layer5,
        ILogger<OcrCascadeService> log,
        TimeProvider? time = null,
        OcrMetrics? metrics = null)
    {
        _pdfTriage = pdfTriage;
        _layer0 = layer0;
        _layer1 = layer1;
        _layer2a = layer2a;
        _layer2b = layer2b;
        _layer2c = layer2c;
        _layer3 = layer3;
        _layer4 = layer4;
        _layer5 = layer5;
        _log = log;
        _time = time ?? TimeProvider.System;
        _metrics = metrics;
    }

    public async Task<OcrCascadeResult> RecognizeAsync(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        OcrContextHints? hints,
        CascadeSurface surface,
        CancellationToken ct)
    {
        // RDY-OCR-OBSERVABILITY: every call gets a unique request id that
        // appears on every log line (via BeginScope) and in any downstream
        // span. The scope dictionary mirrors the metrics tag set so Loki
        // logfmt queries can be joined back to Prometheus metrics.
        var cascadeRequestId = Guid.NewGuid().ToString("N");
        using var scope = _log.BeginScope(new Dictionary<string, object?>
        {
            ["cascade_request_id"] = cascadeRequestId,
            ["surface"]            = surface.ToString(),
            ["content_type"]       = contentType,
        });

        // Input-validation failures still record a metric (outcome=input_err)
        // so dashboards see the noise floor.
        if (bytes.IsEmpty)
        {
            _metrics?.RecordRequest(surface, contentType, "input_err");
            throw new OcrInputException("Input byte buffer is empty.");
        }
        if (string.IsNullOrWhiteSpace(contentType))
        {
            _metrics?.RecordRequest(surface, "", "input_err");
            throw new OcrInputException("Missing content type.");
        }

        var totalStopwatch = Stopwatch.StartNew();
        var timings = new Dictionary<string, double>(capacity: 7);

        // ── Layer 0 preprocess ───────────────────────────────────────────
        var layer0 = await _layer0.RunAsync(bytes, contentType, ct).ConfigureAwait(false);
        timings["layer_0_preprocess"] = layer0.LatencySeconds;
        _metrics?.RecordLayerLatency("layer_0_preprocess", surface, layer0.LatencySeconds * 1000.0);

        // PDF triage shortcut — if we already have a clean text layer, skip
        // the ML layers. Encrypted PDFs exit as a structured 422-like result.
        if (layer0.Triage is PdfTriageVerdict.Encrypted)
        {
            _log.LogInformation(
                "[OCR_CASCADE] triage=encrypted layer=Layer0 surface={Surface} verdict=human_review",
                surface);
            totalStopwatch.Stop();
            _metrics?.RecordRequest(surface, contentType, "encrypted_pdf");
            _metrics?.RecordTotalLatency(surface, "encrypted_pdf", totalStopwatch.Elapsed.TotalMilliseconds);
            _metrics?.RecordHumanReviewFlagged(surface, "encrypted_pdf");
            return BuildEncryptedResult(hints, timings, totalStopwatch.Elapsed.TotalSeconds);
        }

        // ── Layer 1 layout ───────────────────────────────────────────────
        var layer1 = await _layer1.RunAsync(layer0.PreprocessedPageBytes, hints, ct).ConfigureAwait(false);
        timings["layer_1_layout"] = layer1.LatencySeconds;
        _metrics?.RecordLayerLatency("layer_1_layout", surface, layer1.LatencySeconds * 1000.0);

        // ── Layer 2a / 2b / 2c — parallel ────────────────────────────────
        var textRegions = layer1.Regions.Where(r => r.Kind == "text").ToList();
        var mathRegions = layer1.Regions.Where(r => r.Kind == "math").ToList();
        var figureRegions = layer1.Regions.Where(r => r.Kind == "figure" || r.Kind == "table").ToList();

        var l2a = _layer2a.RunAsync(layer0.PreprocessedPageBytes, textRegions, hints, ct);
        var l2b = _layer2b.RunAsync(layer0.PreprocessedPageBytes, mathRegions, ct);
        var l2c = _layer2c.RunAsync(layer0.PreprocessedPageBytes, figureRegions, ct);
        await Task.WhenAll(l2a, l2b, l2c).ConfigureAwait(false);

        var layer2a = l2a.Result;
        var layer2b = l2b.Result;
        var layer2c = l2c.Result;
        timings["layer_2a_text"] = layer2a.LatencySeconds;
        timings["layer_2b_math"] = layer2b.LatencySeconds;
        timings["layer_2c_figures"] = layer2c.LatencySeconds;
        _metrics?.RecordLayerLatency("layer_2a_text",    surface, layer2a.LatencySeconds * 1000.0);
        _metrics?.RecordLayerLatency("layer_2b_math",    surface, layer2b.LatencySeconds * 1000.0);
        _metrics?.RecordLayerLatency("layer_2c_figures", surface, layer2c.LatencySeconds * 1000.0);

        // ── Layer 3 reassembly ───────────────────────────────────────────
        var layer3 = _layer3.Run(layer2a.TextBlocks, layer2b.MathBlocks, layer2c.Figures);
        timings["layer_3_reassemble"] = layer3.LatencySeconds;
        _metrics?.RecordLayerLatency("layer_3_reassemble", surface, layer3.LatencySeconds * 1000.0);

        // ── Layer 4 confidence gate ──────────────────────────────────────
        // Pass page bytes so Layer 4 can crop bboxes and feed them to the
        // cloud fallback runners (Mathpix / Gemini).
        var layer4 = await _layer4.RunAsync(
            layer0.PreprocessedPageBytes,
            layer3.OrderedTextBlocks,
            layer3.OrderedMathBlocks,
            surface, ct).ConfigureAwait(false);
        timings["layer_4_gate"] = layer4.LatencySeconds;
        _metrics?.RecordLayerLatency("layer_4_gate", surface, layer4.LatencySeconds * 1000.0);

        // Per-fallback counter increment. FallbacksFired is a flat list of
        // runner names; the "reason" is currently inferred as "low_conf"
        // since Layer 4 fires fallbacks when primary confidence < τ. Future
        // work can lift the reason into Layer4Result; for now we tag with
        // the primary trigger documented in ADR-0033.
        foreach (var fb in layer4.FallbacksFired)
            _metrics?.RecordFallbackFired(fb, reason: "low_conf");

        // ── Layer 5 CAS validation ───────────────────────────────────────
        var layer5 = await _layer5.RunAsync(layer4.MathBlocks, ct).ConfigureAwait(false);
        timings["layer_5_cas"] = layer5.LatencySeconds;
        _metrics?.RecordLayerLatency("layer_5_cas", surface, layer5.LatencySeconds * 1000.0);

        // CAS verdict counters.
        for (var i = 0; i < layer5.Validated; i++) _metrics?.RecordCasVerdict("verified");
        for (var i = 0; i < layer5.Failed; i++)    _metrics?.RecordCasVerdict("failed");
        var unverifiable = Math.Max(0, layer5.MathBlocks.Count - layer5.Validated - layer5.Failed);
        for (var i = 0; i < unverifiable; i++)     _metrics?.RecordCasVerdict("unverifiable");

        totalStopwatch.Stop();

        var reasons = new List<string>();
        if (layer4.CatastrophicFailure)
            reasons.Add("low_overall_confidence");
        if (layer5.Failed > layer5.Validated && layer5.Validated + layer5.Failed > 0)
            reasons.Add("majority_math_failed_cas");

        var humanReview = reasons.Count > 0
            // Surface A returns 422 up-stack; Surface B flags to human-review
            && (surface == CascadeSurface.StudentInteractive || surface == CascadeSurface.AdminBatch);

        // Classify outcome for request/latency metrics.
        string outcome;
        if (layer4.CatastrophicFailure)                                outcome = "low_conf";
        else if (layer5.Failed > 0 && layer5.Validated == 0)           outcome = "cas_fail";
        else                                                            outcome = "ok";
        _metrics?.RecordRequest(surface, contentType, outcome);
        _metrics?.RecordTotalLatency(surface, outcome, totalStopwatch.Elapsed.TotalMilliseconds);
        if (humanReview)
        {
            foreach (var r in reasons)
                _metrics?.RecordHumanReviewFlagged(surface, r);
        }

        _log.LogInformation(
            "[OCR_CASCADE] runner={Runner} surface={Surface} triage={Triage} " +
            "textBlocks={TextBlocks} mathBlocks={MathBlocks} casOk={CasOk} casFail={CasFail} " +
            "fallbacks={Fallbacks} avgConf={AvgConf:F3} humanReview={HumanReview} outcome={Outcome} totalMs={TotalMs}",
            RunnerName, surface, layer0.Triage,
            layer3.OrderedTextBlocks.Count, layer5.MathBlocks.Count,
            layer5.Validated, layer5.Failed, layer4.FallbacksFired.Count,
            layer4.AverageConfidence, humanReview, outcome,
            totalStopwatch.Elapsed.TotalMilliseconds);

        return new OcrCascadeResult(
            SchemaVersion: SchemaVersion,
            Runner: RunnerName,
            Source: contentType,
            Hints: hints,
            PdfTriage: layer0.Triage,
            TextBlocks: layer3.OrderedTextBlocks,
            MathBlocks: layer5.MathBlocks,
            Figures: layer3.Figures,
            OverallConfidence: Math.Round(layer4.AverageConfidence, 3),
            FallbacksFired: layer4.FallbacksFired,
            CasValidatedMath: layer5.Validated,
            CasFailedMath: layer5.Failed,
            HumanReviewRequired: humanReview,
            ReasonsForReview: reasons,
            LayerTimingsSeconds: timings,
            TotalLatencySeconds: Math.Round(totalStopwatch.Elapsed.TotalSeconds, 3),
            CapturedAt: _time.GetUtcNow().ToString("O"));
    }

    private OcrCascadeResult BuildEncryptedResult(
        OcrContextHints? hints,
        IReadOnlyDictionary<string, double> timings,
        double totalSeconds)
        => new(
            SchemaVersion: SchemaVersion,
            Runner: RunnerName,
            Source: "encrypted",
            Hints: hints,
            PdfTriage: PdfTriageVerdict.Encrypted,
            TextBlocks: Array.Empty<OcrTextBlock>(),
            MathBlocks: Array.Empty<OcrMathBlock>(),
            Figures: Array.Empty<OcrFigureRef>(),
            OverallConfidence: 0.0,
            FallbacksFired: Array.Empty<string>(),
            CasValidatedMath: 0,
            CasFailedMath: 0,
            HumanReviewRequired: true,
            ReasonsForReview: new[] { "preprocess_failed_or_encrypted" },
            LayerTimingsSeconds: timings,
            TotalLatencySeconds: Math.Round(totalSeconds, 3),
            CapturedAt: _time.GetUtcNow().ToString("O"));
}
