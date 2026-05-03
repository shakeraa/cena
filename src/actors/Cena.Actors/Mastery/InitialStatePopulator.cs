// =============================================================================
// Cena Platform -- Initial State Populator
// MST-014: Converts diagnostic results into initial mastery overlay entries
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Converts KST diagnostic results into initial ConceptMasteryState entries.
/// Mastered concepts get optimistic defaults; gap concepts remain at zero.
/// Confidence-adjusted: lower diagnostic confidence → scaled-down initial values.
/// </summary>
public static class InitialStatePopulator
{
    private const float DefaultMastery = MasteryConstants.ProgressionThresholdF;
    private const float DefaultHalfLifeHours = 168f; // 1 week
    private const int DefaultBloomLevel = 3; // Apply level
    private const float HighConfidenceThreshold = 0.80f;

    /// <summary>
    /// Populate initial mastery state from diagnostic result.
    /// Only mastered concepts get entries; gap concepts stay at default (0.0).
    /// </summary>
    public static IReadOnlyDictionary<string, ConceptMasteryState> Populate(
        DiagnosticResult result,
        DateTimeOffset now)
    {
        var states = new Dictionary<string, ConceptMasteryState>();

        float confidence = result.Confidence;
        float masteryValue = confidence >= HighConfidenceThreshold
            ? DefaultMastery
            : DefaultMastery * confidence;
        float halfLife = confidence >= HighConfidenceThreshold
            ? DefaultHalfLifeHours
            : DefaultHalfLifeHours * confidence;

        foreach (var conceptId in result.MasteredConcepts)
        {
            states[conceptId] = new ConceptMasteryState
            {
                MasteryProbability = masteryValue,
                HalfLifeHours = halfLife,
                BloomLevel = DefaultBloomLevel,
                AttemptCount = 1,
                CorrectCount = 1,
                CurrentStreak = 1,
                LastInteraction = now,
                FirstEncounter = now
            };
        }

        return states;
    }

    /// <summary>
    /// Create a DiagnosticCompleted event for persistence.
    /// </summary>
    public static DiagnosticCompletedEvent CreateEvent(
        string studentId,
        DiagnosticResult result,
        DateTimeOffset now) =>
        new(studentId,
            result.MasteredConcepts.ToList(),
            result.GapConcepts.ToList(),
            result.QuestionsAsked,
            result.Confidence,
            now);
}

/// <summary>
/// Event persisted when the onboarding diagnostic completes.
/// </summary>
public sealed record DiagnosticCompletedEvent(
    string StudentId,
    IReadOnlyList<string> MasteredConceptIds,
    IReadOnlyList<string> GapConceptIds,
    int QuestionsAsked,
    float Confidence,
    DateTimeOffset Timestamp);
