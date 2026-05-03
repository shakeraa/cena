// =============================================================================
// Cena Platform — Terminology Lexicon loader (RDY-068b)
//
// Parses docs/content/arabic-math-lexicon.md into a typed, queryable
// dictionary so content-generation services + the CI ship-gate scanner
// can reason about a term's review status before it hits student UI.
//
// The Markdown document is the single source of truth — it has sections
// per topic family and a pipe-delimited table per section. Parsing it
// directly (vs a side-car JSON) keeps authors editing exactly one file
// and avoids "the JSON drifted from the doc" drift.
//
// Review lifecycle (from arabic-math-lexicon.md §Review lifecycle):
//   DRAFT              → engineering guess, dev-only
//   PROF_AMJAD_REVIEW  → under review, pilot-only
//   LOCKED             → signed off, production-safe
// =============================================================================

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Cena.Infrastructure.Localization;

/// <summary>Canonical review states. Source: arabic-math-lexicon.md.</summary>
public enum TerminologyStatus
{
    Draft = 0,
    ProfAmjadReview = 1,
    Locked = 2
}

/// <summary>
/// One lexicon row. <see cref="TopicFamily"/> is the section name
/// (e.g. "Algebra — foundations"). <see cref="Notes"/> is free text.
/// </summary>
public sealed record TerminologyTerm(
    string English,
    string Arabic,
    string Hebrew,
    TerminologyStatus Status,
    string TopicFamily,
    string? Notes = null);

/// <summary>
/// Abstraction so production code depends on the interface, not the
/// file-parser. Tests swap in an in-memory fake.
/// </summary>
public interface ITerminologyLexicon
{
    /// <summary>
    /// Lookup by English key (e.g. "polynomial"). Case-insensitive.
    /// Returns null if the key is not in the lexicon.
    /// </summary>
    TerminologyTerm? GetByEnglish(string englishKey);

    /// <summary>All terms. Enumerated in the order they appear in the source doc.</summary>
    IReadOnlyList<TerminologyTerm> AllTerms { get; }

    /// <summary>Filter by review status.</summary>
    IEnumerable<TerminologyTerm> WithStatus(TerminologyStatus status);

    /// <summary>
    /// True when every term is <see cref="TerminologyStatus.Locked"/>.
    /// The LOCKED_ONLY production mode refuses generation until this is true.
    /// </summary>
    bool IsFullyLocked { get; }
}

/// <summary>
/// Parses the Markdown at <c>docs/content/arabic-math-lexicon.md</c>.
/// Immutable after construction. Thread-safe.
/// </summary>
public sealed class TerminologyLexicon : ITerminologyLexicon
{
    private static readonly Regex SectionHeader =
        new(@"^###\s+\d+\.\s+(?<name>.+)$", RegexOptions.Compiled);

    // Table row: `| arabic | hebrew | english | status | notes |`
    // We accept leading / trailing whitespace inside cells.
    private static readonly Regex TableRow =
        new(@"^\|(?<cells>.+)\|\s*$", RegexOptions.Compiled);

