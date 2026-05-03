// =============================================================================
// Cena Platform — Stem Solution Extractor (RDY-038, ADR-0002)
//
// Best-effort extraction of the expected answer / solution from a question
// stem so CasVerificationGate can run an Equivalence / Solve check against
// the author's marked-correct option — not just a parseability probe.
//
// What we support today:
//   1. Direct-expression stems: "Evaluate 2 + 3", "Simplify (x+1)^2" →
//      returns ExpressionOnly with the evaluable RHS.
//   2. Equation stems: "Solve 2x + 3 = 7", "Find x if x/2 = 4" → returns
//      Equation(lhs, rhs, variable).
//   3. Prose / word problems we cannot confidently parse → returns null
//      and the gate falls through to a NormalForm parseability check whose
//      binding is marked Unverifiable (NOT Verified) — admin queue must
//      review.
//
// We DO NOT try to parse arbitrary Arabic/Hebrew natural language here —
// that belongs to a future NLP-backed extractor. What we do catch is the
// overwhelming majority of Bagrut-style authored questions that state the
// equation or expression literally.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Cas;

/// <summary>
/// RDY-038: shape of what the extractor found in the stem.
/// </summary>
public abstract record StemExtraction
{
    /// <summary>
    /// Stem contains a direct expression to evaluate / simplify. The
    /// author's answer should be CAS-equivalent to this expression.
    /// </summary>
    public sealed record ExpressionOnly(string Expression, string? Variable) : StemExtraction;

    /// <summary>
    /// Stem contains an equation the student must solve. The gate uses
    /// SymPy's Solve to compute the expected root(s), then runs Equivalence
    /// between the author's answer and each root.
    /// </summary>
    public sealed record Equation(string Lhs, string Rhs, string? Variable) : StemExtraction;
}

/// <summary>
/// RDY-038: Contract for the stem extractor. Implementations return null
/// when the stem is not confidently parseable — the gate MUST treat that
/// as Unverifiable, not Verified.
/// </summary>
public interface IStemSolutionExtractor
{
    StemExtraction? Extract(string stem, string? subject);
}

