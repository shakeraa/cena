// =============================================================================
// Cena Platform — Parametric Variant Deduper (prr-200)
//
// Rejects near-duplicate variants by hashing a canonical tuple of:
//   * Normalised rendered stem (whitespace-collapsed, whitespace-trimmed)
//   * Canonicalised answer (already CAS-normalised by the renderer)
//   * Sorted set of distractor canonical forms
//
// The hash is a SHA-256 of the JSON-encoded tuple. Cryptographic strength
// is overkill pedagogically but it's the stdlib default and collision-free
// for the range of variant counts we care about (10k per template per rung).
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.QuestionBank.Templates;

public sealed class ParametricVariantDeduper
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>Number of distinct canonical forms observed.</summary>
    public int UniqueCount => _seen.Count;

    /// <summary>
    /// Try to admit a variant; returns true if it is new, false if a prior
    /// call already admitted an equivalent canonical form.
    /// </summary>
    public bool TryAdmit(ParametricVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var hash = ComputeCanonicalHash(variant);
        return _seen.Add(hash);
    }

    /// <summary>
    /// Canonical hash of a variant. Stable across runs and processes.
    /// </summary>
    public static string ComputeCanonicalHash(ParametricVariant v)
    {
        var normStem = NormaliseStem(v.RenderedStem);
        var distractorForms = v.Distractors
            .Select(d => d.Text.Trim())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        sb.Append("stem=").Append(normStem).Append('\u001f');
        sb.Append("answer=").Append(v.CanonicalAnswer.Trim()).Append('\u001f');
        sb.Append("distractors=");
        foreach (var d in distractorForms)
        {
            sb.Append(d).Append('|');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string NormaliseStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return "";
        // Collapse whitespace runs to a single space; keep case (Hebrew /
        // Arabic stems are case-insensitive but English ones matter for
        // "Solve" vs "solve" distinction when that IS pedagogically distinct).
        var collapsed = System.Text.RegularExpressions.Regex.Replace(stem, @"\s+", " ");
        return collapsed.Trim();
    }
}
