// =============================================================================
// Cena Platform — Buggy Rule Matcher Base (RDY-033, ADR-0031)
//
// Shared helpers for CAS-grounded matchers: normalize expressions into a form
// the CAS router can parse, run an equivalence check against the buggy
// transform's output, and produce an ErrorPatternMatchResult with consistent
// confidence scoring.
//
// All CAS calls go through ICasRouterService (ADR-0002). Never call SymPy
// or MathNet directly from a matcher.
// =============================================================================

using System.Diagnostics;
using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;

/// <summary>
/// Base for CAS-backed buggy-rule matchers. Subclasses override
/// <see cref="ExtractBuggyOutput"/> to produce the expression a student would
/// write if they applied the buggy rule, then call <see cref="VerifyAgainstBuggyOutputAsync"/>.
/// </summary>
public abstract class BuggyRuleMatcherBase : IErrorPatternMatcher
{
    protected readonly ICasRouterService Cas;
    protected readonly ILogger Logger;

    protected BuggyRuleMatcherBase(ICasRouterService cas, ILogger logger)
    {
        Cas = cas;
        Logger = logger;
    }

    public abstract string BuggyRuleId { get; }

    public virtual string Subject => "math";

    public async Task<ErrorPatternMatchResult> TryMatchAsync(
        ErrorPatternMatchContext context,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!string.Equals(context.Subject, Subject, StringComparison.OrdinalIgnoreCase))
            return ErrorPatternMatchResult.NoMatch(BuggyRuleId, "subject mismatch", sw.Elapsed.TotalMilliseconds);

        if (string.IsNullOrWhiteSpace(context.StudentAnswer) || string.IsNullOrWhiteSpace(context.CorrectAnswer))
            return ErrorPatternMatchResult.NoMatch(BuggyRuleId, "empty input", sw.Elapsed.TotalMilliseconds);