/// <summary>
/// RDY-038: Regex-based extractor. Good enough for literal equation /
/// expression stems; silently declines ambiguous natural language.
/// </summary>
public sealed class StemSolutionExtractor : IStemSolutionExtractor
{
    // Strip LaTeX delimiters and common markup so the regex patterns can
    // see the raw math.
    private static readonly Regex DollarMathBlock = new(@"\$\$(?<body>.+?)\$\$",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DollarMath = new(@"\$(?<body>.+?)\$",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BracketMath = new(@"\\\[(?<body>.+?)\\\]",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ParenMath = new(@"\\\((?<body>.+?)\\\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Equation detector: a single '=' (not '==', '!=', '>=' etc.) with
    // math-like content either side. Captures <lhs>=<rhs>.
    private static readonly Regex EquationPattern = new(
        @"(?<lhs>[A-Za-z0-9_\+\-\*/\^\(\)\.\s]+?)\s*=\s*(?<rhs>[A-Za-z0-9_\+\-\*/\^\(\)\.\s]+)",
        RegexOptions.Compiled);

    // Command-verb prefixes to strip before running the equation regex.
    // Without this strip, "Solve 2x+3=7" extracts lhs="Solve 2x+3" because
    // the equation regex greedily consumes the leading verb.
    private static readonly Regex CommandPrefix = new(
        @"^\s*(?:solve|find|compute|evaluate|simplify|determine|calculate|factor|expand|show\s+that|prove)\s+(?:x\s+)?(?:if\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Direct-expression verbs that signal "the answer IS this expression's
    // canonical form" (Evaluate, Simplify, Compute, Factor).
    private static readonly Regex DirectExpressionVerb = new(
        @"(?i)\b(evaluate|simplify|compute|factor|expand)\b\s*[:\-]?\s*(?<expr>[^\.\?\!\n]+)",
        RegexOptions.Compiled);

    // Variable hints: "for x", "where x", "in x", "let x".
    private static readonly Regex VariableHint = new(
        @"(?i)\b(?:for|where|in|let|variable)\s+(?<var>[a-z])\b",
        RegexOptions.Compiled);

    public StemExtraction? Extract(string stem, string? subject)
    {
        if (string.IsNullOrWhiteSpace(stem)) return null;

        var math = ExtractMath(stem);
        var variable = GuessVariable(stem, math);

        // Strip leading command verbs ("Solve ", "Find x if ", …) so the
        // equation regex doesn't absorb them into the lhs.
        var mathNoVerb = CommandPrefix.Replace(math, string.Empty).Trim();

        // Equation takes priority — `2x+3=7` is more specific than `2x+3`.
        var eq = TryExtractEquation(mathNoVerb);
        if (eq is not null) return eq with { Variable = variable ?? eq.Variable };

        var expr = TryExtractDirectExpression(stem, mathNoVerb);
        if (expr is not null) return expr with { Variable = variable ?? expr.Variable };

        return null;
    }

    private static string ExtractMath(string stem)
    {
        // Prefer math inside $...$ / \[...\] / \(...\) when present; fall
        // back to the raw stem otherwise.
        var m = DollarMathBlock.Match(stem);
        if (m.Success) return m.Groups["body"].Value;
        m = DollarMath.Match(stem);
        if (m.Success) return m.Groups["body"].Value;
        m = BracketMath.Match(stem);
        if (m.Success) return m.Groups["body"].Value;
        m = ParenMath.Match(stem);
        if (m.Success) return m.Groups["body"].Value;
        return stem;
    }

    private static string? GuessVariable(string stem, string math)
    {
        var h = VariableHint.Match(stem);
        if (h.Success) return h.Groups["var"].Value;

        // Fall back to the first single-letter token that appears in the
        // math body — typically x / y / t.
        foreach (var c in new[] { 'x', 'y', 't', 'n', 'a', 'b' })
        {
            if (math.IndexOf(c) >= 0 && math.IndexOf(char.ToUpperInvariant(c)) < 0)
                return c.ToString();
        }
        return null;
    }

    private static StemExtraction.Equation? TryExtractEquation(string math)
    {
        // Reject obvious non-equation '=' contexts (comparisons).
        if (math.Contains("==") || math.Contains("!=") || math.Contains(">=") || math.Contains("<="))
            return null;

        // Count raw '=' — if there's exactly one, treat as equation.
        int eqCount = 0;
        foreach (var ch in math) if (ch == '=') eqCount++;
        if (eqCount != 1) return null;

        var m = EquationPattern.Match(math);
        if (!m.Success) return null;

        var lhs = m.Groups["lhs"].Value.Trim();
        var rhs = m.Groups["rhs"].Value.Trim();
        if (string.IsNullOrWhiteSpace(lhs) || string.IsNullOrWhiteSpace(rhs)) return null;

        // If neither side has a variable-looking token, it's not a
        // solve-for-x equation (e.g. "2 + 3 = 5" is just an assertion).
        if (!HasVariable(lhs) && !HasVariable(rhs)) return null;

        return new StemExtraction.Equation(lhs, rhs, null);
    }

    private static StemExtraction.ExpressionOnly? TryExtractDirectExpression(string stem, string math)
    {
        // Never fall through to an expression match if the stem still
        // contains an `=` — that means the equation extractor bailed
        // (ambiguous, no variable, etc.) and treating this as a direct
        // expression would silently ignore the `=` context.
        if (math.Contains('=')) return null;
        if (stem.Contains('=')) return null;

        var m = DirectExpressionVerb.Match(stem);
        if (m.Success)
        {
            var expr = m.Groups["expr"].Value.Trim().TrimEnd('.', '?', '!', ':', ';');
            if (!string.IsNullOrWhiteSpace(expr) && ContainsMathOperator(expr))
                return new StemExtraction.ExpressionOnly(expr, null);
        }

        // If the whole stem is basically just a bare expression (e.g. the
        // math extraction pulled out `(x+1)^2` and nothing else signals
        // otherwise), treat the math block itself as the expression.
        if (math.Length > 0 && math.Length == stem.Trim().Length && ContainsMathOperator(math))
            return new StemExtraction.ExpressionOnly(math.Trim(), null);

        return null;
    }

    private static bool HasVariable(string expr)
    {
        foreach (var c in expr)
            if (char.IsLetter(c)) return true;
        return false;
    }

    private static bool ContainsMathOperator(string expr)
    {
        foreach (var c in expr)
            if (c is '+' or '-' or '*' or '/' or '^' or '(' or ')') return true;
        return false;
    }
}
