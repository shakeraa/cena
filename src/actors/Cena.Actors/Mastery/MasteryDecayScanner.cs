// =============================================================================
// Cena Platform -- Mastery Decay Scanner
// MST-006/MST-007: Scans mastery overlay for decaying concepts
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Result of a decay scan for a single concept.
/// </summary>
public sealed record DecayResult(
    string ConceptId,
    float RecallProbability,
    float HalfLifeHours,
    float HoursSinceLastInteraction);

/// <summary>
/// Scans a mastery overlay for concepts that have decayed below threshold.
/// Pure computation — does not emit events (the actor does that).
/// Scan of 200 concepts completes in microseconds (pure arithmetic per concept).
/// </summary>
public static class MasteryDecayScanner
{
    /// <summary>
    /// Scan all mastered concepts for decay. Returns concepts where
    /// mastery >= minMastery AND recall &lt; decayThreshold.
    /// </summary>
    public static IReadOnlyList<DecayResult> Scan(
        IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay,
        DateTimeOffset now,
        float decayThreshold = 0.70f,
        float minMasteryForScan = 0.70f)
    {
        var results = new List<DecayResult>();

        foreach (var (conceptId, state) in masteryOverlay)
        {
            if (state.MasteryProbability < minMasteryForScan)
                continue;

            if (state.LastInteraction == default || state.HalfLifeHours <= 0)
                continue;

            float recall = state.RecallProbability(now);
            if (recall >= decayThreshold)
                continue;

            float hoursSince = (float)(now - state.LastInteraction).TotalHours;
            results.Add(new DecayResult(conceptId, recall, state.HalfLifeHours, hoursSince));
        }

        return results;
    }
}
