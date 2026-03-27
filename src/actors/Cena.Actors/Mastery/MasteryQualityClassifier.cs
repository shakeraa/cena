// =============================================================================
// Cena Platform -- Mastery Quality Classifier
// MST-012: 2x2 matrix of (fast/slow) x (correct/incorrect)
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Classifies responses into a 2x2 quality matrix using the student's
/// personal response time median as the fast/slow threshold.
/// </summary>
public static class MasteryQualityClassifier
{
    /// <summary>
    /// Classify a single response: (fast/slow) x (correct/incorrect).
    /// Fast = responseTimeMs &lt; medianResponseTimeMs.
    /// </summary>
    public static MasteryQuality Classify(bool isCorrect, int responseTimeMs, float medianResponseTimeMs)
    {
        bool isFast = responseTimeMs < medianResponseTimeMs;

        return (isFast, isCorrect) switch
        {
            (true, true) => MasteryQuality.Mastered,
            (false, true) => MasteryQuality.Effortful,
            (true, false) => MasteryQuality.Careless,
            (false, false) => MasteryQuality.Struggling,
        };
    }

    /// <summary>
    /// Classify the response and update the baseline in one step.
    /// Called by the StudentActor mastery handler after BKT/HLR updates.
    /// </summary>
    public static (MasteryQuality Quality, ResponseTimeBaseline UpdatedBaseline) ClassifyAndUpdate(
        bool isCorrect,
        int responseTimeMs,
        ResponseTimeBaseline baseline)
    {
        var quality = Classify(isCorrect, responseTimeMs, baseline.MedianResponseTimeMs);
        var updatedBaseline = baseline.Update(responseTimeMs);
        return (quality, updatedBaseline);
    }
}
