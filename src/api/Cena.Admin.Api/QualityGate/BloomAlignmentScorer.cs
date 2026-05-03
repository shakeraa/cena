// =============================================================================
// Cena Platform -- Bloom's Taxonomy Alignment Scorer (Stage 1)
// Heuristic-based validation of claimed Bloom's level — no LLM needed
// Research: LLM zero-shot gets 0.73 accuracy; these heuristics complement it
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// Validates Bloom's level alignment using keyword/pattern heuristics.
/// Maps to 3-tier system (Lower/Middle/Higher) for reliability.
/// </summary>
public static class BloomAlignmentScorer
{
    /// <summary>
    /// Score how well the question's content matches its claimed Bloom's level.
    /// Returns 0-100 and any violations.
    /// </summary>
    public static (int Score, IReadOnlyList<QualityViolation> Violations) Score(QualityGateInput input)
    {
        var violations = new List<QualityViolation>();

        if (input.ClaimedBloomLevel < 1 || input.ClaimedBloomLevel > 6)
        {
            violations.Add(new("BloomAlignment", "INVALID_LEVEL", $"Bloom level {input.ClaimedBloomLevel} out of range", ViolationSeverity.Error));
            return (0, violations);
        }

        // Classify the stem into a predicted Bloom tier
        var predicted = PredictBloomTier(input.Stem, input.Language);
        var claimed = GetTier(input.ClaimedBloomLevel);

        if (predicted == claimed)
        {
            // Perfect alignment
            return (100, violations);
        }

        if (Math.Abs((int)predicted - (int)claimed) == 1)
        {
            // Adjacent tier — acceptable but flag
            violations.Add(new("BloomAlignment", "ADJACENT_TIER",
                $"Claimed Bloom {input.ClaimedBloomLevel} ({claimed}) but stem patterns suggest {predicted}",
                ViolationSeverity.Info));
            return (70, violations);
        }

        // Large mismatch — e.g., claimed "Evaluate" (5) but stem is "Define X"
        violations.Add(new("BloomAlignment", "TIER_MISMATCH",
            $"Claimed Bloom {input.ClaimedBloomLevel} ({claimed}) but stem patterns strongly suggest {predicted}",
            ViolationSeverity.Warning));
        return (35, violations);
    }

    private enum BloomTier { Lower = 0, Middle = 1, Higher = 2 }

    private static BloomTier GetTier(int bloomLevel) => bloomLevel switch
    {
        1 or 2 => BloomTier.Lower,    // Remember, Understand
        3 or 4 => BloomTier.Middle,   // Apply, Analyze
        5 or 6 => BloomTier.Higher,   // Evaluate, Create
        _ => BloomTier.Lower
    };

    private static BloomTier PredictBloomTier(string stem, string language)
    {
        var stemLower = stem.ToLowerInvariant();

        // Higher-order indicators (Evaluate + Create)
        var higherPatterns = new[]
        {
            @"\b(evaluate|assess|judge|critique|justify|defend|argue|design|create|propose|construct|develop|formulate|synthesize)\b",
            @"\b(why is .+ better|which approach|what would happen if|design a|propose a|compare and contrast)\b",
            @"\b(הערך|שפוט|צור|הצע|תכנן|נמק|בנה)\b",  // Hebrew higher-order verbs
            @"\b(قيّم|احكم|صمم|اقترح|أنشئ|برر)\b",       // Arabic higher-order verbs
        };

        // Middle-order indicators (Apply + Analyze)
        var middlePatterns = new[]
        {
            @"\b(solve|calculate|compute|apply|find|determine|analyze|classify|compare|differentiate|distinguish|examine)\b",
            @"\b(how many|how much|what is the value|find the|calculate the|solve for|given .+ find)\b",
            @"\bsolve\b.*=",
            @"\b(פתור|חשב|מצא|קבע|נתח|השווה|סווג)\b",  // Hebrew middle-order verbs
            @"\b(حل|احسب|أوجد|حدد|حلل|قارن|صنف)\b",     // Arabic middle-order verbs
        };

        // Lower-order indicators (Remember + Understand)
        var lowerPatterns = new[]
        {
            @"\b(define|list|name|state|recall|identify|recognize|describe|explain|summarize|paraphrase|what is|which of the following)\b",
            @"\b(הגדר|רשום|ציין|זהה|תאר|הסבר|מהו|מהי|איזה)\b",  // Hebrew lower-order verbs
            @"\b(عرّف|اذكر|حدد|صف|اشرح|ما هو|ما هي|أي)\b",       // Arabic lower-order verbs
        };

        int higherScore = higherPatterns.Count(p => Regex.IsMatch(stemLower, p, RegexOptions.IgnoreCase));
        int middleScore = middlePatterns.Count(p => Regex.IsMatch(stemLower, p, RegexOptions.IgnoreCase));
        int lowerScore = lowerPatterns.Count(p => Regex.IsMatch(stemLower, p, RegexOptions.IgnoreCase));

        // Math/Science stems with equations or calculations are typically Middle
        bool hasEquation = Regex.IsMatch(stem, @"[=<>≤≥∫∑∏]|(?:\d+\s*[+\-*/]\s*\d+)");
        if (hasEquation) middleScore += 2;

        // "Which of the following" is almost always Lower
        if (Regex.IsMatch(stemLower, @"which of the following"))
            lowerScore += 2;

        // Default to Middle if no strong signals (most educational questions are Apply/Analyze)
        if (higherScore == 0 && middleScore == 0 && lowerScore == 0)
            return BloomTier.Middle;

        if (higherScore > middleScore && higherScore > lowerScore) return BloomTier.Higher;
        if (middleScore >= higherScore && middleScore >= lowerScore) return BloomTier.Middle;
        return BloomTier.Lower;
    }
}
