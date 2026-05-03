// =============================================================================
// Cena Platform -- Elo Scoring
// MST-010: Student theta vs item difficulty rating system
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Elo-based expected correctness and dual-update rule.
/// Used by the item selector to calibrate question difficulty to the student.
/// </summary>
public static class EloScoring
{
    /// <summary>
    /// Expected probability of a correct response given student ability and item difficulty.
    /// Formula: 1 / (1 + 10^((itemDifficulty - studentTheta) / 400))
    /// </summary>
    public static float ExpectedCorrectness(float studentTheta, float itemDifficulty)
    {
        return 1.0f / (1.0f + MathF.Pow(10f, (itemDifficulty - studentTheta) / 400f));
    }

    /// <summary>
    /// Dual Elo update: student goes up on correct (down on incorrect),
    /// item difficulty adjusts inversely.
    /// K-factors: 40 for new students (&lt;20 attempts), decreasing to 10.
    /// </summary>
    public static (float NewTheta, float NewDifficulty) UpdateRatings(
        float studentTheta,
        float itemDifficulty,
        bool isCorrect,
        float studentK,
        float itemK)
    {
        float expected = ExpectedCorrectness(studentTheta, itemDifficulty);
        float score = isCorrect ? 1.0f : 0.0f;

        float newTheta = studentTheta + studentK * (score - expected);
        float newDifficulty = itemDifficulty + itemK * (expected - score);

        return (newTheta, newDifficulty);
    }

    /// <summary>
    /// Compute student K-factor based on attempt count.
    /// New students (fewer attempts) have higher K for faster calibration.
    /// </summary>
    public static float StudentKFactor(int totalAttempts)
    {
        if (totalAttempts < 20) return 40f;
        if (totalAttempts < 50) return 25f;
        return 10f;
    }
}
