// =============================================================================
// Cena Platform -- Mastery Pipeline
// MST-005: Full computation pipeline: BKT -> HLR -> effective mastery -> threshold
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Result of processing a concept attempt through the full mastery pipeline.
/// </summary>
public sealed record MasteryUpdateResult(
    ConceptMasteryState NewState,
    float EffectiveMastery,
    MasteryThresholdEvent? ThresholdEvent);

/// <summary>
/// Full mastery computation pipeline. Pure computation -- does NOT emit events
/// (that is the actor's job).
/// </summary>
public static class MasteryPipeline
{
    /// <summary>
    /// Process a concept attempt through BKT -> HLR -> effective mastery -> threshold detection.
    /// </summary>
    public static MasteryUpdateResult ProcessAttempt(
        ConceptMasteryState currentState,
        bool isCorrect,
        BktParameters bktParams,
        HlrFeatures hlrFeatures,
        HlrWeights hlrWeights,
        float prereqSupport,
        DateTimeOffset now)
    {
        // Compute previous effective mastery for threshold crossing detection
        float previousEffective = EffectiveMasteryCalculator.Compute(
            currentState, prereqSupport, now);

        // Step 1: Record the attempt (counters, streak, timestamp)
        var state = currentState.WithAttempt(isCorrect, now);

        // Step 2: BKT update (mastery probability)
        state = BktTracer.UpdateState(state, isCorrect, bktParams);

        // Step 3: HLR update (half-life)
        state = HlrCalculator.UpdateState(state, hlrFeatures, hlrWeights);

        // Step 4: Compute new effective mastery
        float newEffective = EffectiveMasteryCalculator.Compute(state, prereqSupport, now);

        // Step 5: Detect threshold crossing
        var thresholdEvent = EffectiveMasteryCalculator.DetectThresholdCrossing(
            previousEffective, newEffective);

        return new MasteryUpdateResult(state, newEffective, thresholdEvent);
    }
}
