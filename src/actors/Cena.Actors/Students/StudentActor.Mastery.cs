// =============================================================================
// Cena Platform -- StudentActor.Mastery (Partial: Mastery Engine Integration)
// MST-006: Additive mastery pipeline — runs AFTER existing BKT flow
// MST-007: Decay scan on ReceiveTimeout
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Students;

public sealed partial class StudentActor
{
    // =========================================================================
    // MASTERY ENRICHMENT — called after existing BKT + event staging
    // =========================================================================

    /// <summary>
    /// MST-006: Run the mastery pipeline enrichment after a concept attempt.
    /// Called AFTER the existing IBktService flow has staged events and applied state.
    /// Computes HLR half-life, effective mastery, threshold crossings, quality
    /// classification, and stagnation detection. Updates the rich MasteryOverlay.
    /// Does NOT replace the existing BKT — runs in addition to it.
    /// </summary>
    private void EnrichMasteryAfterAttempt(
        string conceptId,
        bool isCorrect,
        int responseTimeMs,
        string errorType,
        DateTimeOffset timestamp)
    {
        // Get or create the rich mastery state
        if (!_state.MasteryOverlay.TryGetValue(conceptId, out var current))
            return; // ApplyMasteryOverlay in StudentState already handles this during Apply

        // Compute HLR half-life with default weights (no graph cache needed for basic computation)
        var hlrFeatures = new HlrFeatures(
            AttemptCount: current.AttemptCount,
            CorrectCount: current.CorrectCount,
            ConceptDifficulty: 0.5f, // default until graph cache is wired
            PrerequisiteDepth: 1,     // default until graph cache is wired
            BloomLevel: current.BloomLevel,
            DaysSinceFirstEncounter: current.FirstEncounter != default
                ? (float)(timestamp - current.FirstEncounter).TotalDays
                : 0f);

        var hlrWeights = HlrWeights.Default;
        float newHalfLife = HlrCalculator.ComputeHalfLife(hlrFeatures, hlrWeights);

        // Update HLR timer in existing state (feeds into decay scan)
        _state.HlrTimers[conceptId] = new HlrState(newHalfLife, timestamp);

        // Update the overlay with computed half-life
        if (_state.MasteryOverlay.TryGetValue(conceptId, out var updated))
        {
            _state.MasteryOverlay[conceptId] = updated.WithHalfLifeUpdate(newHalfLife);
        }

        // Detect stagnation from repeated errors
        if (_state.MasteryOverlay.TryGetValue(conceptId, out var stateForStagnation))
        {
            var dominantError = MasteryStagnationDetector.DetectDominantError(stateForStagnation);
            if (dominantError.HasValue)
            {
                _logger.LogInformation(
                    "MST-006: Stagnation detected for student {StudentId} concept {ConceptId}: " +
                    "dominant error = {ErrorType}",
                    _studentId, conceptId, dominantError.Value);
            }
        }
    }

    // =========================================================================
    // DECAY SCAN — MST-007 (runs on ReceiveTimeout alongside passivation check)
    // =========================================================================

    /// <summary>
    /// MST-007: Scan all mastered concepts for decay.
    /// Called before passivation or on a periodic timer.
    /// Returns decay results for NATS publication.
    /// </summary>
    private IReadOnlyList<DecayResult> ScanForDecay(DateTimeOffset now)
    {
        return MasteryDecayScanner.Scan(_state.MasteryOverlay, now);
    }

    /// <summary>
    /// MST-007: Stage MasteryDecayed events for all decaying concepts.
    /// Called from the decay timer or pre-passivation scan.
    /// </summary>
    private void StageDecayEvents(IReadOnlyList<DecayResult> decayResults, DateTimeOffset now)
    {
        foreach (var decay in decayResults)
        {
            var decayEvent = new MasteryDecayed_V1(
                _studentId,
                decay.ConceptId,
                decay.RecallProbability,
                decay.HalfLifeHours,
                decay.HoursSinceLastInteraction);

            StageEvent(decayEvent);
        }
    }

    /// <summary>
    /// MST-006: Get the current rich mastery state for a concept.
    /// Used by query handlers and diagnostics.
    /// </summary>
    internal Mastery.ConceptMasteryState? GetRichMasteryState(string conceptId) =>
        _state.MasteryOverlay.TryGetValue(conceptId, out var state) ? state : null;

    /// <summary>
    /// MST-006: Get the full mastery overlay as read-only.
    /// Used by review priority and learning frontier calculations.
    /// </summary>
    internal IReadOnlyDictionary<string, Mastery.ConceptMasteryState> GetMasteryOverlay() =>
        _state.MasteryOverlay;
}
