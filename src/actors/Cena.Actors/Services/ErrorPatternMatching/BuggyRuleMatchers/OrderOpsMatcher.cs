// =============================================================================
// ORDER-OPS — evaluating left-to-right instead of PEMDAS: 2+3*4 → 20
// =============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;

/// <summary>
/// Detects the classic order-of-operations mal-rule: a student evaluates a
/// mixed +/×/- expression strictly left-to-right, ignoring precedence. The
/// matcher extracts the arithmetic expression from the stem, evaluates it
/// naively left-to-right, and checks whether the student's answer matches
/// that naive total (while the correct answer does not).
/// </summary>
public sealed class OrderOpsMatcher : BuggyRuleMatcherBase
{
    public OrderOpsMatcher(ICasRouterService cas, ILogger<OrderOpsMatcher> logger)
        : base(cas, logger) { }

    public override string BuggyRuleId => "ORDER-OPS";

    // A pure arithmetic expression: digits, decimal points, and + - * / operators.
    // Requires at least one + or - AND at least one * or / to be a non-trivial
    // order-of-operations question (else PEMDAS makes no difference).
    private static readonly Regex ArithmeticExpr = new(
        @"(?<expr>(?:\d+(?:\.\d+)?\s*[+\-*/]\s*){2,}\d+(?:\.\d+)?)",
        RegexOptions.Compiled);

    protected override string? ExtractBuggyOutput(ErrorPatternMatchContext context)
    {
        // Scan the stem first, then the correct answer, for a bare arithmetic expr.
        var candidates = new[]
        {
            NormalizeExpression(context.QuestionStem),
            NormalizeExpression(context.CorrectAnswer)
        };

        foreach (var src in candidates)
        {
            foreach (Match m in ArithmeticExpr.Matches(src))
            {
                var expr = m.Groups["expr"].Value;
                if (!HasMixedPrecedence(expr)) continue;

                var leftToRight = EvaluateLeftToRight(expr);
                if (leftToRight is null) continue;

                return leftToRight.Value.ToString("G", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool HasMixedPrecedence(string expr)
    {
        var hasAddSub = expr.Contains('+') || expr.Contains('-');
        var hasMulDiv = expr.Contains('*') || expr.Contains('/');
        return hasAddSub && hasMulDiv;
    }

    /// <summary>
    /// Evaluate an arithmetic expression naively left-to-right (ignoring precedence).
    /// Returns null on parse failure.
    /// </summary>
    private static double? EvaluateLeftToRight(string expr)
    {
        var tokens = Tokenize(expr);
        if (tokens.Count == 0 || tokens.Count % 2 == 0) return null;
        if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var acc)) return null;

        for (int i = 1; i < tokens.Count; i += 2)
        {
            var op = tokens[i];
            if (!double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rhs))
                return null;
            acc = op switch
            {
                "+" => acc + rhs,
                "-" => acc - rhs,
                "*" => acc * rhs,
                "/" => rhs == 0 ? double.NaN : acc / rhs,
                _ => double.NaN
            };
            if (double.IsNaN(acc)) return null;
        }
        return acc;
    }

    private static List<string> Tokenize(string expr)
    {
        var tokens = new List<string>();
        var buf = new System.Text.StringBuilder();
        foreach (var c in expr)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c is '+' or '-' or '*' or '/')
            {
                if (buf.Length > 0) { tokens.Add(buf.ToString()); buf.Clear(); }
                tokens.Add(c.ToString());
            }
            else
            {
                buf.Append(c);
            }
        }
        if (buf.Length > 0) tokens.Add(buf.ToString());
        return tokens;
    }
}
