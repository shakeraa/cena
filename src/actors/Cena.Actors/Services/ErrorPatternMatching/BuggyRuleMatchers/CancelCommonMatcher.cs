// =============================================================================
// CANCEL-COMMON — (a+b)/a → b  (cancelling summands like factors)
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;

/// <summary>
/// Detects the mal-rule where a student cancels an additive term as if it
/// were a multiplicative factor. Canonical: (x+5)/x → 5 instead of 1 + 5/x.
/// </summary>
public sealed class CancelCommonMatcher : BuggyRuleMatcherBase
{
    public CancelCommonMatcher(ICasRouterService cas, ILogger<CancelCommonMatcher> logger)
        : base(cas, logger) { }

    public override string BuggyRuleId => "CANCEL-COMMON";

    // (a +|- b ...) / denom  — denom must not itself contain a top-level + or -.
    private static readonly Regex FractionSum = new(
        @"\(\s*(?<num>[^()]+?)\s*\)\s*/\s*(?<den>[A-Za-z_][A-Za-z0-9_]*|\d+)",
        RegexOptions.Compiled);

    protected override string? ExtractBuggyOutput(ErrorPatternMatchContext context)
    {
        // Prefer the question stem; if it doesn't contain the shape, fall back
        // to the correct answer (sometimes the stem is a word problem and the
        // fraction only appears in the canonical answer).
        var candidates = new[]
        {
            NormalizeExpression(context.QuestionStem),
            NormalizeExpression(context.CorrectAnswer)
        };

        foreach (var src in candidates)
        {
            var m = FractionSum.Match(src);
            if (!m.Success) continue;

            var num = m.Groups["num"].Value.Trim();
            var den = m.Groups["den"].Value.Trim();

            // Need at least one summand in the numerator that equals the denominator.
            // Split on top-level + / - (no nested parens in the captured num).
            var parts = num
                .Replace("-", "+-")
                .Split('+', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            // Must match the shape a+b (at least two summands).
            if (parts.Count < 2) continue;

            // The buggy transform "cancels" the summand equal to the denominator,
            // leaving the remaining summand(s).
            var remaining = parts.Where(p => p != den).ToList();
            if (remaining.Count == parts.Count) continue; // denom didn't appear as summand
            if (remaining.Count == 0) return "0";

            // Reconstruct: "b" or "b + c" etc., with leading "-" preserved.
            var joined = string.Join(" + ", remaining).Replace("+ -", "- ");
            return joined;
        }

        return null;
    }
}