    private static readonly Regex StatusToken =
        new(@"^\s*(DRAFT|PROF_AMJAD_REVIEW|LOCKED)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ImmutableArray<TerminologyTerm> _terms;
    private readonly ImmutableDictionary<string, TerminologyTerm> _byEnglish;

    private TerminologyLexicon(IEnumerable<TerminologyTerm> terms)
    {
        _terms = terms.ToImmutableArray();
        _byEnglish = _terms
            .GroupBy(t => t.English.Trim().ToLowerInvariant())
            .ToImmutableDictionary(g => g.Key, g => g.First());
    }

    public IReadOnlyList<TerminologyTerm> AllTerms => _terms;

    public TerminologyTerm? GetByEnglish(string englishKey)
    {
        if (string.IsNullOrWhiteSpace(englishKey)) return null;
        return _byEnglish.TryGetValue(englishKey.Trim().ToLowerInvariant(), out var t)
            ? t
            : null;
    }

    public IEnumerable<TerminologyTerm> WithStatus(TerminologyStatus status)
        => _terms.Where(t => t.Status == status);

    public bool IsFullyLocked => _terms.All(t => t.Status == TerminologyStatus.Locked);

    // ── Loading ──────────────────────────────────────────────────────────

    /// <summary>
    /// Load the canonical lexicon from the repo-relative Markdown path.
    /// Throws <see cref="FileNotFoundException"/> if the doc is missing
    /// (the loader treats a missing lexicon as a startup failure so a
    /// deploy cannot silently downgrade to no-lexicon enforcement).
    /// </summary>
    public static TerminologyLexicon LoadFromFile(string markdownPath)
    {
        if (!File.Exists(markdownPath))
            throw new FileNotFoundException(
                $"Terminology lexicon markdown not found at '{markdownPath}'. "
                + "This file is required at startup — see docs/content/arabic-math-lexicon.md.",
                markdownPath);

        var lines = File.ReadAllLines(markdownPath);
        return LoadFromLines(lines);
    }

    /// <summary>
    /// Parse from an in-memory line collection. Exposed for unit tests
    /// so the parser can be exercised without a temp file.
    /// </summary>
    public static TerminologyLexicon LoadFromLines(IEnumerable<string> lines)
    {
        var terms = new List<TerminologyTerm>();
        string currentSection = "(prologue)";
        bool inTable = false;
        int rowIndex = 0;

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;

            // Section header?
            var section = SectionHeader.Match(line);
            if (section.Success)
            {
                currentSection = section.Groups["name"].Value.Trim();
                inTable = false;
                rowIndex = 0;
                continue;
            }

            // Table row?
            var row = TableRow.Match(line);
            if (!row.Success)
            {
                inTable = false;
                continue;
            }

            // Skip the table header row and the `|---|---|` separator row.
            var cells = SplitCells(row.Groups["cells"].Value);
            if (cells.Count < 4)
                continue;

            // Heuristic: the first data row has the header text "Arabic" /
            // "Hebrew" / "English" somewhere. The separator row has only
            // dashes. The `Status` column on a real row must match one of
            // our three tokens.
            var statusCell = cells.Count >= 4 ? cells[3].Trim() : string.Empty;
            if (!StatusToken.IsMatch(statusCell))
            {
                // Header / separator / non-lexicon row — skip.
                continue;
            }

            inTable = true;
            rowIndex++;

            var arabic = cells[0].Trim();
            var hebrew = cells[1].Trim();
            var english = StripParenNotes(cells[2].Trim());
            var status = ParseStatus(statusCell);
            var notes = cells.Count >= 5
                ? string.IsNullOrWhiteSpace(cells[4]) ? null : cells[4].Trim()
                : null;

            // Skip rows that don't actually have content.
            if (string.IsNullOrWhiteSpace(english)
                || string.IsNullOrWhiteSpace(arabic)
                || string.IsNullOrWhiteSpace(hebrew))
                continue;

            terms.Add(new TerminologyTerm(
                English: english,
                Arabic: arabic,
                Hebrew: hebrew,
                Status: status,
                TopicFamily: currentSection,
                Notes: notes));
        }

        return new TerminologyLexicon(terms);
    }

    private static List<string> SplitCells(string raw)
    {
        // The captured group excludes the outermost pipes; split on inner
        // pipes. A lexicon cell never contains a literal pipe so this is
        // safe.
        return raw.Split('|').ToList();
    }

    /// <summary>
    /// The English column sometimes has parenthetical disambiguators like
    /// "term (in a polynomial)". We key on the stripped form to match a
    /// content-gen call's `GetByEnglish("term")`.
    /// </summary>
    private static string StripParenNotes(string english)
    {
        var idx = english.IndexOf('(');
        return idx < 0 ? english : english[..idx].Trim();
    }

    private static TerminologyStatus ParseStatus(string token)
    {
        return token.Trim().ToUpperInvariant() switch
        {
            "LOCKED" => TerminologyStatus.Locked,
            "PROF_AMJAD_REVIEW" => TerminologyStatus.ProfAmjadReview,
            _ => TerminologyStatus.Draft,
        };
    }
}
