// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus Experiment Metrics Collector (FOC-010.2)
//
// Collects per-student per-session metrics for focus A/B experiments.
// All metrics are tagged with experiment arm for offline analysis.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Collects and stores experiment metrics per session.
/// </summary>
public interface IFocusExperimentCollector
{
    void RecordSessionMetrics(ExperimentSessionMetrics metrics);
    IReadOnlyList<ExperimentSessionMetrics> GetMetrics(string experimentId);
    IReadOnlyList<ExperimentSessionMetrics> GetMetricsForStudent(Guid studentId, string experimentId);
}

public sealed class FocusExperimentCollector : IFocusExperimentCollector
{
    // Cap in-memory metrics to prevent unbounded growth.
    // Oldest entries are evicted when capacity is reached.
    // Production should persist to Marten/Postgres before eviction.
    private const int MaxMetrics = 100_000;

    private readonly List<ExperimentSessionMetrics> _metrics = new();
    private readonly object _lock = new();

    public void RecordSessionMetrics(ExperimentSessionMetrics metrics)
    {
        lock (_lock)
        {
            if (_metrics.Count >= MaxMetrics)
                _metrics.RemoveAt(0); // Evict oldest
            _metrics.Add(metrics);
        }
    }

    public IReadOnlyList<ExperimentSessionMetrics> GetMetrics(string experimentId)
    {
        lock (_lock)
        {
            var result = new List<ExperimentSessionMetrics>();
            for (int i = 0; i < _metrics.Count; i++)
            {
                if (_metrics[i].ExperimentId == experimentId)
                    result.Add(_metrics[i]);
            }
            return result;
        }
    }

    public IReadOnlyList<ExperimentSessionMetrics> GetMetricsForStudent(Guid studentId, string experimentId)
    {
        lock (_lock)
        {
            var result = new List<ExperimentSessionMetrics>();
            for (int i = 0; i < _metrics.Count; i++)
            {
                if (_metrics[i].StudentId == studentId && _metrics[i].ExperimentId == experimentId)
                    result.Add(_metrics[i]);
            }
            return result;
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public record ExperimentSessionMetrics(
    Guid StudentId,
    Guid SessionId,
    string ExperimentId,
    ExperimentArm Arm,
    DateTimeOffset Timestamp,

    // ── Focus metrics ──
    double? FocusStateAccuracy,          // Model prediction vs self-report (0-1)
    double? BreakEffectiveness,          // Post-break accuracy - pre-break accuracy
    double? MicrobreakComplianceRate,    // Fraction of suggested microbreaks taken (0-1)

    // ── Engagement metrics ──
    bool? ReturnedNextSession,           // Did student come back within 48h?
    double? NextSessionPerformanceDelta, // Accuracy improvement in next session

    // ── Struggle metrics ──
    double? ProductiveStrugglePrecision, // True positive rate of productive struggle classification

    // ── Self-report (post-session) ──
    int? SelfReportedFocus,             // 1-5 scale: "How focused were you?" (Hebrew/Arabic)
    string? SelfReportLanguage          // "he" or "ar" — for bilingual analysis
);
