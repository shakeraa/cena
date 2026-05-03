// =============================================================================
// Cena Platform -- HLR Feature Vector
// MST-003: Half-Life Regression feature inputs (stack-allocated, zero GC)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// HLR feature vector for half-life computation. Readonly struct for zero GC pressure.
/// </summary>
public readonly record struct HlrFeatures(
    int AttemptCount,
    int CorrectCount,
    float ConceptDifficulty,
    int PrerequisiteDepth,
    int BloomLevel,
    float DaysSinceFirstEncounter)
{
    private const int FeatureCount = 6;

    /// <summary>
    /// Returns feature vector as a span. Caller provides the buffer to avoid allocation.
    /// </summary>
    public void FillVector(Span<float> buffer)
    {
        if (buffer.Length < FeatureCount)
            throw new ArgumentException($"Buffer must have at least {FeatureCount} elements");

        buffer[0] = AttemptCount;
        buffer[1] = CorrectCount;
        buffer[2] = ConceptDifficulty;
        buffer[3] = PrerequisiteDepth;
        buffer[4] = BloomLevel;
        buffer[5] = DaysSinceFirstEncounter;
    }
}
