// =============================================================================
// Cena Platform -- Effective Mastery Calculator
// MST-005: Composite signal combining BKT, HLR recall, and prerequisites
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Computes effective mastery: the single number that answers
/// "how well does this student know this concept right now?"
/// Formula: min(P(L), p_recall) * prereq_support.
/// </summary>
public static class EffectiveMasteryCalculator
{
    /// <summary>
    /// Compute effective mastery combining BKT probability, recall, and prerequisite support.
    /// </summary>
    public static float Compute(ConceptMasteryState state, float prereqSupport, DateTimeOffset now)
    {
        if (prereqSupport <= 0f)
            return 0.0f;

        if (state.LastInteraction == default)
            return 0.0f;

        float recall = state.RecallProbability(now);
        float effective = Math.Min(state.MasteryProbability, recall) * prereqSupport;
        return Math.Clamp(effective, 0.0f, 1.0f);
    }

    /// <summary>
    /// Detect threshold crossing between previous and new effective mastery.
    /// Returns null if no threshold is crossed.
    /// </summary>
    public static MasteryThresholdEvent? DetectThresholdCrossing(
        float previousEffective, float newEffective)
    {
        // Upward crossing past 0.90 -> ConceptMastered
        if (previousEffective < MasteryThreshold.Proficient &&
            newEffective >= MasteryThreshold.Proficient)
            return MasteryThresholdEvent.ConceptMastered;

        // Downward crossing below 0.70 -> MasteryDecayed
        if (previousEffective >= MasteryThreshold.Developing &&
            newEffective < MasteryThreshold.Developing)
            return MasteryThresholdEvent.MasteryDecayed;

        // Downward crossing below 0.60 -> PrerequisiteBlocked
        if (previousEffective >= MasteryThreshold.PrerequisiteGate &&
            newEffective < MasteryThreshold.PrerequisiteGate)
            return MasteryThresholdEvent.PrerequisiteBlocked;

        return null;
    }
}
