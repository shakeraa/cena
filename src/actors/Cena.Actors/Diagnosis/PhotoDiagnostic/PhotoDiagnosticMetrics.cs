// =============================================================================
// Cena Platform — PhotoDiagnosticMetrics (EPIC-PRR-J PRR-420/421/422)
//
// OTel meter + instruments for the photo-diagnostic pipeline. Exposed as a
// DI-registered singleton (matches StuckClassifierMetrics pattern) so every
// stage in the pipeline records into the same meter and we export a
// coherent dashboard.
//
// Instruments:
//   - ocr_confidence  histogram — per-step OCR confidence 0..1
//   - template_score  histogram — TemplateMatchingScorer score 0..1
//   - end_to_end_ms   histogram — upload→narration latency (SLO p95 < 10s)
//   - low_confidence_refusals counter — chain-verify refusals (PRR-420)
//   - template_fallback counter — no-template "check with teacher" fallback
//   - audit_sampled    counter — diagnostic flagged for SME review
//
// Production-grade: real OTel instruments registered with IMeterFactory,
// no stubs (memory 'No stubs — production grade', 2026-04-11).
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Single source for the photo-diagnostic pipeline's OTel meter +
/// instruments. Singleton; consumers inject and call the Record* methods.
/// </summary>
public sealed class PhotoDiagnosticMetrics : IDisposable
{
    public const string MeterName = "Cena.Actors.Diagnosis.PhotoDiagnostic";

    private readonly Meter _meter;
    private readonly Histogram<double> _ocrConfidence;
    private readonly Histogram<double> _templateScore;
    private readonly Histogram<double> _endToEndMs;
    private readonly Counter<long> _lowConfidenceRefusals;
    private readonly Counter<long> _templateFallback;
    private readonly Counter<long> _auditSampled;

    public PhotoDiagnosticMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName, "1.0.0");

        _ocrConfidence = _meter.CreateHistogram<double>(
            "cena.photo_diagnostic.ocr_confidence",
            unit: "1",
            description: "Per-step OCR confidence, 0..1.");
        _templateScore = _meter.CreateHistogram<double>(
            "cena.photo_diagnostic.template_score",
            unit: "1",
            description: "TemplateMatchingScorer best-match score, 0..1.");
        _endToEndMs = _meter.CreateHistogram<double>(
            "cena.photo_diagnostic.end_to_end_ms",
            unit: "ms",
            description: "Upload → narration latency. Launch SLO: p95 < 10000ms.");
        _lowConfidenceRefusals = _meter.CreateCounter<long>(
            "cena.photo_diagnostic.low_confidence_refusals_total",
            description: "StepChainVerifier refusals due to OCR confidence below threshold.");
        _templateFallback = _meter.CreateCounter<long>(
            "cena.photo_diagnostic.template_fallback_total",
            description: "Break-type had no template match; 'check with teacher' fallback shown.");
        _auditSampled = _meter.CreateCounter<long>(
            "cena.photo_diagnostic.audit_sampled_total",
            description: "Diagnostic flagged for retrospective SME review by the audit sampler.");
    }

    public void RecordOcrConfidence(double confidence, string source)
    {
        _ocrConfidence.Record(confidence, new TagList { { "source", source } });
    }

    public void RecordTemplateScore(double score, MisconceptionBreakType breakType)
    {
        _templateScore.Record(score, new TagList { { "break_type", breakType.ToString() } });
    }

    public void RecordEndToEndLatency(double ms, bool succeeded)
    {
        _endToEndMs.Record(ms, new TagList { { "outcome", succeeded ? "succeeded" : "failed" } });
    }

    public void RecordLowConfidenceRefusal(string reason)
    {
        _lowConfidenceRefusals.Add(1, new TagList { { "reason", reason } });
    }

    public void RecordTemplateFallback(MisconceptionBreakType breakType)
    {
        _templateFallback.Add(1, new TagList { { "break_type", breakType.ToString() } });
    }

    public void RecordAuditSampled(string sampleReason)
    {
        _auditSampled.Add(1, new TagList { { "reason", sampleReason } });
    }

    public void Dispose() => _meter.Dispose();
}
