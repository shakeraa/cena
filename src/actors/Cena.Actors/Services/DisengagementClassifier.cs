// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Disengagement Classifier (FOC-006.1)
//
// Distinguishes boredom from fatigue when a student disengages.
// Baker et al. (2010): "Better to be frustrated than bored."
// Pekrun (2006): Control-Value Theory — boredom arises from low control
// or low perceived value; fatigue from cognitive resource depletion.
//
// Critical: boredom and fatigue need OPPOSITE interventions.
//   Bored → increase challenge, change topic, add competition
//   Fatigued → take a break, rest, end session
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Classifies disengagement as boredom or fatigue based on behavioral signals.
/// </summary>
public interface IDisengagementClassifier
{
    DisengagementType Classify(DisengagementInput input);
}

public sealed class DisengagementClassifier : IDisengagementClassifier
{
    public DisengagementType Classify(DisengagementInput input)
    {
        int boredomSignals = 0;
        int fatigueSignals = 0;

        // ── Boredom signals ──

        // Fast correct answers = material too easy
        if (input.RecentAccuracy > 0.85 && input.ResponseTimeRatio < 0.7)
            boredomSignals += 2; // Strong signal — weighted double

        // Declining engagement despite high accuracy (doesn't need help)
        if (input.EngagementTrend < -0.05 && input.RecentAccuracy > 0.7)
            boredomSignals++;

        // Low hint usage (material is trivial)
        if (input.HintRequestRate < 0.02 && input.RecentAccuracy > 0.8)
            boredomSignals++;

        // Increasing app backgrounding (seeking stimulation elsewhere)
        if (input.AppBackgroundingRate > 0.3)
            boredomSignals++;

        // ── Fatigue signals ──

        // Increasing RT with declining accuracy = cognitive depletion
        if (input.ResponseTimeRatio > 1.3 && input.RecentAccuracy < 0.5)
            fatigueSignals += 2; // Strong signal

        // High vigilance time without break
        if (input.MinutesSinceLastBreak > 20)
            fatigueSignals++;

        // Decreasing touch pattern consistency = motor fatigue
        if (input.TouchPatternConsistencyDelta < -0.2)
            fatigueSignals++;

        // Multiple sessions today + late in session
        if (input.SessionsToday >= 3 && input.MinutesInSession > 15)
            fatigueSignals++;

        // Late evening study
        if (input.IsLateEvening)
            fatigueSignals++;

        // ── Classification ──
        if (boredomSignals >= 3 && fatigueSignals <= 1)
        {
            // Clear boredom: high accuracy + low engagement + fast answers
            return input.RecentAccuracy > 0.85
                ? DisengagementType.Bored_TooEasy
                : DisengagementType.Bored_NoValue;
        }

        if (fatigueSignals >= 3 && boredomSignals <= 1)
        {
            // Clear fatigue: slow + inaccurate + long session
            return input.TouchPatternConsistencyDelta < -0.3
                ? DisengagementType.Fatigued_Motor
                : DisengagementType.Fatigued_Cognitive;
        }

        if (boredomSignals >= 2 && fatigueSignals >= 2)
            return DisengagementType.Mixed;

        return DisengagementType.Unknown;
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public enum DisengagementType
{
    Bored_TooEasy,       // Material too easy — increase difficulty
    Bored_NoValue,       // Student can't see task value — change topic, add context
    Fatigued_Cognitive,  // Cognitive resources depleted — take a break
    Fatigued_Motor,      // Physical/motor fatigue — rest, stretch
    Mixed,               // Both boredom and fatigue signals
    Unknown              // Insufficient signals to classify
}

public record DisengagementInput(
    double RecentAccuracy,              // Accuracy over last 10 questions (0-1)
    double ResponseTimeRatio,           // Current RT / baseline RT (<1 = faster, >1 = slower)
    double EngagementTrend,             // Slope of engagement over session (-1 to 1)
    double HintRequestRate,             // Hints per question (0-1)
    double AppBackgroundingRate,         // Fraction of session time app was backgrounded (0-1)
    double MinutesSinceLastBreak,       // Minutes since last break (micro or recovery)
    double TouchPatternConsistencyDelta, // Change in touch consistency (-1 to 1, negative = worse)
    int SessionsToday,                  // Number of sessions today
    double MinutesInSession,            // Minutes elapsed in current session
    bool IsLateEvening                  // After 21:00 Israel time
);

/// <summary>
/// Non-break intervention for bored students.
/// Breaks won't help boredom — they need stimulation, not rest.
/// </summary>
public enum AlternativeAction
{
    None,                // No alternative action
    IncreaseDifficulty,  // Jump to harder problems
    ChangeTopic,         // Switch to a different concept
    AddChallenge,        // Enable challenge/competition mode
    EnableCompetition    // Add leaderboard or peer competition element
}
