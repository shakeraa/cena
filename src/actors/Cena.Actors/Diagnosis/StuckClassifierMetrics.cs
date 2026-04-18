// =============================================================================
// Cena Platform — StuckClassifierMetrics (RDY-063 Phase 1)
//
// Single source for the classifier's OTel meter + counters. Registered
// as a singleton in DI; HybridStuckClassifier and the repository both
// use it. Kept as a dedicated class (rather than inline in the
// classifier) so that tests can inject a no-op or capturing impl.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cena.Actors.Diagnosis;

public sealed class StuckClassifierMetrics : IDisposable
{
    public const string MeterName = "Cena.Actors.Diagnosis.StuckClassifier";

    private readonly Meter _meter;
    private readonly Counter<long> _diagnosesTotal;
    private readonly Counter<long> _actionableTotal;
    private readonly Counter<long> _lowConfidenceTotal;
    private readonly Histogram<int> _latencyMs;
    private readonly Counter<long> _persistedTotal;
    private readonly Counter<long> _persistFailureTotal;

    public StuckClassifierMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName, "1.0.0");
        _diagnosesTotal = _meter.CreateCounter<long>(
            "cena.stuck.diagnoses_total",
            description: "Stuck-type diagnosis count, partitioned by source and label.");
        _actionableTotal = _meter.CreateCounter<long>(
            "cena.stuck.actionable_total",
            description: "Diagnoses whose confidence cleared the actionable threshold.");
        _lowConfidenceTotal = _meter.CreateCounter<long>(
            "cena.stuck.low_confidence_total",
            description: "Diagnoses below the actionable threshold (fallback invoked).");
        _latencyMs = _meter.CreateHistogram<int>(
            "cena.stuck.diagnose_latency_ms",
            unit: "ms",
            description: "End-to-end diagnosis latency including LLM round-trip when taken.");
        _persistedTotal = _meter.CreateCounter<long>(
            "cena.stuck.persisted_total",
            description: "Diagnoses persisted to StuckDiagnosisDocument.");
        _persistFailureTotal = _meter.CreateCounter<long>(
            "cena.stuck.persist_failure_total",
            description: "Persistence failures (non-fatal; classifier output still returned).");
    }

    public void RecordDiagnosis(StuckDiagnosisSource source, StuckType primary, bool actionable)
    {
        var tags = new TagList
        {
            { "source", source.ToString() },
            { "primary", primary.ToString() },
        };
        _diagnosesTotal.Add(1, tags);
        (actionable ? _actionableTotal : _lowConfidenceTotal).Add(1, tags);
    }

    public void RecordLatency(int latencyMs, StuckDiagnosisSource source)
    {
        _latencyMs.Record(latencyMs, new TagList { { "source", source.ToString() } });
    }

    public void RecordPersistSuccess(StuckType primary) =>
        _persistedTotal.Add(1, new TagList { { "primary", primary.ToString() } });

    public void RecordPersistFailure(string reason) =>
        _persistFailureTotal.Add(1, new TagList { { "reason", reason } });

    public void Dispose() => _meter.Dispose();
}
