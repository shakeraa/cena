// =============================================================================
// Cena Platform — BKT Calibration Options (RDY-024)
// IOptions<T> binding for config/bkt-params.json.
// Phase A: infrastructure. Phase B: EM-calibrated values replace defaults.
// =============================================================================

namespace Cena.Actors.Services;

/// <summary>
/// Per-subject BKT parameter overrides loaded from configuration.
/// All probabilities must be in (0, 1).
/// </summary>
public sealed class SubjectBktParams
{
    public double PLearning { get; set; } = 0.10;
    public double PSlip { get; set; } = 0.05;
    public double PGuess { get; set; } = 0.20;
    public double PForget { get; set; } = 0.02;
    public double PInitial { get; set; } = 0.10;

    /// <summary>
    /// Convert to the immutable <see cref="BktParameters"/> struct used by
    /// the BKT engine, inheriting progression/prerequisite thresholds from
    /// <see cref="MasteryConstants"/>.
    /// </summary>
    public BktParameters ToBktParameters() => new(
        PLearning: PLearning,
        PSlip: PSlip,
        PGuess: PGuess,
        PForget: PForget,
        PInitial: PInitial,
        ProgressionThreshold: MasteryConstants.ProgressionThreshold,
        PrerequisiteGateThreshold: MasteryConstants.PrerequisiteGateThreshold);
}

/// <summary>
/// Root configuration object bound from the "BktCalibration" section.
/// Maps to <c>config/bkt-params.json</c> (version-controlled).
/// </summary>
public sealed class BktCalibrationOptions
{
    public const string SectionName = "BktCalibration";

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;

    /// <summary>ISO-8601 timestamp of last calibration run, null if uncalibrated.</summary>
    public string? CalibratedAt { get; set; }

    /// <summary>Human-readable description of calibration source.</summary>
    public string? CalibrationSource { get; set; }

    /// <summary>
    /// Fallback BKT parameters used when no subject-specific override exists.
    /// </summary>
    public SubjectBktParams Defaults { get; set; } = new();

    /// <summary>
    /// Per-subject BKT parameter overrides keyed by lowercase subject name
    /// (e.g. "algebra", "geometry", "trigonometry").
    /// </summary>
    public Dictionary<string, SubjectBktParams> Subjects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
