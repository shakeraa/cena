// =============================================================================
// Cena Platform -- Difficulty Gap Computation
// Shared utility: computes the gap between question difficulty and student mastery.
// Positive = stretch (question harder than ability).
// Negative = regression (question easier than ability).
// Near zero = ZPD-appropriate.
//
// Research basis:
//   - Bjork (2011): Desirable difficulty for deeper learning
//   - Dweck (2006): Growth mindset framing for stretch vs. regression
//   - Vygotsky ZPD: Bottom/center/top of zone warrants different scaffolding
//   - Rohrer et al. (2015): Interleaved practice with varying difficulty
// =============================================================================

namespace Cena.Actors.Services;

/// <summary>
/// Classifies the difficulty frame based on the gap between question difficulty
/// and student mastery. Used by explanation, hint, and tutoring services to
/// calibrate response tone and depth.
/// </summary>
public enum DifficultyFrame
{
    /// <summary>Well above ability (gap > +0.3). Encourage, don't judge.</summary>
    Stretch,

    /// <summary>Above ability (gap +0.1 to +0.3). Normal productive struggle.</summary>
    Challenge,

    /// <summary>ZPD center (gap -0.1 to +0.1). Standard explanation.</summary>
    Appropriate,

    /// <summary>Below ability (gap -0.3 to -0.1). Should have gotten right.</summary>
    Expected,

    /// <summary>Well below ability (gap < -0.3). Investigate prerequisite decay.</summary>
    Regression
}

/// <summary>
/// Computes and classifies the gap between question difficulty and student mastery.
/// Thread-safe, stateless utility.
/// </summary>
public static class DifficultyGap
{
    /// <summary>
    /// Compute the raw gap. Positive = question harder than student ability.
    /// </summary>
    public static float Compute(float questionDifficulty, float masteryProbability)
        => questionDifficulty - masteryProbability;

    /// <summary>
    /// Classify the gap into a difficulty frame for response calibration.
    /// </summary>
    public static DifficultyFrame Classify(float gap) => gap switch
    {
        > 0.3f  => DifficultyFrame.Stretch,
        > 0.1f  => DifficultyFrame.Challenge,
        > -0.1f => DifficultyFrame.Appropriate,
        > -0.3f => DifficultyFrame.Expected,
        _       => DifficultyFrame.Regression
    };

    /// <summary>
    /// Convenience: compute + classify in one call.
    /// </summary>
    public static (float Gap, DifficultyFrame Frame) Analyze(float questionDifficulty, float masteryProbability)
    {
        var gap = Compute(questionDifficulty, masteryProbability);
        return (gap, Classify(gap));
    }

    /// <summary>
    /// Returns a human-readable label for the difficulty frame (used in LLM prompts).
    /// </summary>
    public static string Label(DifficultyFrame frame) => frame switch
    {
        DifficultyFrame.Stretch     => "stretch challenge (well above current level)",
        DifficultyFrame.Challenge   => "productive challenge (above current level)",
        DifficultyFrame.Appropriate => "ZPD-appropriate (matched to level)",
        DifficultyFrame.Expected    => "expected knowledge (should know this)",
        DifficultyFrame.Regression  => "regression alert (well below demonstrated level)",
        _ => "unknown"
    };

    /// <summary>
    /// Suggested max tokens for explanation based on difficulty frame.
    /// Stretch questions deserve longer explanations; regression needs concise diagnostics.
    /// </summary>
    public static int SuggestedMaxTokens(DifficultyFrame frame) => frame switch
    {
        DifficultyFrame.Stretch     => 500,     // Student is learning something new — explain fully
        DifficultyFrame.Challenge   => 400,     // Normal productive struggle
        DifficultyFrame.Appropriate => 350,     // Standard explanation
        DifficultyFrame.Expected    => 250,     // Brief — student knows this
        DifficultyFrame.Regression  => 200,     // Concise diagnostic — find the gap
        _ => 350
    };

    /// <summary>
    /// Human-readable framing for LLM prompts. Prepended to generation prompts
    /// to calibrate the LLM's tone and depth based on difficulty context.
    /// Returns empty string for Appropriate (no special framing needed).
    /// </summary>
    public static string ToPromptFrame(DifficultyFrame frame) => frame switch
    {
        DifficultyFrame.Stretch =>
            "This was a very challenging question above the student's current level. " +
            "Encourage effort, don't assume they should have known.",
        DifficultyFrame.Challenge =>
            "This question was above the student's comfort zone — a productive challenge.",
        DifficultyFrame.Appropriate => "",
        DifficultyFrame.Expected =>
            "This question should be within the student's ability. " +
            "Focus on identifying the specific gap or misconception.",
        DifficultyFrame.Regression =>
            "This question is below the student's demonstrated level. " +
            "Check whether a prerequisite concept has decayed or if this was a careless error.",
        _ => ""
    };

    /// <summary>
    /// Applies difficulty-aware scaling to a base max-token count.
    /// Stretch: +50% (student is learning something new, needs fuller explanation).
    /// Regression: -30% (student just needs a reminder, not a lecture).
    /// </summary>
    public static int AdjustMaxTokens(int baseTokens, DifficultyFrame frame) => frame switch
    {
        DifficultyFrame.Stretch    => (int)(baseTokens * 1.5),
        DifficultyFrame.Challenge  => (int)(baseTokens * 1.2),
        DifficultyFrame.Expected   => (int)(baseTokens * 0.85),
        DifficultyFrame.Regression => (int)(baseTokens * 0.7),
        _                          => baseTokens
    };
}
