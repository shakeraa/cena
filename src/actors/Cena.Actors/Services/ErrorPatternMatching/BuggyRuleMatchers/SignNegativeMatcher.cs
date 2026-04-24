// =============================================================================
// SIGN-NEGATIVE — -(a+b) → -a + b   (failing to distribute leading minus)
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;

/// <summary>
/// Detects the mal-rule where a leading negative is applied only to the first
/// summand: -(x+5) written as -x+5 instead of -x-5.
/// </summary>
public sealed class SignNegativeMatcher : BuggyRuleMatcherBase
{
    public SignNegativeMatcher(ICasRouterService cas, ILogger<SignNegativeMatcher> logger)
        : base(cas, logger) { }

    public override string BuggyRuleId => "SIGN-NEGATIVE";

    // -(inner) where inner has at least one top-level + or -.
    private static readonly Regex LeadingNeg = new(
        @"-\s*\(\s*(?<inner>[^()]+?)\s*\)",
        RegexOptions.Compiled);

    protected override string? ExtractBuggyOutput(ErrorPatternMatchContext context)
    {
        var candidates = new[]
        {
            NormalizeExpression(context.QuestionStem),
            NormalizeExpression(context.CorrectAnswer)
        };

        foreach (var src in candidates)
        {
            var m = LeadingNeg.Match(src);
            if (!m.Success) continue;

            var inner = m.Groups["inner"].Value.Trim();

            // Need at least one summand with top-level + or -.
            // Reuse the same split style as DIST-EXP-SUM.
            var terms = SplitAdditive(inner);
            if (terms.Count < 2) continue;

            // Buggy transform: negate only the first term, leave the rest.
            var first = terms[0];
            var head = first.Sign == '+' ? $"-{first.Token}" : $"+{first.Token}";
            var tail = string.Join(string.Empty, terms.Skip(1).Select(t =>
                t.Sign == '+' ? $" + {t.Token}" : $" - {t.Token}"));

            return (head + tail).TrimStart('+').Trim();
        }

        return null;
    }

    private static List<(char Sign, string Token)> SplitAdditive(string inner)
    {
        var result = new List<(char, string)>();
        var sign = '+';
        var buf = new System.Text.StringBuilder();
        int i = 0;
        if (i < inner.Length && (inner[i] == '+' || inner[i] == '-'))
        {
            sign = inner[i];
            i++;
        }

        for (; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '+' || c == '-')
            {
                var prev = buf.Length > 0 ? buf[^1] : ' ';
                if (prev is '*' or '/' or '^' or '(')
                {
                    buf.Append(c);
                    continue;
                }
                if (buf.Length > 0)
                {
                    result.Add((sign, buf.ToString().Trim()));
                    buf.Clear();
                }
                sign = c;
            }
            else
            {
                buf.Append(c);
            }
        }
        if (buf.Length > 0) result.Add((sign, buf.ToString().Trim()));
        return result;
    }
}
