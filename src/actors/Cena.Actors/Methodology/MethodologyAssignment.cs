// =============================================================================
// Cena Platform -- Methodology Assignment (Value Object)
// Hierarchical confidence-gated methodology tracking per level
// (Subject → Topic → Concept cascade)
// =============================================================================

namespace Cena.Actors.Methodology;

/// <summary>
/// The level at which a methodology assignment is made.
/// Resolution cascades from most specific to least specific.
/// </summary>
public enum MethodologyLevel
{
    Subject,
    Topic,
    Concept
}

/// <summary>
/// How the methodology was assigned at this level.
/// </summary>
public enum MethodologySource
{
    /// <summary>Inherited from a parent level (no local data).</summary>
    Inherited,
    /// <summary>Assigned by data-driven confidence gate (N >= threshold).</summary>
    DataDriven,
    /// <summary>Overridden manually by a teacher/admin.</summary>
    TeacherOverride,
    /// <summary>Assigned by the MCM error-type routing algorithm.</summary>
    McmRouted,
    /// <summary>Default from Bloom's progression (Layer 1 fallback).</summary>
    BloomsDefault
}

/// <summary>
/// Immutable assignment of a methodology at a given hierarchy level.
/// Tracks confidence metrics so the system knows when to promote from inherited to data-driven.
/// </summary>
public sealed record MethodologyAssignment
{
    public Students.Methodology Methodology { get; init; }
    public MethodologySource Source { get; init; }

    /// <summary>Confidence score (0-1). Only meaningful when Source is DataDriven.</summary>
    public float Confidence { get; init; }

    /// <summary>Total attempts evaluated for this level + methodology combination.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Correct attempts for this methodology at this level.</summary>
    public int CorrectCount { get; init; }

    /// <summary>Timestamp of the last methodology switch at this level.</summary>
    public DateTimeOffset LastSwitchAt { get; init; }

    /// <summary>UTC timestamp when the confidence gate was first crossed (N >= threshold).</summary>
    public DateTimeOffset? ConfidenceReachedAt { get; init; }

    /// <summary>Success rate: CorrectCount / AttemptCount.</summary>
    public float SuccessRate => AttemptCount == 0 ? 0f : CorrectCount / (float)AttemptCount;

    /// <summary>True when enough data has accumulated to be statistically meaningful.</summary>
    public bool HasSufficientData(int threshold) => AttemptCount >= threshold;

    // ── With-methods for immutable updates ──

    public MethodologyAssignment WithAttempt(bool correct) => this with
    {
        AttemptCount = AttemptCount + 1,
        CorrectCount = correct ? CorrectCount + 1 : CorrectCount,
        Confidence = ComputeConfidence(AttemptCount + 1, correct ? CorrectCount + 1 : CorrectCount)
    };

    public MethodologyAssignment WithSwitch(Students.Methodology newMethodology, MethodologySource source, DateTimeOffset timestamp) =>
        new()
        {
            Methodology = newMethodology,
            Source = source,
            Confidence = 0f,
            AttemptCount = 0,
            CorrectCount = 0,
            LastSwitchAt = timestamp
        };

    /// <summary>
    /// Compute confidence as a Wilson score lower bound (conservative estimate of success rate).
    /// This penalizes small sample sizes — a 3/3 success is ~0.44 confidence, not 1.0.
    /// </summary>
    private static float ComputeConfidence(int attempts, int correct)
    {
        if (attempts == 0) return 0f;

        // Wilson score interval lower bound (z=1.96 for 95% CI)
        const double z = 1.96;
        double n = attempts;
        double p = correct / n;

        double denominator = 1 + z * z / n;
        double centre = p + z * z / (2 * n);
        double spread = z * Math.Sqrt((p * (1 - p) + z * z / (4 * n)) / n);

        float lower = (float)((centre - spread) / denominator);
        return Math.Max(0f, lower);
    }

    // ── Factory ──

    public static MethodologyAssignment Default(Students.Methodology methodology, MethodologySource source) =>
        new()
        {
            Methodology = methodology,
            Source = source,
            Confidence = 0f,
            AttemptCount = 0,
            CorrectCount = 0,
            LastSwitchAt = DateTimeOffset.MinValue
        };
}