        try
        {
            var buggyOutput = ExtractBuggyOutput(context);
            if (buggyOutput is null)
                return ErrorPatternMatchResult.NoMatch(BuggyRuleId, "stem does not fit rule shape", sw.Elapsed.TotalMilliseconds);

            ct.ThrowIfCancellationRequested();
            return await VerifyAgainstBuggyOutputAsync(context, buggyOutput, sw, ct);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("{RuleId}: matcher cancelled by budget after {Ms:F1}ms", BuggyRuleId, sw.Elapsed.TotalMilliseconds);
            return ErrorPatternMatchResult.NoMatch(BuggyRuleId, "budget exhausted", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "{RuleId}: matcher raised unexpected exception", BuggyRuleId);
            return ErrorPatternMatchResult.NoMatch(BuggyRuleId, $"matcher error: {ex.GetType().Name}", sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Produce the expression a student would write if they applied the buggy rule
    /// to the question's stem / correct answer. Return null if the stem shape does
    /// not match the rule (cheap early exit — no CAS spend).
    /// </summary>
    protected abstract string? ExtractBuggyOutput(ErrorPatternMatchContext context);

    /// <summary>
    /// Compare the student's answer to the buggy output via CAS.
    /// Default implementation tries Equivalence then NumericalTolerance.
    /// </summary>
    protected virtual async Task<ErrorPatternMatchResult> VerifyAgainstBuggyOutputAsync(
        ErrorPatternMatchContext context,
        string buggyOutput,
        Stopwatch sw,
        CancellationToken ct)
    {
        var studentExpr = NormalizeExpression(context.StudentAnswer);
        var buggyExpr = NormalizeExpression(buggyOutput);
        var correctExpr = NormalizeExpression(context.CorrectAnswer);

        // Guard: if the buggy output is the correct answer, we have nothing to say.
        // (Happens when the stem trivially reduces to the buggy shape's output.)
        var buggyVsCorrect = await Cas.VerifyAsync(
            new CasVerifyRequest(CasOperation.Equivalence, buggyExpr, correctExpr, Variable: null),
            ct);
        if (buggyVsCorrect.Status == CasVerifyStatus.Ok && buggyVsCorrect.Verified)
        {
            return ErrorPatternMatchResult.NoMatch(BuggyRuleId,
                "buggy transform coincides with correct answer — rule does not distinguish",
                sw.Elapsed.TotalMilliseconds, buggyVsCorrect.Engine);
        }

        var equivalence = await Cas.VerifyAsync(
            new CasVerifyRequest(CasOperation.Equivalence, studentExpr, buggyExpr, Variable: null),
            ct);

        if (equivalence.Status == CasVerifyStatus.Ok && equivalence.Verified)
        {
            return new ErrorPatternMatchResult(
                Matched: true,
                BuggyRuleId: BuggyRuleId,
                Confidence: 1.0,
                StudentTransformation: $"student answer ≡ {buggyOutput} (buggy transform of correct form)",
                Notes: null,
                ElapsedMs: sw.Elapsed.TotalMilliseconds,
                Engine: equivalence.Engine);
        }

        // Numerical fallback for fully-arithmetic answers (ORDER-OPS, CANCEL-COMMON with
        // concrete a, b). The CAS router routes numeric pairs through MathNet.
        if (LooksNumeric(studentExpr) && LooksNumeric(buggyExpr))
        {
            var numeric = await Cas.VerifyAsync(
                new CasVerifyRequest(CasOperation.NumericalTolerance, studentExpr, buggyExpr, Variable: null),
                ct);
            if (numeric.Status == CasVerifyStatus.Ok && numeric.Verified)
            {
                return new ErrorPatternMatchResult(
                    Matched: true,
                    BuggyRuleId: BuggyRuleId,
                    Confidence: 0.7,
                    StudentTransformation: $"student answer numerically matches buggy transform ({buggyOutput})",
                    Notes: "numerical-tolerance match (no symbolic equivalence)",
                    ElapsedMs: sw.Elapsed.TotalMilliseconds,
                    Engine: numeric.Engine);
            }
        }

        return ErrorPatternMatchResult.NoMatch(BuggyRuleId,
            $"student answer not equivalent to buggy output ({buggyOutput})",
            sw.Elapsed.TotalMilliseconds, equivalence.Engine);
    }

    /// <summary>
    /// Canonicalize a math string so the CAS parsers have the best chance of accepting it.
    /// Handles common notation drift: Unicode exponents, "×" vs "*", unicode minus,
    /// NBSP, LaTeX "\cdot", and collapse repeated whitespace.
    /// </summary>
    protected static string NormalizeExpression(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var s = raw.Trim();

        // Strip leading/trailing math delimiters
        if (s.StartsWith('$') && s.EndsWith('$') && s.Length >= 2) s = s[1..^1].Trim();

        // Common Unicode → ASCII
        s = s.Replace('×', '*')
             .Replace('·', '*')
             .Replace('÷', '/')
             .Replace('−', '-')  // U+2212 minus
             .Replace('–', '-')  // en-dash
             .Replace('—', '-')  // em-dash
             .Replace('\u00A0', ' '); // non-breaking space

        // LaTeX artifacts
        s = s.Replace("\\cdot", "*")
             .Replace("\\times", "*")
             .Replace("\\div", "/")
             .Replace("\\frac", "frac");

        // Unicode superscripts → ^N
        s = UnicodeSuperscripts.Replace(s, m =>
        {
            var digits = new string(m.Value.Select(UnicodeSupToDigit).ToArray());
            return $"^{digits}";
        });

        // Collapse whitespace
        s = WhitespaceRun.Replace(s, " ");

        return s;
    }

    private static readonly Regex UnicodeSuperscripts = new("[\u2070\u00b9\u00b2\u00b3\u2074-\u2079]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new("\\s+", RegexOptions.Compiled);

    private static char UnicodeSupToDigit(char c) => c switch
    {
        '\u2070' => '0',
        '\u00b9' => '1',
        '\u00b2' => '2',
        '\u00b3' => '3',
        '\u2074' => '4',
        '\u2075' => '5',
        '\u2076' => '6',
        '\u2077' => '7',
        '\u2078' => '8',
        '\u2079' => '9',
        _ => c
    };

    protected static bool LooksNumeric(string expr) =>
        !string.IsNullOrWhiteSpace(expr)
        && double.TryParse(expr.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
}
