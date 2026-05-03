// =============================================================================
// Cena Platform -- Distractor Quality Scorer (Stage 1)
// Pre-deployment heuristic evaluation of distractor quality
// Post-deployment: replaced by actual student response data (rpb, selection rates)
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// Scores distractor quality based on structural heuristics (no LLM).
/// Checks: plausibility cues, rationale presence, diversity, format consistency.
/// </summary>
public static class DistractorQualityScorer
{
    public static (int Score, IReadOnlyList<QualityViolation> Violations) Score(QualityGateInput input)
    {
        var violations = new List<QualityViolation>();
        var distractors = input.Options.Where(o => !o.IsCorrect).ToList();

        if (distractors.Count == 0)
        {
            violations.Add(new("DistractorQuality", "NO_DISTRACTORS", "No distractor options found", ViolationSeverity.Critical));
            return (0, violations);
        }

        float totalWeight = 0;
        float weightedScore = 0;

        // Check 1: All distractors have text (weight: 3)
        {
            const float weight = 3f;
            totalWeight += weight;
            int empty = distractors.Count(d => string.IsNullOrWhiteSpace(d.Text));
            if (empty > 0)
            {
                violations.Add(new("DistractorQuality", "EMPTY_DISTRACTOR", $"{empty} distractor(s) have empty text", ViolationSeverity.Critical));
                weightedScore += weight * 0;
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        // Check 2: Distractors have rationale (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            int withRationale = distractors.Count(d => !string.IsNullOrWhiteSpace(d.DistractorRationale));
            float pct = (float)withRationale / distractors.Count;
            if (pct >= 1.0f)
            {
                weightedScore += weight * 100;
            }
            else if (pct >= 0.5f)
            {
                violations.Add(new("DistractorQuality", "PARTIAL_RATIONALE", $"Only {withRationale}/{distractors.Count} distractors have rationale", ViolationSeverity.Info));
                weightedScore += weight * 60;
            }
            else
            {
                violations.Add(new("DistractorQuality", "NO_RATIONALE", "Most distractors lack rationale", ViolationSeverity.Warning));
                weightedScore += weight * 30;
            }
        }

        // Check 3: Distractors are distinct from each other (weight: 3)
        {
            const float weight = 3f;
            totalWeight += weight;
            var texts = distractors.Select(d => d.Text.Trim().ToLowerInvariant()).ToList();
            bool hasDuplicates = texts.Distinct().Count() < texts.Count;
            if (hasDuplicates)
            {
                violations.Add(new("DistractorQuality", "DUPLICATE_DISTRACTORS", "Two or more distractors have identical text", ViolationSeverity.Critical));
                weightedScore += weight * 0;
            }
            else
            {
                // Check for near-duplicates (very similar text)
                bool hasNearDuplicate = false;
                for (int i = 0; i < texts.Count; i++)
                {
                    for (int j = i + 1; j < texts.Count; j++)
                    {
                        if (ComputeJaccardSimilarity(texts[i], texts[j]) > 0.8f)
                        {
                            hasNearDuplicate = true;
                            break;
                        }
                    }
                    if (hasNearDuplicate) break;
                }
                if (hasNearDuplicate)
                {
                    violations.Add(new("DistractorQuality", "NEAR_DUPLICATE_DISTRACTORS", "Two distractors are very similar", ViolationSeverity.Warning));
                    weightedScore += weight * 50;
                }
                else
                {
                    weightedScore += weight * 100;
                }
            }
        }

        // Check 4: Distractor length consistency with correct answer (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            var correct = input.Options.FirstOrDefault(o => o.IsCorrect);
            if (correct != null)
            {
                int correctLen = correct.Text.Trim().Length;
                var distractorLens = distractors.Select(d => d.Text.Trim().Length).ToList();
                float avgDistLen = (float)distractorLens.Average();

                // Correct answer should not be significantly longer/shorter than distractors
                if (correctLen > 0 && avgDistLen > 0)
                {
                    // Haladyna concern: correct answer being LONGER than distractors reveals it
                    // Distractors being longer than correct is less problematic
                    float ratio = (float)correctLen / avgDistLen;
                    if (ratio > 3.0f)
                    {
                        violations.Add(new("DistractorQuality", "LENGTH_MISMATCH", $"Correct answer is {ratio:F1}x longer than avg distractor", ViolationSeverity.Warning));
                        weightedScore += weight * 30;
                    }
                    else if (ratio > 2.0f)
                    {
                        weightedScore += weight * 60;
                    }
                    else if (ratio < 0.2f)
                    {
                        // Correct answer suspiciously short compared to distractors
                        violations.Add(new("DistractorQuality", "CORRECT_TOO_SHORT", $"Correct answer is only {ratio:F2}x of avg distractor length", ViolationSeverity.Info));
                        weightedScore += weight * 70;
                    }
                    else
                    {
                        weightedScore += weight * 100;
                    }
                }
                else
                {
                    weightedScore += weight * 50;
                }
            }
            else
            {
                weightedScore += weight * 50;
            }
        }

        // Check 5: Distractors should not be obviously absurd (very short single-char, etc.) (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            int absurd = distractors.Count(d =>
            {
                var text = d.Text.Trim();
                // Single character that's not a math expression (a, b, c are labels, not answers)
                if (text.Length == 1 && !char.IsDigit(text[0])) return false; // allow single-letter labels
                // "???" or "---" or similar placeholder text
                if (Regex.IsMatch(text, @"^[?!.\-_]{2,}$")) return true;
                // Exactly matches "true" or "false" when other options are complex
                return false;
            });

            if (absurd > 0)
            {
                violations.Add(new("DistractorQuality", "ABSURD_DISTRACTOR", $"{absurd} distractor(s) appear to be placeholders", ViolationSeverity.Error));
                weightedScore += weight * 20;
            }
            else
            {
                weightedScore += weight * 100;
            }
        }

        // Check 6: For math/science subjects, distractors should contain numbers/expressions if the correct answer does (weight: 2)
        {
            const float weight = 2f;
            totalWeight += weight;
            var mathSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Math", "Physics", "Chemistry" };
            if (mathSubjects.Contains(input.Subject))
            {
                var correct = input.Options.FirstOrDefault(o => o.IsCorrect);
                bool correctHasNumbers = correct != null && Regex.IsMatch(correct.Text, @"\d");
                if (correctHasNumbers)
                {
                    int distractorsWithNumbers = distractors.Count(d => Regex.IsMatch(d.Text, @"\d"));
                    float pct = (float)distractorsWithNumbers / distractors.Count;
                    if (pct < 0.5f)
                    {
                        violations.Add(new("DistractorQuality", "MISSING_NUMERIC_DISTRACTORS", "Correct answer has numbers but most distractors don't", ViolationSeverity.Warning));
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
            else
            {
                weightedScore += weight * 100;
            }
        }

        int score = totalWeight > 0 ? (int)Math.Round(weightedScore / totalWeight) : 0;
        return (score, violations);
    }

    private static float ComputeJaccardSimilarity(string a, string b)
    {
        var setA = Regex.Matches(a, @"\b\w+\b").Select(m => m.Value).ToHashSet();
        var setB = Regex.Matches(b, @"\b\w+\b").Select(m => m.Value).ToHashSet();
        if (setA.Count == 0 && setB.Count == 0) return 1f;
        int intersection = setA.Intersect(setB).Count();
        int union = setA.Union(setB).Count();
        return union > 0 ? (float)intersection / union : 0f;
    }
}
