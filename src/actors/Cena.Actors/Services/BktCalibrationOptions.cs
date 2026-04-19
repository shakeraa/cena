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
    /// <para>
    /// RDY-024b hardening: validates every probability is in (0, 1) and
    /// that the param combination doesn't degenerate BKT pedagogy. A
    /// malformed calibration JSON (NaN, negative probability, ≥1.0, or
    /// the degenerate PSlip+PGuess≥1 case) would silently poison
    /// mastery updates — throwing at conversion means the host fails
    /// to boot with a clear message rather than producing garbage.
    /// </para>
    /// </summary>
    public BktParameters ToBktParameters()
    {
        Validate(nameof(PLearning), PLearning);
        Validate(nameof(PSlip),     PSlip);
        Validate(nameof(PGuess),    PGuess);
        Validate(nameof(PForget),   PForget);
        Validate(nameof(PInitial),  PInitial);

        // Additional sanity checks specific to BKT pedagogy:
        //  - PSlip + PGuess < 1: otherwise a mastered student slipping
        //    is likelier than a non-mastered student guessing correctly,
        //    which inverts the BKT inference. EM calibration CAN emit
        //    this on sparse concepts; we want to catch it before prod.
        //  - PForget < PLearning: if decay outpaces acquisition the
        //    student can never cross the progression threshold.
        if (PSlip + PGuess >= 1.0)
            throw new InvalidOperationException(
                $"BKT params degenerate: PSlip ({PSlip}) + PGuess ({PGuess}) must be < 1.0.");
        if (PForget >= PLearning)
            throw new InvalidOperationException(
                $"BKT params unstable: PForget ({PForget}) must be < PLearning ({PLearning}).");

        return new(
            PLearning: PLearning,
            PSlip: PSlip,
            PGuess: PGuess,
            PForget: PForget,
            PInitial: PInitial,
            ProgressionThreshold: MasteryConstants.ProgressionThreshold,
            PrerequisiteGateThreshold: MasteryConstants.PrerequisiteGateThreshold);
    }

    private static void Validate(string name, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new InvalidOperationException(
                $"BKT param {name} must be a finite number (got {value}). Check config/bkt-params.json.");
        if (value <= 0.0 || value >= 1.0)
            throw new InvalidOperationException(
                $"BKT param {name} must be in (0, 1) — got {value}. Check config/bkt-params.json.");
    }
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
