// =============================================================================
// Cena Platform -- Glossary Term Checker (RDY-027)
// Stage 1 scorer: validates that question terminology aligns with the
// canonical trilingual glossary at config/glossary.json.
// =============================================================================

using System.Text.Json;
using System.Text.RegularExpressions;
using Cena.Api.Contracts.Admin.QualityGate;

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// Checks that a question's stem and options use terminology from the
/// canonical glossary. Returns a score (0-100) and any violations.
/// Non-glossary terms are flagged as warnings, not hard failures.
/// </summary>
public static class GlossaryTermChecker
{
    private static readonly Lazy<GlossaryData> _glossary = new(LoadGlossary);

    /// <summary>
    /// Score the question based on glossary term coverage.
    /// Returns 100 if the glossary cannot be loaded (fail-open).
    /// </summary>
    public static (int Score, List<QualityViolation> Violations) Check(QualityGateInput input)
    {
        var violations = new List<QualityViolation>();

        GlossaryData glossary;
        try
        {
            glossary = _glossary.Value;
        }
        catch
        {
            // Fail-open: if the glossary file is missing or malformed, skip the check
            return (100, violations);
        }

        if (glossary.Terms.Count == 0)
            return (100, violations);

        // Build the term set for the question's language
        var terms = glossary.GetTermsForLanguage(input.Language);
        if (terms.Count == 0)
            return (100, violations); // No terms for this language

        // Collect all text to check
        var textsToCheck = new List<(string Field, string Text)>
        {
            ("stem", input.Stem),
        };
        foreach (var opt in input.Options)
        {
            textsToCheck.Add(($"option:{opt.Label}", opt.Text));
        }

        int totalTermsFound = 0;
        int termsInGlossary = 0;

        foreach (var (field, text) in textsToCheck)
        {
            // Extract domain-specific terms from the text
            var extracted = ExtractDomainTerms(text, input.Language);

            foreach (var term in extracted)
            {
                totalTermsFound++;
                if (terms.Contains(term.ToLowerInvariant()))
                {
                    termsInGlossary++;
                }
                else
                {
                    violations.Add(new QualityViolation(
                        Dimension: "GlossaryTerms",
                        RuleId: "GLOSS-001",
                        Description: $"Term '{term}' in {field} not found in canonical glossary ({input.Language})",
                        Severity: ViolationSeverity.Warning));
                }
            }
        }

        // Score: if no domain terms detected, return 100 (not applicable)
        if (totalTermsFound == 0)
            return (100, violations);

        // Percentage of terms matching the glossary
        int score = (int)Math.Round(100.0 * termsInGlossary / totalTermsFound);
        return (Math.Clamp(score, 0, 100), violations);
    }

    /// <summary>
    /// Extracts domain-specific terms from text based on language.
    /// Uses simple heuristics — not full NLP — to identify multi-word
    /// mathematical and scientific terms.
    /// </summary>
    private static List<string> ExtractDomainTerms(string text, string language)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var glossary = _glossary.Value;
        var allTerms = glossary.GetTermsForLanguage(language);

        // Check for each known glossary term in the text (case-insensitive)
        var lowerText = text.ToLowerInvariant();
        foreach (var term in allTerms)
        {
            if (lowerText.Contains(term))
            {
                result.Add(term);
            }
        }

        // For Hebrew/Arabic, also extract standalone multi-word patterns
        // that look like domain terms but aren't in the glossary
        if (language == "he")
        {
            // Hebrew math terms: sequences of Hebrew chars with spaces
            foreach (Match m in Regex.Matches(text, @"[\u0590-\u05FF][\u0590-\u05FF\s\-]{2,}[\u0590-\u05FF]"))
            {
                var candidate = m.Value.Trim().ToLowerInvariant();
                if (!allTerms.Contains(candidate) && candidate.Length > 3)
                {
                    result.Add(candidate);
                }
            }
        }
        else if (language == "ar")
        {
            foreach (Match m in Regex.Matches(text, @"[\u0600-\u06FF][\u0600-\u06FF\s\-]{2,}[\u0600-\u06FF]"))
            {
                var candidate = m.Value.Trim().ToLowerInvariant();
                if (!allTerms.Contains(candidate) && candidate.Length > 3)
                {
                    result.Add(candidate);
                }
            }
        }

        return result.Distinct().ToList();
    }

    // ── Glossary loading ──

    private static GlossaryData LoadGlossary()
    {
        // Walk up from the assembly directory to find config/glossary.json
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        string? glossaryPath = null;

        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "config", "glossary.json");
            if (File.Exists(candidate))
            {
                glossaryPath = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        if (glossaryPath is null)
        {
            // Try relative to working directory as fallback
            var cwd = Path.Combine(Directory.GetCurrentDirectory(), "config", "glossary.json");
            if (File.Exists(cwd))
                glossaryPath = cwd;
        }

        if (glossaryPath is null)
            return new GlossaryData(new List<GlossaryEntry>());

        var json = File.ReadAllText(glossaryPath);
        var raw = JsonSerializer.Deserialize<GlossaryFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return new GlossaryData(raw?.Terms ?? new List<GlossaryEntry>());
    }

    // ── Internal types ──

    private sealed class GlossaryFile
    {
        public string Version { get; set; } = "";
        public List<GlossaryEntry> Terms { get; set; } = new();
    }

    private sealed class GlossaryEntry
    {
        public string Id { get; set; } = "";
        public string English { get; set; } = "";
        public string Hebrew { get; set; } = "";
        public string Arabic { get; set; } = "";
        public string Domain { get; set; } = "";
        public string? ArabicGender { get; set; }
        public string? Notes { get; set; }
        public string? Source { get; set; }
    }

    private sealed class GlossaryData
    {
        public IReadOnlyList<GlossaryEntry> Terms { get; }
        private readonly HashSet<string> _hebrewTerms;
        private readonly HashSet<string> _arabicTerms;
        private readonly HashSet<string> _englishTerms;

        public GlossaryData(IReadOnlyList<GlossaryEntry> terms)
        {
            Terms = terms;
            _hebrewTerms = new HashSet<string>(
                terms.Select(t => t.Hebrew.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            _arabicTerms = new HashSet<string>(
                terms.Select(t => t.Arabic.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            _englishTerms = new HashSet<string>(
                terms.Select(t => t.English.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<string> GetTermsForLanguage(string lang) => lang switch
        {
            "he" => _hebrewTerms,
            "ar" => _arabicTerms,
            "en" => _englishTerms,
            _ => new HashSet<string>(),
        };
    }
}
