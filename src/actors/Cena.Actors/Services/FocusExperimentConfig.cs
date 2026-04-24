// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus A/B Testing Framework (FOC-010.1)
//
// Hash-based deterministic experiment assignment for validating
// focus engine research hypotheses with Cena's population
// (Israeli 16-18 year olds, Hebrew/Arabic, Bagrut math).
//
// 6 predefined focus experiments + 3 SAI student AI interaction experiments (opt-in).
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
        //
        // These 3 experiments are OPT-IN: StartDate is far future so
        // they are NOT active by default. Activate by updating StartDate
        // to the desired launch date via configuration or migration.
        // ═══════════════════════════════════════════════════════════

        // ── Experiment 1: Explanation Tiers ──
        // 4 arms: measure learning impact of L1 vs L2 vs L3 explanations.
        // Control arm (no AI explanation) limited to 10% to minimise student impact.
        new FocusExperiment(
            ExperimentId: SaiExplanationTiers,
            Name: "Explanation Tiers",
            Description: "4-arm comparison: no explanation vs L1 static vs L2 cached vs L3 personalized",
            PrimaryMetric: "mastery_gain",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 10,
                    "No AI explanation — generic 'Incorrect. Try again.'"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 30,
                    "L1 static — AI-generated explanation from question aggregate"),
                new ExperimentArmConfig(ExperimentArm.TreatmentB, 30,
                    "L2 cached — error-type-specific cached explanation"),
                new ExperimentArmConfig(ExperimentArm.TreatmentC, 30,
                    "L3 personalized — full personalized explanation with student context")
            },
            StartDate: SaiOptInStartDate,
            EndDate: DateTimeOffset.MaxValue
        ),

        // ── Experiment 2: Hint BKT Credit Curve ──
        // 3 arms: validate the BKT credit curve for hint usage.
        // Each arm uses a different credit multiplier schedule (0/1/2/3 hints).
        new FocusExperiment(
            ExperimentId: SaiHintBktCredit,
            Name: "Hint BKT Credit Curve",
            Description: "3-arm comparison of BKT credit curves: aggressive / moderate / lenient penalty for hint usage",
            PrimaryMetric: "mastery_accuracy",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 34,
                    "Aggressive — credit curve 1.0 / 0.7 / 0.4 / 0.1 (strong hint penalty)"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 33,
                    "Moderate — credit curve 1.0 / 0.8 / 0.5 / 0.2 (moderate hint penalty)"),
                new ExperimentArmConfig(ExperimentArm.TreatmentB, 33,
                    "Lenient — credit curve 1.0 / 0.9 / 0.7 / 0.4 (light hint penalty)")
            },
            StartDate: SaiOptInStartDate,
            EndDate: DateTimeOffset.MaxValue
        ),

        // ── Experiment 3: Confusion Gating ──
        // 3 arms: validate whether respecting the confusion patience window improves learning.
        new FocusExperiment(
            ExperimentId: SaiConfusionGating,
            Name: "Confusion-Aware Delivery Gating",
            Description: "3-arm comparison: respect patience window vs immediate delivery vs student choice",
            PrimaryMetric: "confusion_resolution_rate",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 34,
                    "Patience — respect ConfusionDetector patience window, no hint during ConfusionResolving"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 33,
                    "Immediate — always deliver hints immediately when confusion detected, ignore patience window"),
                new ExperimentArmConfig(ExperimentArm.TreatmentB, 33,
                    "Student choice — show 'Need a hint?' prompt during ConfusionResolving, let student decide")
            },
            StartDate: SaiOptInStartDate,
            EndDate: DateTimeOffset.MaxValue
        )
    };

    // SAI-006 experiment ID constants
    public const string SaiExplanationTiers = "sai-explanation-tiers";
    public const string SaiHintBktCredit = "sai-hint-bkt-credit";
    public const string SaiConfusionGating = "sai-confusion-gating";

    /// <summary>
    /// SAI experiments are opt-in: this far-future start date ensures they are
    /// NOT active by default. To activate, override via configuration or set
    /// StartDate to the desired launch date in a Marten migration.
    /// </summary>
    internal static readonly DateTimeOffset SaiOptInStartDate = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
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
    TreatmentB,  // For multi-arm (A/B/C) experiments
    TreatmentC   // For 4-arm experiments (e.g. explanation_tiers: control/l1/l2/l3)
}
