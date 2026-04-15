// =============================================================================
// DIST-EXP-SUM — (a+b)^n → a^n + b^n  (exponent distributed over addition)
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;

/// <summary>
/// Detects the classic algebra mal-rule: distributing an exponent across
/// addition. Canonical example: a student writes (x+3)² = x² + 9 instead of
/// x² + 6x + 9.
/// </summary>
public sealed class DistExpSumMatcher : BuggyRuleMatcherBase
{
    public DistExpSumMatcher(ICasRouterService cas, ILogger<DistExpSumMatcher> logger)
        : base(cas, logger) { }

    public override string BuggyRuleId => "DIST-EXP-SUM";

    // (expr1 +|- expr2 [+|- expr3 ...]) ^ n   with n ≥ 2
    // Captures the inner sum and the exponent. Balanced-paren handling is
    // intentionally shallow — we require a single outer paren pair.
    private static readonly Regex BinomialPower = new(
        @"\(\s*(?<inner>[^()]+?)\s*\)\s*\^\s*(?<exp>\d+)",
        RegexOptions.Compiled);

    protected override string? ExtractBuggyOutput(ErrorPatternMatchContext context)
    {
        var stem = NormalizeExpression(context.QuestionStem);
        var m = BinomialPower.Match(stem);
        if (!m.Success) return null;

        var inner = m.Groups["inner"].Value.Trim();
        if (!int.TryParse(m.Groups["exp"].Value, out var exponent) || exponent < 2) return null;

        // Split the inner expression on top-level + or - (no nested parens expected here
        // because BinomialPower already rejects them inside the captured group).
        var terms = SplitTopLevelAdditive(inner);
        if (terms.Count < 2) return null;

        // Buggy transform: raise each term to the exponent independently.
        var parts = terms.Select(t =>
        {
            var (sign, token) = t;
            var raised = $"({token})^{exponent}";
            return sign == '+' ? raised : $"-{raised}";
        });

        // Strip a leading '+' if the first term was positive.
        var joined = string.Join(" + ", parts).Replace("+ -", "- ");
        if (joined.StartsWith("+ ")) joined = joined[2..];
        return joined;
    }

    /// <summary>Split "a + b - c" → [(+,"a"), (+,"b"), (-,"c")]. No nested parens.</summary>
    private static List<(char Sign, string Token)> SplitTopLevelAdditive(string inner)
    {
        var result = new List<(char, string)>();
        var currentSign = '+';
        var buf = new System.Text.StringBuilder();

        // Allow a leading sign.
        int i = 0;
        if (i < inner.Length && (inner[i] == '+' || inner[i] == '-'))
        {
            currentSign = inner[i];
            i++;
        }

        for (; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '+' || c == '-')
            {
                // Distinguish binary +/- from a sign attached to an exponent literal.
                // Previous char matters; we treat '+'/'-' as separators when not
                // immediately after '*', '/', '^', '('.
                var prev = buf.Length > 0 ? buf[^1] : ' ';
                if (prev is '*' or '/' or '^' or '(')
                {
                    buf.Append(c);
                    continue;
                }

                if (buf.Length > 0)
                {
                    result.Add((currentSign, buf.ToString().Trim()));
                    buf.Clear();
                }
                currentSign = c;
            }
            else
            {
                buf.Append(c);
            }
        }

        if (buf.Length > 0)
            result.Add((currentSign, buf.ToString().Trim()));

        return result;
    }
}
