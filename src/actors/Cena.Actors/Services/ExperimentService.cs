// =============================================================================
// Cena Platform -- A/B Experiment Service (SAI-05)
// Hash-based deterministic arm assignment + metric recording for offline analysis.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Assigns students to experiment arms deterministically and records metrics.
/// </summary>
public interface IExperimentService
{
    /// <summary>
    /// Returns the experiment arm for a student. Deterministic: same student + experiment
    /// always yields the same arm, without needing to persist assignment state.
    /// </summary>
    string GetArm(string studentId, string experimentName);

    /// <summary>
    /// Records a metric data point for offline A/B analysis.
    /// </summary>
    void RecordMetric(string studentId, string sessionId, string experimentName, string metricName, double value);
}

public sealed class ExperimentService : IExperimentService
{
    private readonly ILogger<ExperimentService> _logger;

    public ExperimentService(ILogger<ExperimentService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string GetArm(string studentId, string experimentName)
    {
        var hash = HashCode.Combine(studentId, experimentName);
        var experiments = GetExperimentDefinitions();

        if (!experiments.TryGetValue(experimentName, out var arms))
        {
            _logger.LogWarning("Unknown experiment {Experiment}, defaulting to control", experimentName);
            return "control";
        }

        var idx = Math.Abs(hash) % arms.Length;
        return arms[idx];
    }

    /// <inheritdoc />
    public void RecordMetric(string studentId, string sessionId, string experimentName, string metricName, double value)
    {
        // TODO: persist to Marten document for offline analysis
        _logger.LogDebug(
            "Experiment metric: student={StudentId} session={SessionId} experiment={Experiment} metric={Metric} value={Value}",
            studentId, sessionId, experimentName, metricName, value);
    }

    private static Dictionary<string, string[]> GetExperimentDefinitions() => new()
    {
        ["explanation_quality"] = ["control", "l2_cached", "l3_personalized"],
        ["hint_progression"] = ["control", "hints_no_bkt_adjust", "hints_with_bkt_adjust"]
    };
}
