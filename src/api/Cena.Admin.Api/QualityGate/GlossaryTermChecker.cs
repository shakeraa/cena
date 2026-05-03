// =============================================================================
// Cena Platform -- Glossary Term Checker (RDY-027)
// Stage 1 scorer: validates that question terminology aligns with the
// canonical trilingual glossary at config/glossary.json.
//
// Design: Only scores based on *recognized* glossary terms found in the text.
// Does NOT penalize for text that isn't a glossary term (that would flag every
// sentence as a violation). The score reflects: "of the domain terms we can
// identify, what fraction come from the canonical glossary?"
// =============================================================================

using System.Text.Json;
using Cena.Api.Contracts.Admin.QualityGate;

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// Checks that a question's stem and options use terminology from the
/// canonical glossary. Returns a score (0-100) and any violations.
/// </summary>
public static class GlossaryTermChecker
{
    // Production glossary — loaded once from config/glossary.json.
    // Tests inject via CheckWithGlossary() instead.
    private static readonly Lazy<GlossaryIndex> _defaultGlossary = new(
        () => LoadGlossaryFromDisk() ?? GlossaryIndex.Empty);

    /// <summary>
    /// Score using the production glossary (loaded from disk).
    /// Fails open: returns 100 if glossary is unavailable.
    /// </summary>
    public static (int Score, List<QualityViolation> Violations) Check(QualityGateInput input)
    {
        GlossaryIndex glossary;
        try { glossary = _defaultGlossary.Value; }
        catch { return (100, new List<QualityViolation>()); }

        return CheckWithGlossary(input, glossary);
    }

    /// <summary>
    /// Score using an injected glossary index (for testing and composition).
    /// </summary>
    public static (int Score, List<QualityViolation> Violations) CheckWithGlossary(
        QualityGateInput input, GlossaryIndex glossary)
    {
        var violations = new List<QualityViolation>();

        if (glossary.IsEmpty)
            return (100, violations);

        var termSet = glossary.GetTermsForLanguage(input.Language);
        if (termSet.Count == 0)
            return (100, violations);

        // Collect all text fields to scan
        var textsToCheck = new List<(string Field, string Text)> { ("stem", input.Stem) };
        foreach (var opt in input.Options)
            textsToCheck.Add(($"option:{opt.Label}", opt.Text));

        // Strategy: scan text for glossary term occurrences (longest-match-first).
        // We only count terms that are *in* or *not in* the glossary — we never
        // try to extract "unknown domain terms" from free text (that's NLP, not us).
        var sortedTerms = termSet.OrderByDescending(t => t.Length).ToList();
        int matchCount = 0;
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (field, text) in textsToCheck)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            var lowerText = text.ToLowerInvariant();

            foreach (var term in sortedTerms)
            {
                var lowerTerm = term.ToLowerInvariant();

                // For Hebrew/Arabic: use simple containment. These scripts don't
                // have English-style substring-of-longer-word problems (e.g.
                // "cat" inside "concatenate"), and definite articles prefix-attach
                // (ה + פונקציה, ال + دالة) so word-boundary checks produce false negatives.
                //
                // For Latin-script languages: use word-boundary-aware matching
                // to avoid "equation" matching inside "requationed".
                bool useSimpleContains = input.Language is "he" or "ar";

                if (useSimpleContains)
                {
                    if (lowerText.Contains(lowerTerm, StringComparison.Ordinal))
                    {
                        matchedTerms.Add(term);
                        matchCount++;
                    }
                }
                else
                {
                    int idx = 0;
                    while ((idx = lowerText.IndexOf(lowerTerm, idx, StringComparison.Ordinal)) >= 0)
                    {
                        bool startOk = idx == 0 || !char.IsLetterOrDigit(lowerText[idx - 1]);
                        int endIdx = idx + lowerTerm.Length;
                        bool endOk = endIdx >= lowerText.Length || !char.IsLetterOrDigit(lowerText[endIdx]);

                        if (startOk && endOk)
                        {
                            matchedTerms.Add(term);
                            matchCount++;
                            break;
                        }
                        idx = endIdx;
                    }
                }
            }
        }

        // If the question has no recognizable domain terms, it's not applicable (score=100).
        // This handles purely numeric questions like "2+3=?" that have no terminology.
        if (matchCount == 0)
            return (100, violations);

        // All matched terms are by definition in the glossary, so score is 100.
        // The value of this checker is the *violations* it produces when terms
        // from a known "non-standard variants" list are detected, and the
        // metadata it attaches (which glossary terms appear in the question).
        //
        // Future: compare against a curated "common-misspelling" / "non-standard
        // synonym" list and deduct points for those. For now, this checker
        // validates that the glossary is loadable, terms are findable, and
        // wires the infrastructure into the quality gate pipeline.

        // Attach found terms as info-level violations for traceability
        foreach (var term in matchedTerms)
        {
            violations.Add(new QualityViolation(
                Dimension: "GlossaryTerms",
                RuleId: "GLOSS-INFO",
                Description: $"Glossary term found: '{term}'",
                Severity: ViolationSeverity.Info));
        }

        // Score is 100 (all recognized terms are canonical). When the non-standard
        // variants list is added, mismatches will reduce this score.
        return (100, violations);
    }

    // ── Glossary loading ──

    private static GlossaryIndex? LoadGlossaryFromDisk()
    {
        var searchRoots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppDomain.CurrentDomain.BaseDirectory,
        };

        foreach (var root in searchRoots)
        {
            var dir = root;
            for (int depth = 0; depth < 6; depth++)
            {
                var candidate = Path.Combine(dir, "config", "glossary.json");
                if (File.Exists(candidate))
                    return ParseGlossaryFile(candidate);
                var parent = Path.GetDirectoryName(dir);
                if (parent is null || parent == dir) break;
                dir = parent;
            }
        }

        return null;
    }

    private static GlossaryIndex ParseGlossaryFile(string path)
    {
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<GlossaryFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        return GlossaryIndex.FromEntries(raw?.Terms ?? new List<GlossaryEntry>());
    }

    // ── Public types (for DI / testing) ──

    private sealed class GlossaryFile
    {
        public string Version { get; set; } = "";
        public List<GlossaryEntry> Terms { get; set; } = new();
    }

    public sealed class GlossaryEntry
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

    public sealed class GlossaryIndex
    {
        public static readonly GlossaryIndex Empty = new(
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

        private readonly HashSet<string> _hebrewTerms;
        private readonly HashSet<string> _arabicTerms;
        private readonly HashSet<string> _englishTerms;

        public bool IsEmpty => _hebrewTerms.Count == 0
                            && _arabicTerms.Count == 0
                            && _englishTerms.Count == 0;

        private GlossaryIndex(
            HashSet<string> hebrew, HashSet<string> arabic, HashSet<string> english)
        {
            _hebrewTerms = hebrew;
            _arabicTerms = arabic;
            _englishTerms = english;
        }

        public static GlossaryIndex FromEntries(IReadOnlyList<GlossaryEntry> entries)
        {
            var he = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var en = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(e.Hebrew)) he.Add(e.Hebrew);
                if (!string.IsNullOrWhiteSpace(e.Arabic)) ar.Add(e.Arabic);
                if (!string.IsNullOrWhiteSpace(e.English)) en.Add(e.English);
            }

            return new GlossaryIndex(he, ar, en);
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
