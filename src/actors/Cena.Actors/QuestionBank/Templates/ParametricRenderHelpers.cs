// =============================================================================
// Cena Platform — Parametric Render Helpers (prr-200)
//
// Pure static substitution + screening utilities shared by the production
// SymPyParametricRenderer and the CLI's offline renderer. Lives in the domain
// project so the CLI can consume it without re-referencing the Admin API.
// No LLM import — this file is in-scope for NoLlmInParametricPipelineTest.
// =============================================================================

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.QuestionBank.Templates;

public static class ParametricRenderHelpers
{
    private static readonly Regex AllowedTokenRegex = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Substitute slot tokens in the stem template. Braces render as plain
    /// text; the stem may contain {{ }} for literal braces.
    /// </summary>
    public static string SubstituteStem(
        string template,
        IReadOnlyDictionary<string, ParametricSlotValue> slots)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(slots);

        var sb = new StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i++; continue; }
                var end = template.IndexOf('}', i + 1);
                if (end < 0) throw new FormatException($"Unterminated '{{' at offset {i}");
                var name = template.Substring(i + 1, end - i - 1);
                if (!AllowedTokenRegex.IsMatch(name))
                    throw new FormatException($"Invalid slot token '{name}' at offset {i}");
                if (!slots.TryGetValue(name, out var value))
                    throw new FormatException($"Stem references undefined slot '{name}'");
                sb.Append(value.IsIntegral()
                    ? (value.ToIntegerOrNull() ?? 0).ToString(CultureInfo.InvariantCulture)
                    : value.ToExpressionString());
                i = end;
                continue;
            }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            { sb.Append('}'); i++; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Substitute slot names in a SymPy expression. Word-boundary aware so a
    /// slot named 'a' does not get substituted inside 'abs'. Input is
    /// validated to contain only [A-Za-z0-9_+\-*/^().,\s] — any character
    /// outside that set is rejected before substitution (prr-200 safety-allowlist).
    /// </summary>
    public static string SubstituteSlots(
        string expr,
        IReadOnlyDictionary<string, ParametricSlotValue> slots)
    {
        ArgumentNullException.ThrowIfNull(expr);
        ArgumentNullException.ThrowIfNull(slots);

        foreach (var ch in expr)
        {
            if (!(char.IsLetterOrDigit(ch)
                  || ch == '_' || ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '^'
                  || ch == '(' || ch == ')' || ch == '.' || ch == ',' || ch == ' ' || ch == '\t'))
            {
                throw new FormatException(
                    $"Disallowed character '{ch}' in expression '{expr}' (prr-200 safety-allowlist).");
            }
        }

        var orderedNames = slots.Keys.OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal);
        var result = expr;
        foreach (var name in orderedNames)
        {
            var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])";
            result = Regex.Replace(result, pattern, slots[name].ToExpressionString(), RegexOptions.CultureInvariant);
        }
        return result;
    }

    /// <summary>
    /// Cheap pre-CAS screen. After substitution we scan for literal "/0" or
    /// "/ 0" not inside an identifier. Does not catch computed denominators
    /// like <c>1/(a-a)</c> — CAS handles those.
    /// </summary>
    public static bool ContainsLiteralDivideByZero(string expr)
    {
        if (string.IsNullOrEmpty(expr)) return false;
        var s = Regex.Replace(expr, @"\s+", "");
        return Regex.IsMatch(s, @"/0(?![.\d])");
    }

    public enum AnswerShape { Integer, Rational, Decimal, Symbolic, NonFinite }

    /// <summary>Shape classification for a CAS-canonicalised answer string.</summary>
    public static AnswerShape ClassifyAnswerShape(string canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return AnswerShape.Symbolic;
        var trimmed = canonical.Trim();
        if (trimmed.Contains("nan", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("inf", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("zoo", StringComparison.OrdinalIgnoreCase))
            return AnswerShape.NonFinite;

        if (Regex.IsMatch(trimmed, @"^[+-]?\d+$")) return AnswerShape.Integer;

        if (Regex.IsMatch(trimmed, @"^[+-]?\d+/[+-]?\d+$"))
        {
            var parts = trimmed.Split('/');
            if (long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
                && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
                && q != 0)
            {
                return p % q == 0 ? AnswerShape.Integer : AnswerShape.Rational;
            }
            return AnswerShape.Rational;
        }

        // Parenthesised rational: (p/q)
        var wrapped = Regex.Match(trimmed, @"^\(\s*([+-]?\d+)\s*/\s*([+-]?\d+)\s*\)$");
        if (wrapped.Success
            && long.TryParse(wrapped.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wp)
            && long.TryParse(wrapped.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wq)
            && wq != 0)
        {
            return wp % wq == 0 ? AnswerShape.Integer : AnswerShape.Rational;
        }

        if (Regex.IsMatch(trimmed, @"^[+-]?\d+\.\d+$")) return AnswerShape.Decimal;

        return AnswerShape.Symbolic;
    }

    public static bool IsShapeAccepted(AnswerShape shape, AcceptShape flags) => shape switch
    {
        AnswerShape.Integer    => (flags & AcceptShape.Integer) != 0,
        AnswerShape.Rational   => (flags & AcceptShape.Rational) != 0,
        AnswerShape.Decimal    => (flags & AcceptShape.Decimal) != 0,
        AnswerShape.Symbolic   => (flags & AcceptShape.Symbolic) != 0,
        AnswerShape.NonFinite  => false,
        _                      => false
    };
}
