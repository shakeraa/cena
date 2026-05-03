// =============================================================================
// Cena Platform — OCR Cascade Metrics (RDY-OCR-OBSERVABILITY / Phase 4)
//
// Central OpenTelemetry/System.Diagnostics.Metrics instrumentation for the
// ADR-0033 cascade. Single Meter "Cena.Infrastructure.Ocr" so hosts can
// enable it with one `AddMeter("Cena.Infrastructure.Ocr")` call on their
// OpenTelemetry MeterProvider.
//
// Metric names and tag keys are snake_case to match the convention in
// the existing Cena metrics (see Cena.HttpCircuitBreaker, Cena.Actors.*).
//
// Recording contract (from OcrCascadeService.RecognizeAsync):
//   - Every call records exactly one RecordRequest + one RecordTotalLatency.
//   - Every layer records RecordLayerLatency with its own layer tag.
//   - Layer 4 records RecordFallbackFired once per fallback fired.
//   - Layer 5 records RecordCasVerdict once per math block validated/failed.
//   - HumanReviewRequired=true → RecordHumanReviewFlagged with reason list.
//
// NO STUBS. MeterListener-based tests (OcrMetricsTests) verify every metric
// fires on the expected code path.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cena.Infrastructure.Ocr.Observability;

public sealed class OcrMetrics : IDisposable
{
    public const string MeterName = "Cena.Infrastructure.Ocr";
    public const string MeterVersion = "1.0";

    private readonly Meter _meter;

    private readonly Counter<long> _requests;
    private readonly Counter<long> _fallbacksFired;
    private readonly Counter<long> _casVerdicts;
    private readonly Counter<long> _humanReviewFlagged;

    private readonly Histogram<double> _totalLatencyMs;
    private readonly Histogram<double> _layerLatencyMs;

    public OcrMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName, MeterVersion) ?? new Meter(MeterName, MeterVersion);

        _requests = _meter.CreateCounter<long>(
            name: "ocr.cascade.requests",
            unit: "{request}",
            description: "Total OCR cascade invocations, tagged by surface + content_type + outcome.");

        _fallbacksFired = _meter.CreateCounter<long>(
            name: "ocr.cascade.fallbacks_fired",
            unit: "{fallback}",
            description: "Layer 4 cloud fallback invocations (mathpix / gemini_vision / tesseract_retry).");

        _casVerdicts = _meter.CreateCounter<long>(
            name: "ocr.cas.verdicts",
            unit: "{verdict}",
            description: "Layer 5 SymPy verdicts per math block (verified / failed / unverifiable).");

        _humanReviewFlagged = _meter.CreateCounter<long>(
            name: "ocr.human_review.flagged",
            unit: "{flag}",
            description: "Items that exited the cascade with human_review_required=true.");

        _totalLatencyMs = _meter.CreateHistogram<double>(
            name: "ocr.cascade.total_latency",
            unit: "ms",
            description: "End-to-end cascade latency per request, tagged by surface + outcome.");

        _layerLatencyMs = _meter.CreateHistogram<double>(
            name: "ocr.layer.latency",
            unit: "ms",
            description: "Per-layer latency (layer 0..5) tagged by surface.");
    }

    /// <summary>
    /// Record one completed cascade call. Outcome is one of:
    /// <c>ok</c> | <c>low_conf</c> | <c>cas_fail</c> | <c>circuit_open</c> |
    /// <c>input_err</c> | <c>encrypted_pdf</c>.
    /// </summary>
    public void RecordRequest(CascadeSurface surface, string contentType, string outcome)
    {
        _requests.Add(1,
            new KeyValuePair<string, object?>("surface", SurfaceTag(surface)),
            new KeyValuePair<string, object?>("content_type", contentType),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordTotalLatency(CascadeSurface surface, string outcome, double latencyMs)
    {
        _totalLatencyMs.Record(latencyMs,
            new KeyValuePair<string, object?>("surface", SurfaceTag(surface)),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordLayerLatency(string layer, CascadeSurface surface, double latencyMs)
    {
        _layerLatencyMs.Record(latencyMs,
            new KeyValuePair<string, object?>("layer", layer),
            new KeyValuePair<string, object?>("surface", SurfaceTag(surface)));
    }

    /// <summary>
    /// Record a Layer-4 fallback firing. <paramref name="fallback"/> is the
    /// runner name ("mathpix", "gemini_vision", "tesseract_retry"); reason
    /// is one of ("low_conf", "math_complex", "cas_fail").
    /// </summary>
    public void RecordFallbackFired(string fallback, string reason)
    {
        _fallbacksFired.Add(1,
            new KeyValuePair<string, object?>("fallback", fallback),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Record a per-math-block CAS verdict: <c>verified</c>, <c>failed</c>,
    /// or <c>unverifiable</c>.
    /// </summary>
    public void RecordCasVerdict(string verdict)
    {
        _casVerdicts.Add(1,
            new KeyValuePair<string, object?>("verdict", verdict));
    }

    public void RecordHumanReviewFlagged(CascadeSurface surface, string reason)
    {
        _humanReviewFlagged.Add(1,
            new KeyValuePair<string, object?>("surface", SurfaceTag(surface)),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Helper: run a code path under a Stopwatch and record its elapsed
    /// time as a layer latency. Returns the result of the delegate.
    /// </summary>
    public async Task<T> MeasureLayerAsync<T>(
        string layer, CascadeSurface surface, Func<Task<T>> fn)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await fn().ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            RecordLayerLatency(layer, surface, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static string SurfaceTag(CascadeSurface surface) => surface switch
    {
        CascadeSurface.StudentInteractive => "A",
        CascadeSurface.AdminBatch         => "B",
        _                                 => "unknown",
    };

    public void Dispose() => _meter.Dispose();
}
