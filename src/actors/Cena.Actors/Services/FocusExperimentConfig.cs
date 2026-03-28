// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus A/B Testing Framework (FOC-010.1)
//
// Hash-based deterministic experiment assignment for validating
// focus engine research hypotheses with Cena's population
// (Israeli 16-18 year olds, Hebrew/Arabic, Bagrut math).
//
// 6 predefined experiments cover all research-backed focus features.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Manages focus experiment configuration and student assignment.
/// </summary>
public interface IFocusExperimentService
{
    /// <summary>
    /// Get the experiment arm for a student. Deterministic: same student → same arm.
    /// </summary>
    ExperimentArm GetAssignment(Guid studentId, string experimentId);

    /// <summary>
    /// Check if an experiment is currently active.
    /// </summary>
    bool IsActive(string experimentId);

    /// <summary>
    /// Get all active experiments.
    /// </summary>
    IReadOnlyList<FocusExperiment> GetActiveExperiments();
}

public sealed class FocusExperimentService : IFocusExperimentService
{
    private readonly IReadOnlyList<FocusExperiment> _experiments;

    public FocusExperimentService(IReadOnlyList<FocusExperiment>? experiments = null)
    {
        _experiments = experiments ?? DefaultExperiments;
    }

    public ExperimentArm GetAssignment(Guid studentId, string experimentId)
    {
        var experiment = FindExperiment(experimentId);
        if (experiment is null || !IsWithinDateRange(experiment))
            return ExperimentArm.Control; // Default to control if experiment not found/active

        // Hash-based deterministic assignment
        // Combine student ID and experiment ID to get a stable hash
        int hash = HashCode.Combine(studentId, experimentId);
        // Bitwise AND with 0x7FFFFFFF avoids OverflowException from Math.Abs(int.MinValue)
        int bucket = (hash & 0x7FFFFFFF) % 100;

        // Multi-arm support: distribute across arms based on percentages
        if (experiment.Arms.Count == 0)
            return ExperimentArm.Control;

        int cumulative = 0;
        for (int i = 0; i < experiment.Arms.Count; i++)
        {
            cumulative += experiment.Arms[i].PercentageAllocation;
            if (bucket < cumulative)
                return experiment.Arms[i].Arm;
        }

        return ExperimentArm.Control; // Fallback
    }

    public bool IsActive(string experimentId)
    {
        var experiment = FindExperiment(experimentId);
        return experiment is not null && IsWithinDateRange(experiment);
    }

    public IReadOnlyList<FocusExperiment> GetActiveExperiments()
    {
        var active = new List<FocusExperiment>();
        for (int i = 0; i < _experiments.Count; i++)
        {
            if (IsWithinDateRange(_experiments[i]))
                active.Add(_experiments[i]);
        }
        return active;
    }

    private FocusExperiment? FindExperiment(string experimentId)
    {
        for (int i = 0; i < _experiments.Count; i++)
        {
            if (_experiments[i].ExperimentId == experimentId)
                return _experiments[i];
        }
        return null;
    }

    private static bool IsWithinDateRange(FocusExperiment experiment)
    {
        var now = DateTimeOffset.UtcNow;
        return now >= experiment.StartDate && now <= experiment.EndDate;
    }

    // ═══════════════════════════════════════════════════════════════
    // 6 PREDEFINED EXPERIMENTS
    // ═══════════════════════════════════════════════════════════════

    public static readonly IReadOnlyList<FocusExperiment> DefaultExperiments = new[]
    {
        new FocusExperiment(
            ExperimentId: "foc-microbreaks",
            Name: "Proactive Microbreaks",
            Description: "Proactive 90s/10min breaks vs reactive only",
            PrimaryMetric: "post_break_accuracy_delta",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Reactive breaks only"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Proactive 90s/10min + reactive")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: "foc-boredom-fatigue",
            Name: "Boredom-Fatigue Split",
            Description: "Single disengaged state vs split bored/exhausted with differentiated intervention",
            PrimaryMetric: "return_rate_next_session",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Single Disengaged state"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Split Bored/Exhausted")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: "foc-confusion-patience",
            Name: "Confusion Patience Window",
            Description: "Intervene at 3 wrong vs wait for confusion resolution (5-question window)",
            PrimaryMetric: "delayed_test_score_1_week",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Intervene at 3 wrong answers"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Wait for confusion resolution (5-question window)")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: "foc-peak-time",
            Name: "Peak Time Adaptation",
            Description: "Fixed 15-min peak vs personalized chronotype-adjusted peak",
            PrimaryMetric: "false_positive_focus_degradation_rate",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Fixed 15-min peak"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Personalized chronotype-adjusted peak")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: "foc-solution-diversity",
            Name: "Solution Diversity Signal",
            Description: "Existing struggle classifier vs + solution diversity signal",
            PrimaryMetric: "productive_struggle_classification_accuracy",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Existing struggle classifier"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "+ solution diversity signal")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: "foc-sensor-enhanced",
            Name: "Sensor-Enhanced Focus",
            Description: "4-signal model vs 8-signal model (with sensors)",
            PrimaryMetric: "focus_state_accuracy_vs_self_report",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "4-signal model"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "8-signal model (with sensors)")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),

        // ═══════════════════════════════════════════════════════════
        // SAI-006: STUDENT AI INTERACTION EXPERIMENTS
        // ═══════════════════════════════════════════════════════════

        new FocusExperiment(
            ExperimentId: SaiAdaptiveExplanations,
            Name: "Adaptive Explanations",
            Description: "L1 static explanation only vs full L2+L3 personalized pipeline",
            PrimaryMetric: "next_concept_mastery_gain_1_week",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "L1 static explanation only"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Full L2 cache + L3 personalized pipeline")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: SaiHintBktWeighting,
            Name: "Hint BKT Credit Weighting",
            Description: "Hints with no BKT adjustment vs credit curve (1.0/0.7/0.4/0.1)",
            PrimaryMetric: "mastery_retention_1_week",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Hints delivered, BKT P_T unchanged"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Hints with credit curve: 1.0/0.7/0.4/0.1 P_T scaling")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        ),
        new FocusExperiment(
            ExperimentId: SaiConfusionGating,
            Name: "Confusion-Aware Delivery Gating",
            Description: "Always deliver hints/explanations vs DeliveryGate confusion+boredom gating",
            PrimaryMetric: "delayed_test_score_1_week",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Always deliver immediately (no DeliveryGate)"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Full DeliveryGate: confusion-aware + boredom-aware gating")
            },
            StartDate: DateTimeOffset.MinValue,
            EndDate: DateTimeOffset.MaxValue
        )
    };

    // SAI-006 experiment ID constants
    public const string SaiAdaptiveExplanations = "sai-adaptive-explanations";
    public const string SaiHintBktWeighting = "sai-hint-bkt-weighting";
    public const string SaiConfusionGating = "sai-confusion-gating";
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public record FocusExperiment(
    string ExperimentId,
    string Name,
    string Description,
    string PrimaryMetric,
    IReadOnlyList<ExperimentArmConfig> Arms,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate
);

public record ExperimentArmConfig(
    ExperimentArm Arm,
    int PercentageAllocation,  // 0-100
    string Description
);

public enum ExperimentArm
{
    Control,
    Treatment,
    TreatmentB  // For multi-arm (A/B/C) experiments
}
