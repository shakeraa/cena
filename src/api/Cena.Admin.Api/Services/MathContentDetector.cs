// =============================================================================
// Cena Platform — Math Content Detector (RDY-034, ADR-0002)
// Decides whether a question body (stem + options) contains math that must
// be verified by the CAS oracle before the question may be ingested.
//
// Rules:
//   - Subject in {math, mathematics, physics, chemistry} → always math.
//   - Language/history/other → detect LaTeX delimiters ($…$, \(…\), \[…\])
//     OR an equation-like heuristic (digit/operator/=/inequality/sqrt/sin/...).
//     Hit == "word problem with math content" → gate must verify.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Admin.Api.Services;

/// <summary>
/// RDY-034: Result of analyzing a question body for math content.
/// </summary>
public sealed record MathContentDetectionResult(
    bool HasMathContent,
    IReadOnlyList<string> ExtractedExpressions);

/// <summary>
/// RDY-034 / ADR-0002: Boundary detector used by the CAS ingestion gate
/// to decide whether a question stem carries math that MUST be CAS-verified.
/// </summary>
public interface IMathContentDetector
{
    MathContentDetectionResult Analyze(string body, string subject);

    /// <summary>
    /// Convenience probe: true if the subject+body pair has any math content.
    /// </summary>
    bool HasMathContent(string body, string subject) => Analyze(body, subject).HasMathContent;
}

/// <summary>
/// RDY-034: Default implementation. Uses a set of regexes plus subject hints.
/// </summary>
public sealed class MathContentDetector : IMathContentDetector
{
    private static readonly HashSet<string> MathSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "math", "mathematics", "maths", "physics", "chemistry"
    };

    // Non-greedy $…$ but not $$…$$
    private static readonly Regex DollarInline =
        new(@"(?<!\$)\$(?!\$)(.+?)(?<!\$)\$(?!\$)", RegexOptions.Compiled);
    private static readonly Regex LatexParen =
        new(@"\\\((.+?)\\\)", RegexOptions.Compiled);
    private static readonly Regex LatexBracket =
        new(@"\\\[(.+?)\\\]", RegexOptions.Compiled);

    // Equation-like heuristic: digit/var followed by an operator/equality glyph
    private static readonly Regex EquationLike =
        new(@"\d\s*[+\-*/=<>≤≥]\s*\d|=\s*\d|\d\s*=|\\frac|\\sqrt|\bsin\s*\(|\bcos\s*\(|\btan\s*\(|\blog\s*\(|\bln\s*\(|\bint\s*\(|\bintegrate\s*\(|\bsqrt\s*\(|\^\s*\d|x\^\d|\by\s*=",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public MathContentDetectionResult Analyze(string body, string subject)
    {
        body ??= string.Empty;
        subject ??= string.Empty;

        var expressions = new List<string>();
        var subjectIsMath = MathSubjects.Contains(subject.Trim());

        AddMatches(DollarInline.Matches(body), expressions);
        AddMatches(LatexParen.Matches(body), expressions);
        AddMatches(LatexBracket.Matches(body), expressions);

        bool hasLatex = expressions.Count > 0;
        bool hasEquation = EquationLike.IsMatch(body);

        // Subject is authoritative. For non-math subjects, fall back to
        // heuristic detection (word-problem safety-net).
        bool hasMath = subjectIsMath || hasLatex || hasEquation;

        return new MathContentDetectionResult(hasMath, expressions);
    }

    private static void AddMatches(MatchCollection matches, List<string> into)
    {
        foreach (Match m in matches)
        {
            if (m.Groups.Count > 1) into.Add(m.Groups[1].Value);
            else into.Add(m.Value);
        }
    }
}
