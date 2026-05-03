// =============================================================================
// FRACTION-ADD — a/b + c/d → (a+c)/(b+d)  (adding fractions by adding in parallel)
// =============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;

/// <summary>
/// Detects the mal-rule where a student adds fractions by summing numerators
/// and denominators separately: 1/2 + 1/3 → 2/5 instead of 5/6.
/// </summary>
public sealed class FractionAddMatcher : BuggyRuleMatcherBase
{
    public FractionAddMatcher(ICasRouterService cas, ILogger<FractionAddMatcher> logger)
        : base(cas, logger) { }

    public override string BuggyRuleId => "FRACTION-ADD";

    // <num1>/<den1>  +  <num2>/<den2> — num and den are single tokens
    // (digit runs, single letters, or parenthesized expressions without inner parens).
    private static readonly Regex FractionSum = new(
        @"(?<n1>\d+|\([^()]+\)|[A-Za-z_][A-Za-z0-9_]*)\s*/\s*(?<d1>\d+|\([^()]+\)|[A-Za-z_][A-Za-z0-9_]*)\s*\+\s*(?<n2>\d+|\([^()]+\)|[A-Za-z_][A-Za-z0-9_]*)\s*/\s*(?<d2>\d+|\([^()]+\)|[A-Za-z_][A-Za-z0-9_]*)",
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
            var m = FractionSum.Match(src);
            if (!m.Success) continue;

            var n1 = m.Groups["n1"].Value;
            var d1 = m.Groups["d1"].Value;
            var n2 = m.Groups["n2"].Value;
            var d2 = m.Groups["d2"].Value;

            // Buggy: (n1+n2)/(d1+d2). Materialize numerically if all tokens parse as numbers,
            // else leave symbolic and let CAS equivalence do the work.
            if (TryParseAll(out var vn1, out var vd1, out var vn2, out var vd2, n1, d1, n2, d2))
            {
                var numSum = vn1 + vn2;
                var denSum = vd1 + vd2;
                if (denSum == 0) return null;
                // Report as a reduced-friendly symbolic fraction — keep as division so
                // MathNet/SymPy can simplify and compare against the student's answer.
                return $"({numSum.ToString(CultureInfo.InvariantCulture)}) / ({denSum.ToString(CultureInfo.InvariantCulture)})";
            }

            return $"({n1} + {n2}) / ({d1} + {d2})";
        }

        return null;
    }

    private static bool TryParseAll(out double a, out double b, out double c, out double d,
        params string[] tokens)
    {
        a = b = c = d = 0;
        if (tokens.Length != 4) return false;
        var ok = double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
              && double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b)
              && double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out c)
              && double.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        return ok;
    }
}
