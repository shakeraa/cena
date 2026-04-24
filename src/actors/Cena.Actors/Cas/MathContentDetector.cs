// =============================================================================
// Cena Platform — Math Content Detector (RDY-034 / RDY-037, ADR-0002)
// Decides whether a question body (stem + options) contains math that must
// be verified by the CAS oracle before the question may be ingested.
//
// Rules:
//   - Subject in {math, mathematics, physics, chemistry} → always math.
//   - Language/history/other → detect LaTeX delimiters ($…$, \(…\), \[…\])
//     OR an equation-like heuristic (digit/operator/=/inequality/sqrt/sin/...).
//     Hit == "word problem with math content" → gate must verify.
//
// RDY-037: relocated from Cena.Admin.Api.Services → Cena.Actors.Cas so the
// detector lives alongside the CAS gate it feeds.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Cas;

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

    // RDY-048: Word-problem language hints. Only counts when coupled with
    // a digit-adjacent token elsewhere in the stem — the cue ALONE cannot
    // declare math, because "What is the capital of France?" contains
    // "what is the" but is not math.
    private static readonly Regex WordProblemVerb = new(
        @"\b(calculate|compute|evaluate|solve|simplify|factor|expand|differentiate|integrate)\b" +
        @"|اِحسُب|احسب|اوجد|أوجد|احسبوا" +
        @"|חשב|חשבו|מצא|מצאו|פתור|פתרו",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A math-adjacent indicator must accompany the verb for the cue to fire.
    private static readonly Regex DigitOrVariable = new(
        @"\d|\b[a-z]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        // RDY-048: word-problem verb ("solve", "calculate", Arabic/Hebrew
        // equivalents) only counts when paired with a digit or variable
        // token — the verb alone is too broad.
        bool hasWordProblemCue =
            WordProblemVerb.IsMatch(body) && DigitOrVariable.IsMatch(body);

        // Subject is authoritative. For non-math subjects, fall back to
        // heuristic detection (word-problem safety-net). For math subjects
        // with purely prose stems, word-problem cues keep the gate engaged
        // instead of silently dropping to Unverifiable.
        bool hasMath = subjectIsMath || hasLatex || hasEquation || hasWordProblemCue;

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
