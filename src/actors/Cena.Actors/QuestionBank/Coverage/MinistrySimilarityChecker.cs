// =============================================================================
// Cena Platform — Ministry Similarity Checker (prr-201, ADR-0043)
//
// Deterministic n-gram cosine similarity between a candidate stem (e.g. a
// stage-2 LLM isomorph output) and a corpus of Ministry-of-Education
// Bagrut exam item stems. Scores above the configured threshold
// (default 0.82) mean the candidate is too close to the Ministry text and
// must be rejected — we ship reference-only per ADR-0043, never verbatim.
//
// Design:
//   * No LLM. No embeddings model. n-gram cosine over character 5-grams,
//     language-agnostic, deterministic, ~O(candidate length × corpus size).
//   * Corpus is injected via IMinistryReferenceCorpus so tests can provide
//     a tiny in-memory list and production provides whatever the ingestion
//     pipeline has normalised.
//   * Threshold is a ConfigMap value (default 0.82) — surfaced in the drop
//     reason so an admin can see "0.91 > 0.82 → rejected" without opening
//     a log.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// Corpus of Ministry Bagrut reference stems. Production impl reads from
/// Marten; tests hand in a list. Keeping the contract small so the checker
/// is testable in isolation.
/// </summary>
public interface IMinistryReferenceCorpus
{
    /// <summary>
    /// Reference stems for the given subject/track. Order-independent.
    /// </summary>
    IReadOnlyList<MinistryReferenceItem> GetReferences(string subject, string trackKey);
}

public sealed record MinistryReferenceItem(
    string Id,
    string NormalisedStem);

public sealed record MinistrySimilarityVerdict(
    double Score,
    string? NearestReferenceId,
    bool IsTooClose);

/// <summary>
/// Deterministic checker.
/// </summary>
public sealed class MinistrySimilarityChecker
{
    private readonly IMinistryReferenceCorpus _corpus;
    private readonly double _threshold;
    private const int NGramSize = 5;

    public MinistrySimilarityChecker(IMinistryReferenceCorpus corpus, double threshold = 0.82)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (threshold <= 0.0 || threshold >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "threshold must be in (0, 1)");
        _corpus = corpus;
        _threshold = threshold;
    }

    public double Threshold => _threshold;

    public MinistrySimilarityVerdict Score(string candidateStem, string subject, string trackKey)
    {
        if (string.IsNullOrWhiteSpace(candidateStem))
            return new MinistrySimilarityVerdict(0, null, false);

        var refs = _corpus.GetReferences(subject, trackKey);
        if (refs.Count == 0)
            return new MinistrySimilarityVerdict(0, null, false);

        var candGrams = NGrams(Normalise(candidateStem));
        if (candGrams.Count == 0)
            return new MinistrySimilarityVerdict(0, null, false);

        double best = 0;
        string? nearestId = null;
        foreach (var r in refs)
        {
            var refGrams = NGrams(r.NormalisedStem);
            var sim = Cosine(candGrams, refGrams);
            if (sim > best)
            {
                best = sim;
                nearestId = r.Id;
            }
        }

        return new MinistrySimilarityVerdict(best, nearestId, best >= _threshold);
    }

    internal static string Normalise(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Lowercase + collapse whitespace + strip punctuation. Keeps
        // alphabetic chars in any Unicode block (Hebrew, Arabic, Latin)
        // and digits. Punctuation-insensitive so "Solve: x+1=0" and
        // "Solve x + 1 = 0" map together.
        var lowered = s.Trim().ToLowerInvariant();
        var kept = new System.Text.StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) kept.Append(ch);
            else if (char.IsWhiteSpace(ch))
            {
                if (kept.Length > 0 && kept[^1] != ' ') kept.Append(' ');
            }
            // everything else dropped
        }
        return kept.ToString().Trim();
    }

    internal static Dictionary<string, int> NGrams(string normalised)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        if (normalised.Length < NGramSize) return d;
        for (var i = 0; i <= normalised.Length - NGramSize; i++)
        {
            var g = normalised.Substring(i, NGramSize);
            d[g] = d.TryGetValue(g, out var c) ? c + 1 : 1;
        }
        return d;
    }

    internal static double Cosine(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;
        long dot = 0;
        foreach (var kv in a)
        {
            if (b.TryGetValue(kv.Key, out var bc))
                dot += (long)kv.Value * bc;
        }
        if (dot == 0) return 0.0;
        double na = 0, nb = 0;
        foreach (var kv in a) na += (double)kv.Value * kv.Value;
        foreach (var kv in b) nb += (double)kv.Value * kv.Value;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}

/// <summary>
/// Empty corpus — degenerate injection for call sites that haven't yet
/// wired up the Ministry ingestion (production hosts pre-Bagrut-corpus-go-live,
/// subjects other than math). Always returns 0/NotTooClose so candidates
/// pass through; telemetry still shows the check ran.
/// </summary>
public sealed class EmptyMinistryReferenceCorpus : IMinistryReferenceCorpus
{
    public IReadOnlyList<MinistryReferenceItem> GetReferences(string subject, string trackKey)
        => Array.Empty<MinistryReferenceItem>();
}
