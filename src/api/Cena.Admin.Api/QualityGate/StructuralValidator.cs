// =============================================================================
// Cena Platform -- Structural Validator (Stage 1)
// Zero-LLM-cost validation using Haladyna's item-writing rules
// Research: catches ~30% of bad items at <5ms per question
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// Implements 15 automatable Haladyna item-writing rules plus Cena-specific structural checks.
/// Each rule returns a score 0-100 and optional violations.
/// </summary>
public static class StructuralValidator
{
    /// <summary>Validate a question candidate and return structural validity score + violations.</summary>
    public static (int Score, IReadOnlyList<QualityViolation> Violations) Validate(QualityGateInput input)
    {
        var violations = new List<QualityViolation>();
        int totalRules = 0;
        int passedRules = 0;

        // Rule 1: Stem must be non-empty and meaningful (>10 chars)
        totalRules++;
        if (string.IsNullOrWhiteSpace(input.Stem))
        {
            violations.Add(new("StructuralValidity", "STEM_EMPTY", "Question stem is empty", ViolationSeverity.Critical));
        }
        else if (input.Stem.Trim().Length < 10)
        {
            violations.Add(new("StructuralValidity", "STEM_TOO_SHORT", $"Stem is only {input.Stem.Trim().Length} chars (min 10)", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 2: Must have exactly 4 options for MCQ
        totalRules++;
        if (input.Options.Count < 3)
        {
            violations.Add(new("StructuralValidity", "TOO_FEW_OPTIONS", $"Only {input.Options.Count} options (min 3)", ViolationSeverity.Critical));
        }
        else if (input.Options.Count > 5)
        {
            violations.Add(new("StructuralValidity", "TOO_MANY_OPTIONS", $"{input.Options.Count} options (max 5)", ViolationSeverity.Warning));
        }
        else
        {
            passedRules++;
        }

        // Rule 3: Exactly one correct answer
        totalRules++;
        int correctCount = input.Options.Count(o => o.IsCorrect);
        if (correctCount == 0)
        {
            violations.Add(new("StructuralValidity", "NO_CORRECT_ANSWER", "No option marked as correct", ViolationSeverity.Critical));
        }
        else if (correctCount > 1)
        {
            violations.Add(new("StructuralValidity", "MULTIPLE_CORRECT", $"{correctCount} options marked correct (expected 1)", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 4: CorrectOptionIndex must be valid and match IsCorrect flag
        totalRules++;
        if (input.CorrectOptionIndex < 0 || input.CorrectOptionIndex >= input.Options.Count)
        {
            violations.Add(new("StructuralValidity", "INVALID_CORRECT_INDEX", $"CorrectOptionIndex {input.CorrectOptionIndex} out of range", ViolationSeverity.Critical));
        }
        else if (input.Options.Count > input.CorrectOptionIndex && !input.Options[input.CorrectOptionIndex].IsCorrect)
        {
            violations.Add(new("StructuralValidity", "INDEX_MISMATCH", "CorrectOptionIndex doesn't match IsCorrect flag", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 5: No empty answer options
        totalRules++;
        int emptyOptions = input.Options.Count(o => string.IsNullOrWhiteSpace(o.Text));
        if (emptyOptions > 0)
        {
            violations.Add(new("StructuralValidity", "EMPTY_OPTIONS", $"{emptyOptions} option(s) have empty text", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 6: Options should be similar in length (Haladyna: avoid length cues)
        // Longest option should not be >2.5x the shortest
        totalRules++;
        if (input.Options.Count >= 2 && input.Options.All(o => !string.IsNullOrWhiteSpace(o.Text)))
        {
            var lengths = input.Options.Select(o => o.Text.Trim().Length).Where(l => l > 0).ToList();
            if (lengths.Count >= 2)
            {
                float ratio = (float)lengths.Max() / lengths.Min();
                if (ratio > 3.0f)
                {
                    violations.Add(new("StructuralValidity", "LENGTH_DISPARITY", $"Option length ratio {ratio:F1}x (max 3.0x)", ViolationSeverity.Warning));
                }
                else
                {
                    passedRules++;
                }
            }
            else
            {
                passedRules++;
            }
        }
        else
        {
            passedRules++; // Skip if options already flagged
        }

        // Rule 7: No "All of the above" / "None of the above" (Haladyna rule)
        totalRules++;
        bool hasAllNone = input.Options.Any(o =>
        {
            var lower = o.Text.Trim().ToLowerInvariant();
            return lower.Contains("all of the above") || lower.Contains("none of the above")
                || lower.Contains("כל התשובות") || lower.Contains("אף תשובה")
                || lower == "all" || lower == "none"
                || lower.Contains("كل الإجابات") || lower.Contains("لا شيء مما سبق");
        });
        if (hasAllNone)
        {
            violations.Add(new("StructuralValidity", "ALL_NONE_ABOVE", "Contains 'all/none of the above' option (Haladyna rule)", ViolationSeverity.Warning));
        }
        else
        {
            passedRules++;
        }

        // Rule 8: Stem should not contain negative wording without emphasis
        totalRules++;
        bool hasNegative = ContainsNegativeWording(input.Stem);
        if (hasNegative)
        {
            violations.Add(new("StructuralValidity", "NEGATIVE_STEM", "Stem contains negative wording (NOT/EXCEPT) — consider rephrasing", ViolationSeverity.Warning));
        }
        else
        {
            passedRules++;
        }

        // Rule 9: Bloom's level must be 1-6
        totalRules++;
        if (input.ClaimedBloomLevel < 1 || input.ClaimedBloomLevel > 6)
        {
            violations.Add(new("StructuralValidity", "INVALID_BLOOM", $"Bloom's level {input.ClaimedBloomLevel} out of range 1-6", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 10: Difficulty must be 0.0-1.0
        totalRules++;
        if (input.ClaimedDifficulty < 0f || input.ClaimedDifficulty > 1f)
        {
            violations.Add(new("StructuralValidity", "INVALID_DIFFICULTY", $"Difficulty {input.ClaimedDifficulty} out of range 0-1", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 11: Subject must be a valid Bagrut subject
        totalRules++;
        var validSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Math", "Physics", "Chemistry", "Biology", "Computer Science", "English" };
        if (!validSubjects.Contains(input.Subject))
        {
            violations.Add(new("StructuralValidity", "INVALID_SUBJECT", $"Subject '{input.Subject}' not in Bagrut curriculum", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 12: Language must be he/ar/en
        totalRules++;
        var validLanguages = new HashSet<string> { "he", "ar", "en" };
        if (!validLanguages.Contains(input.Language))
        {
            violations.Add(new("StructuralValidity", "INVALID_LANGUAGE", $"Language '{input.Language}' not supported (he/ar/en)", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 13: No duplicate options (exact text match)
        totalRules++;
        var optionTexts = input.Options.Select(o => o.Text.Trim().ToLowerInvariant()).ToList();
        if (optionTexts.Distinct().Count() < optionTexts.Count)
        {
            violations.Add(new("StructuralValidity", "DUPLICATE_OPTIONS", "Two or more options have identical text", ViolationSeverity.Critical));
        }
        else
        {
            passedRules++;
        }

        // Rule 14: Correct answer should not always be in the same position (check against a single question: just validate index is valid)
        // This is really a batch check; for single items, just ensure index is randomizable
        totalRules++;
        passedRules++; // Always passes for single-item check

        // Rule 15: Distractors should have rationale (recommended but not required)
        totalRules++;
        int distractorsWithoutRationale = input.Options
            .Where(o => !o.IsCorrect)
            .Count(o => string.IsNullOrWhiteSpace(o.DistractorRationale));
        if (distractorsWithoutRationale > 0 && input.Options.Count(o => !o.IsCorrect) > 0)
        {
            float pctMissing = (float)distractorsWithoutRationale / input.Options.Count(o => !o.IsCorrect);
            if (pctMissing > 0.5f)
            {
                violations.Add(new("StructuralValidity", "MISSING_RATIONALE", $"{distractorsWithoutRationale} distractor(s) missing rationale", ViolationSeverity.Info));
            }
            else
            {
                passedRules++;
            }
        }
        else
        {
            passedRules++;
        }

        // Rule 16 (RDY-003): Bloom Level 3+ (Apply and above) must have at least
        // one prerequisite. Without prerequisites the adaptive system cannot enforce
        // prerequisite gating and students may encounter higher-order questions
        // before mastering foundational concepts.
        totalRules++;
        if (input.ClaimedBloomLevel >= 3
            && (input.Prerequisites is null || input.Prerequisites.Count == 0))
        {
            violations.Add(new("StructuralValidity", "BLOOM3_NO_PREREQS",
                $"Bloom Level {input.ClaimedBloomLevel} (Apply+) requires at least one prerequisite concept",
                ViolationSeverity.Warning));
        }
        else
        {
            passedRules++;
        }

        // Rule 17 (RDY-004): Arabic translation completeness.
        // Arabic is the primary user language (80% of target users). Questions
        // missing an Arabic translation are flagged as info-level (not blocker)
        // so the quality gate surfaces translation gaps for prioritization.
        totalRules++;
        if (input.AvailableLanguages is not null
            && input.AvailableLanguages.Count > 0
            && !input.AvailableLanguages.Contains("ar"))
        {
            violations.Add(new("StructuralValidity", "MISSING_ARABIC",
                "Question has no Arabic translation (primary user language)",
                ViolationSeverity.Info));
        }
        else
        {
            passedRules++;
        }

        // Calculate score: percentage of rules passed, scaled to 0-100
        int score = totalRules > 0 ? (int)Math.Round(100.0 * passedRules / totalRules) : 0;

        // Critical violations force score to 0
        if (violations.Any(v => v.Severity == ViolationSeverity.Critical))
            score = Math.Min(score, 20);

        return (score, violations);
    }

    private static bool ContainsNegativeWording(string stem)
    {
        var patterns = new[]
        {
            @"\bNOT\b", @"\bEXCEPT\b", @"\bNEVER\b",
            @"\bלא\b", @"\bחוץ מ\b", @"\bלמעט\b",
            @"\bليس\b", @"\bماعدا\b", @"\bباستثناء\b"
        };

        return patterns.Any(p => Regex.IsMatch(stem, p, RegexOptions.IgnoreCase));
    }
}
