// =============================================================================
// Cena Platform -- Stem Clarity Scorer (Stage 1)
// Haladyna-based stem quality assessment — no LLM needed
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// Scores stem clarity based on automatable Haladyna rules:
/// - Presents a single clear problem
/// - Appropriate length
/// - No grammatical cues revealing the answer
/// - Vocabulary appropriate for grade level
/// </summary>
public static class StemClarityScorer
{
    public static (int Score, IReadOnlyList<QualityViolation> Violations) Score(QualityGateInput input)
    {
        var violations = new List<QualityViolation>();
        float totalWeight = 0;
        float weightedScore = 0;

        // Check 1: Stem length — should be meaningful but not excessively long (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            int len = input.Stem.Trim().Length;
            if (len < 15)
            {
                violations.Add(new("StemClarity", "STEM_VERY_SHORT", $"Stem is {len} chars — likely insufficient", ViolationSeverity.Warning));
                weightedScore += weight * 20;
            }
            else if (len < 30)
            {
                weightedScore += weight * 60;
            }
            else if (len > 500)
            {
                violations.Add(new("StemClarity", "STEM_VERY_LONG", $"Stem is {len} chars — may overwhelm students", ViolationSeverity.Info));
                weightedScore += weight * 70;
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        // Check 2: Stem ends with question mark or instruction verb (weight: 1)
        {
            const float weight = 1f;
            totalWeight += weight;
            var trimmed = input.Stem.Trim();
            bool endsWithQuestion = trimmed.EndsWith('?') || trimmed.EndsWith('؟');
            bool hasInstructionVerb = HasInstructionVerb(trimmed, input.Language);
            bool hasEquationToSolve = Regex.IsMatch(trimmed, @"[=<>≤≥]");

            if (endsWithQuestion || hasInstructionVerb || hasEquationToSolve)
            {
                weightedScore += weight * 100;
            }
            else
            {
                violations.Add(new("StemClarity", "NO_QUESTION_FORM", "Stem doesn't form a clear question or instruction", ViolationSeverity.Warning));
                weightedScore += weight * 40;
            }
        }

        // Check 3: No grammatical cues that leak the answer (weight: 2)
        // e.g., stem ending with "a" or "an" only matching one option
        {
            const float weight = 2f;
            totalWeight += weight;
            bool hasGrammaticalCue = CheckGrammaticalCues(input);
            if (hasGrammaticalCue)
            {
                violations.Add(new("StemClarity", "GRAMMATICAL_CUE", "Stem contains grammatical cue that may reveal the answer", ViolationSeverity.Warning));
                weightedScore += weight * 30;
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        // Check 4: No absolute terms that make the question trivial (weight: 1)
        {
            const float weight = 1f;
            totalWeight += weight;
            bool hasAbsolute = Regex.IsMatch(input.Stem, @"\b(always|never|every|only|all|completely)\b", RegexOptions.IgnoreCase);
            if (hasAbsolute)
            {
                violations.Add(new("StemClarity", "ABSOLUTE_TERM", "Stem uses absolute language (always/never/all)", ViolationSeverity.Info));
                weightedScore += weight * 60;
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        // Check 5: Stem should not repeat verbatim in any option (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            var stemWords = ExtractSignificantWords(input.Stem);
            bool optionRepeats = false;
            foreach (var opt in input.Options)
            {
                var optWords = ExtractSignificantWords(opt.Text);
                float overlap = stemWords.Count > 0
                    ? (float)stemWords.Intersect(optWords).Count() / stemWords.Count
                    : 0;
                if (overlap > 0.8f)
                {
                    optionRepeats = true;
                    break;
                }
            }
            if (optionRepeats)
            {
                violations.Add(new("StemClarity", "OPTION_REPEATS_STEM", "An option repeats >80% of the stem text", ViolationSeverity.Warning));
                weightedScore += weight * 40;
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        // Check 6: Correct answer should not be obviously longer than distractors (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            var correctOpt = input.Options.FirstOrDefault(o => o.IsCorrect);
            var distractors = input.Options.Where(o => !o.IsCorrect).ToList();
            if (correctOpt != null && distractors.Count > 0)
            {
                int correctLen = correctOpt.Text.Trim().Length;
                float avgDistractorLen = (float)distractors.Average(d => d.Text.Trim().Length);
                if (avgDistractorLen > 0 && correctLen > avgDistractorLen * 2.0f)
                {
                    violations.Add(new("StemClarity", "CORRECT_LONGER", $"Correct answer ({correctLen} chars) is >2x longer than avg distractor ({avgDistractorLen:F0} chars)", ViolationSeverity.Warning));
                    weightedScore += weight * 40;
                }
                else
                {
                    weightedScore += weight * 100;
                }
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        int score = totalWeight > 0 ? (int)Math.Round(weightedScore / totalWeight) : 0;
        return (score, violations);
    }

    private static bool HasInstructionVerb(string stem, string language)
    {
        var enVerbs = new[] { "solve", "find", "calculate", "determine", "identify", "explain",
            "describe", "compare", "analyze", "evaluate", "write", "draw", "prove", "show",
            "select", "choose", "complete", "fill", "match", "classify", "predict", "trace" };
        var heVerbs = new[] { "פתור", "מצא", "חשב", "קבע", "זהה", "הסבר", "תאר", "השווה" };
        var arVerbs = new[] { "حل", "أوجد", "احسب", "حدد", "عرف", "اشرح", "صف", "قارن" };

        var verbs = language switch
        {
            "he" => heVerbs.Concat(enVerbs),
            "ar" => arVerbs.Concat(enVerbs),
            _ => enVerbs
        };

        var stemLower = stem.ToLowerInvariant();
        return verbs.Any(v => stemLower.Contains(v.ToLowerInvariant()));
    }

    private static bool CheckGrammaticalCues(QualityGateInput input)
    {
        // English: stem ending with "a" or "an" matches only one option's starting letter
        if (input.Language == "en")
        {
            var stemTrimmed = input.Stem.Trim().ToLowerInvariant();
            if (stemTrimmed.EndsWith(" a") || stemTrimmed.EndsWith(" an"))
            {
                bool isAn = stemTrimmed.EndsWith(" an");
                var vowelStarts = input.Options.Count(o =>
                {
                    char first = o.Text.Trim().ToLowerInvariant().FirstOrDefault();
                    return "aeiou".Contains(first);
                });
                // If article matches only 1 option, it's a cue
                if (isAn && vowelStarts == 1) return true;
                if (!isAn && (input.Options.Count - vowelStarts) == 1) return true;
            }
        }

        return false;
    }

    private static HashSet<string> ExtractSignificantWords(string text)
    {
        var stopWords = new HashSet<string> { "the", "a", "an", "is", "are", "was", "were",
            "of", "in", "to", "for", "and", "or", "not", "with", "at", "by", "from", "on" };

        return Regex.Matches(text.ToLowerInvariant(), @"\b\w{3,}\b")
            .Select(m => m.Value)
            .Where(w => !stopWords.Contains(w))
            .ToHashSet();
    }
}
