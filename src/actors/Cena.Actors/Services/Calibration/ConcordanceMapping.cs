// =============================================================================
// Cena Platform — Concordance Mapping (RDY-080)
//
// Versioned mapping from Cena IRT θ to Bagrut scaled score (0-100).
// Instances are immutable, pre-registered, and every consumer of the
// mapping (predictors, admin dashboards, audit exports) records the
// mapping version that produced the result.
//
// The mapping does NOT fit itself — fitting happens offline once enough
// calibration-cohort data has been collected per the study design
// (see docs/psychometrics/calibration-study-design.md). This record is
// the runtime envelope that carries the pre-fitted coefficients and
// the evidence that justifies the mapping's adequacy.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Actors.Services.Calibration;

/// <summary>
/// Functional family of the θ → Bagrut conversion. Cannot change
/// without pre-registration + Dr. Yael sign-off (study design §3.3).
/// </summary>
public enum ConcordanceModelKind
{
    /// <summary>Bagrut = β₀ + β₁ · θ + ε. Default family per study design.</summary>
    LinearV1,

    /// <summary>
    /// Two-piece linear split at a pre-registered knot. Used only if
    /// residual diagnostics reject LinearV1 adequacy on held-out data.
    /// </summary>
    PiecewiseLinearV1,

    /// <summary>
    /// Joint-IRT anchor-item equating: Cena θ is scaled onto the
    /// Ministry θ scale via common items, then mapped to scaled score
    /// via the published Ministry conversion table.
    /// </summary>
    CommonItemEquatingV1
}

/// <summary>
/// A single concordance mapping version. Immutable; supersession is
/// expressed by setting <see cref="SupersededAtUtc"/> on the prior
/// version and creating a new row with <c>Version = priorVersion + 1</c>.
/// </summary>
public sealed record ConcordanceMapping
{
    /// <summary>Monotonic version. Never re-used, never edited.</summary>
    public required int Version { get; init; }

    public required ConcordanceModelKind ModelKind { get; init; }

    /// <summary>
    /// Pre-registration identifier from the study protocol. Ties this
    /// mapping back to the signed-off design doc so auditors can see
    /// the model was not chosen post-hoc.
    /// </summary>
    public required string PreRegistrationId { get; init; }

    /// <summary>
    /// Model coefficients, JSON-serialised. Shape depends on
    /// <see cref="ModelKind"/>:
    ///   LinearV1:            {"beta0": double, "beta1": double, "sigma": double}
    ///   PiecewiseLinearV1:   {"knotTheta": double, "lower": {β0,β1}, "upper": {β0,β1}, "sigma": double}
    ///   CommonItemEquatingV1: {"anchorItems": [...], "ministryTableVersion": string}
    /// </summary>
    public required string CoefficientsJson { get; init; }

    public required int TrainingCohortSize { get; init; }
    public required int ValidationCohortSize { get; init; }

    /// <summary>
    /// SHA-256 hex of the ordered, deduplicated list of anonymised
    /// student IDs used to fit the mapping. Enables the audit question
    /// "was student X in the training cohort?" without retaining the
    /// list in cleartext.
    /// </summary>
    public required string TrainingCohortHash { get; init; }

    public required CalibrationAdequacy Adequacy { get; init; }

