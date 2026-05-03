// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Confusion Detector (FOC-005.1)
//
// Detects cognitive confusion and distinguishes it from frustration.
// D'Mello & Graesser (2012, 2014): Confusion caused by cognitive
// disequilibrium is BENEFICIAL for deep learning — if resolved.
// Frustration from persistent failure is HARMFUL.
//
// Key: Confusion → IF resolved → deep learning (DO NOT interrupt)
//      Frustration → IF persistent → learned helplessness (INTERVENE)
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Detects confusion signals from student behavior during a learning session.
/// </summary>
public interface IConfusionDetector
{
    ConfusionState Detect(ConfusionInput input);
}

public sealed class ConfusionDetector : IConfusionDetector
{
    /// <summary>
    /// Analyzes behavioral signals to detect confusion state.
    ///
    /// Confusion signals (any 2+ triggers confusion detection):
    /// 1. Wrong answer on previously-mastered concept (unexpected error)
    /// 2. Longer RT followed by correct answer (thinking through confusion)
    /// 3. Changed answer mid-submission (reconsidered)
    /// 4. Requested hint then cancelled (tried to solve on their own)
    /// </summary>
    public ConfusionState Detect(ConfusionInput input)
    {
        int confusionSignals = 0;

        // Signal 1: Unexpected error on mastered concept
        if (input.WrongOnMasteredConcept)
            confusionSignals++;

        // Signal 2: Elevated RT followed by correct answer (thinking)
        if (input.ResponseTimeRatio > 1.5 && input.LastAnswerCorrect)
            confusionSignals++;

        // Signal 3: Changed answer mid-submission
        if (input.AnswerChangedCount > 0)
            confusionSignals++;

        // Signal 4: Requested hint then cancelled
        if (input.HintRequestedThenCancelled)
            confusionSignals++;

        // ── Not enough signals → not confused ──
        if (confusionSignals < 2)
            return ConfusionState.NotConfused;

        // ── Check resolution status ──
        if (input.QuestionsInConfusionWindow > 0)
        {
            // Student has been confused for a while — is it resolving?
            if (input.AccuracyInConfusionWindow > 0.5)
                return ConfusionState.ConfusionResolving;

            // Exceeded patience window without resolution
            if (input.QuestionsInConfusionWindow >= input.PatienceWindowSize)
                return ConfusionState.ConfusionStuck;
        }

        // ── Fresh confusion detected ──
        return ConfusionState.Confused;
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public enum ConfusionState
{
    NotConfused,        // No confusion signals
    Confused,           // Confusion just detected — start monitoring
    ConfusionResolving, // Confused but accuracy recovering — DO NOT intervene
    ConfusionStuck      // Confusion persists past patience window — scaffold
}

public record ConfusionInput(
    bool WrongOnMasteredConcept,       // Unexpected error on concept with mastery > 0.7
    double ResponseTimeRatio,           // Current RT / baseline RT (>1.5 = slower than usual)
    bool LastAnswerCorrect,            // Did they get the last question right (after thinking)?
    int AnswerChangedCount,            // How many times they changed their answer before submitting
    bool HintRequestedThenCancelled,   // Asked for hint, then dismissed it
    int QuestionsInConfusionWindow,    // How many questions since confusion was first detected
    double AccuracyInConfusionWindow,  // Accuracy within the confusion monitoring window (0-1)
    int PatienceWindowSize = 5         // How many questions to wait before declaring "stuck"
);