    /// <summary>
    /// Dr. Yael (or designated psychometrics approver). Null until
    /// approval is granted; mapping MUST NOT be used in production
    /// while <see cref="ApprovedBy"/> is null.
    /// </summary>
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }

    /// <summary>
    /// When a later version supersedes this one. Predictions made
    /// before this timestamp by this mapping remain attributable;
    /// new predictions use the successor.
    /// </summary>
    public DateTimeOffset? SupersededAtUtc { get; init; }

    /// <summary>
    /// True only when <see cref="Adequacy"/> clears the pre-registered
    /// bar (study design §3.5) AND <see cref="ApprovedBy"/> is set.
    /// While false, F8 point-estimate UI remains blocked; trajectory
    /// bands (RDY-071) show instead.
    /// </summary>
    [JsonIgnore]
    public bool F8PointEstimateEnabled
        => ApprovedBy is not null
           && SupersededAtUtc is null
           && Adequacy.ClearsBar;

    /// <summary>
    /// Apply the mapping to a single θ. Returns predicted scaled score
    /// and the standard error at that θ. Throws
    /// <see cref="InvalidOperationException"/> if the mapping is not
    /// yet approved (refuse to silently produce a number from an
    /// unapproved mapping).
    /// </summary>
    public BagrutPrediction Predict(double theta)
    {
        if (!F8PointEstimateEnabled)
            throw new InvalidOperationException(
                $"ConcordanceMapping v{Version} is not approved for prediction "
                + "(ApprovedBy is null OR adequacy did not clear the bar). "
                + "Callers MUST check F8PointEstimateEnabled before Predict.");

        // The actual prediction math lives in the per-model implementation;
        // this scaffold intentionally does not implement it so a
        // half-fitted model cannot ship a prediction through. The
        // production implementation will deserialize CoefficientsJson
        // and compute (point, SE) per ModelKind.
        throw new NotImplementedException(
            $"Predict() not implemented on scaffold. Implement per-model "
            + $"in a separate PR once {ModelKind} coefficients are real.");
    }
}

/// <summary>
/// Adequacy-test results from the held-out validation cohort.
/// Each field maps to a specific acceptance criterion in the study
/// design doc so auditors can trace "did this mapping actually pass?".
/// </summary>
public sealed record CalibrationAdequacy
{
    /// <summary>Mean absolute error on held-out cohort (Bagrut points).</summary>
    public required double HeldOutMae { get; init; }

    /// <summary>Root mean squared error on held-out cohort (Bagrut points).</summary>
    public required double HeldOutRmse { get; init; }

    /// <summary>
    /// Coverage of the held-out cohort within the stated 68% CI.
    /// Target ≥ 0.63 (allowing ±5pp slack on the ideal 0.68).
    /// </summary>
    public required double HeldOutCoverage68 { get; init; }

    /// <summary>
    /// Coverage within the stated 95% CI. Target ≥ 0.92.
    /// </summary>
    public required double HeldOutCoverage95 { get; init; }

    /// <summary>
    /// Mapping standard error at the cohort mean θ. The primary
    /// ship-criterion: must be ≤ 5 Bagrut points.
    /// </summary>
    public required double MappingStandardError { get; init; }

    /// <summary>
    /// Shapiro-Wilk p-value on residuals. > 0.05 clears the normality
    /// check for LinearV1.
    /// </summary>
    public required double ResidualsShapiroWilkP { get; init; }

    /// <summary>
    /// Breusch-Pagan p-value for heteroscedasticity. > 0.05 clears.
    /// </summary>
    public required double ResidualsBreuschPaganP { get; init; }

    /// <summary>
    /// 5-fold cross-validation RMSE. ≤ 5 clears.
    /// </summary>
    public required double CrossValidationRmse { get; init; }

    /// <summary>
    /// Whether all pre-registered adequacy tests cleared.
    /// This is the single bar for ship-readiness of the mapping.
    /// </summary>
    public bool ClearsBar
        => MappingStandardError <= 5.0
           && HeldOutMae <= 6.0
           && HeldOutCoverage68 >= 0.63
           && HeldOutCoverage95 >= 0.92
           && ResidualsShapiroWilkP > 0.05
           && ResidualsBreuschPaganP > 0.05
           && CrossValidationRmse <= 5.0;
}

/// <summary>
/// Output of a single <see cref="ConcordanceMapping.Predict"/> call.
/// Carries the mapping version so downstream consumers can show
/// "predicted by mapping v3 on 2026-09-15, SE 4.8".
/// </summary>
public sealed record BagrutPrediction(
    int MappingVersion,
    double PredictedScaledScore,
    double StandardError,
    DateTimeOffset ComputedAtUtc);
