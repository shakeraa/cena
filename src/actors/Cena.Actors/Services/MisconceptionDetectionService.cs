// =============================================================================
// Cena Platform — Misconception Detection Service (RDY-014 + RDY-033)
//
// Connects wrong answers → error patterns → MisconceptionCatalog buggy rules.
// When a match is found, returns a MisconceptionDetectionResult including the
// appropriate RemediationTask from RemediationTemplates.
//
// Pattern matching approach (RDY-033, ADR-0031):
//   1. Delegate to IErrorPatternMatcherEngine — CAS-grounded matchers that
//      check symbolic equivalence via ICasRouterService (ADR-0002).
//   2. Fallback: legacy inline string heuristics for buggy rules not yet
//      covered by a first-class IErrorPatternMatcher.
//
// All misconception data is session-scoped per ADR-0003.
// Events carry [MlExcluded] per RDY-006.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Services.ErrorPatternMatching;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Result of attempting to detect a misconception from a wrong answer.
/// </summary>
public sealed record MisconceptionDetectionResult(
    bool Detected,
    string? BuggyRuleId,
    double Confidence,
    string? CounterExample,
    RemediationTask? RemediationTask);

/// <summary>
/// RDY-014: Detects misconceptions from wrong answers by matching against
/// the MisconceptionCatalog's 15 empirically-documented buggy rules.
/// </summary>
public interface IMisconceptionDetectionService
{
    /// <summary>
    /// Attempt to detect a misconception from a wrong answer.
    /// Returns detection result with optional remediation task.
    /// </summary>
    MisconceptionDetectionResult Detect(
        string questionStem,
        string correctAnswer,
        string studentAnswer,
        string subject,
        string? conceptId,
        ExplanationErrorType? errorType);

    /// <summary>
    /// Get the remediation task for a detected misconception.
    /// </summary>
    RemediationTask? GetRemediation(string buggyRuleId);
}

public sealed class MisconceptionDetectionService : IMisconceptionDetectionService
{
    private readonly ILogger<MisconceptionDetectionService> _logger;
    private readonly IErrorPatternMatcherEngine? _matcherEngine;

    // Pattern matchers: each maps a buggy rule ID to a detection function.
    // The function receives (questionStem, correctAnswer, studentAnswer, subject)
    // and returns a confidence score (0.0-1.0). Score >= 0.5 triggers detection.
    private static readonly Dictionary<string, Func<string, string, string, double>> Matchers = new()
    {
        // ── Algebra ──

        ["DIST-EXP-SUM"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student squares terms separately instead of expanding
            // e.g., correct="x²+6x+9" but student="x²+9"
            // The correct answer has a middle cross-term (like 6x) that the student omitted
            bool correctHasCrossTerm = correct.Contains("x") && (correct.Contains("+") || correct.Contains("-"));
            bool studentMissingCrossTerm = !student.Contains("x+") && !student.Contains("x-")
                && student.Length < correct.Length - 2;
            if (correctHasCrossTerm && studentMissingCrossTerm && student.Length >= 3)
                return 0.7;
            return 0.0;
        },

        ["SIGN-FLIP-INEQ"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: answer has wrong inequality direction
            if ((correct.Contains(">") && student.Contains("<")) ||
                (correct.Contains("<") && student.Contains(">")))
                return 0.8;
            return 0.0;
        },

        ["CANCEL-FRACTION-ADD"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student cancelled additive terms in a fraction
            // Simplified answer when it shouldn't be (e.g., "5" instead of "3.5")
            if (double.TryParse(student.Trim(), out var sVal) &&
                double.TryParse(correct.Trim(), out var cVal) &&
                Math.Abs(sVal - Math.Round(sVal)) < 0.01 &&
                Math.Abs(cVal - Math.Round(cVal)) > 0.01)
                return 0.5;
            return 0.0;
        },

        ["SQRT-SUM"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student added square roots instead of computing root of sum
            // e.g., √(9+16)=√25=5 but student got 3+4=7
            if (double.TryParse(student.Trim(), out var sVal) &&
                double.TryParse(correct.Trim(), out var cVal) &&
                sVal > cVal && Math.Abs(sVal - cVal) > 1)
                return 0.5;
            return 0.0;
        },

        ["NEGATIVE-EXPONENT"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student wrote a negative number instead of a fraction
            if (student.StartsWith("-") && correct.StartsWith("0.") || correct.Contains("/"))
                return 0.7;
            return 0.0;
        },

        // ── Calculus ──

        ["CHAIN-RULE-MISSING"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student answer is missing the chain factor
            // e.g., correct="2cos(2x)" but student="cos(2x)"
            // The student's answer is a suffix of the correct answer (missing the leading factor)
            if (correct.Length > student.Length && correct.EndsWith(student))
                return 0.8;
            // Or same trig function but correct has a multiplier prefix
            if ((correct.Contains("cos") && student.Contains("cos") ||
                 correct.Contains("sin") && student.Contains("sin")) &&
                correct.Length > student.Length)
                return 0.7;
            return 0.0;
        },

        ["INTEGRAL-CONSTANT"] = (correct, student, subject) =>
        {
            if (!subject.Equals("math", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student forgot +C
            if (correct.Contains("+ C") && !student.Contains("C") && !student.Contains("c"))
                return 0.9;
            if (correct.Contains("+C") && !student.Contains("C") && !student.Contains("c"))
                return 0.9;
            return 0.0;
        },

        // ── Physics ──

        ["VELOCITY-ACCEL-SIGN"] = (correct, student, subject) =>
        {
            if (!subject.Equals("physics", StringComparison.OrdinalIgnoreCase)) return 0.0;
            // Pattern: student says 0 instead of -9.8 (or similar)
            if (student.Trim() == "0" && (correct.Contains("-9.8") || correct.Contains("−9.8")))
                return 0.9;
            return 0.0;
        },
    };

    public MisconceptionDetectionService(ILogger<MisconceptionDetectionService> logger)
        : this(logger, null) { }

    /// <summary>
    /// RDY-033 constructor: engine-first detection. The matcher engine runs the
    /// CAS-backed IErrorPatternMatcher registry (ADR-0031); the legacy string
    /// heuristics remain as a fallback for rules the engine does not yet cover.
    /// </summary>
    public MisconceptionDetectionService(
        ILogger<MisconceptionDetectionService> logger,
        IErrorPatternMatcherEngine? matcherEngine)
    {
        _logger = logger;
        _matcherEngine = matcherEngine;
    }

    public MisconceptionDetectionResult Detect(
        string questionStem,
        string correctAnswer,
        string studentAnswer,
        string subject,
        string? conceptId,
        ExplanationErrorType? errorType)
    {
        // RDY-033: Prefer the CAS-grounded matcher engine when registered.
        // We run the engine synchronously from this sync interface by awaiting
        // the task without SyncContext — the engine's 100 ms budget caps the wait.
        if (_matcherEngine is not null)
        {
            var engineResult = RunEngineSync(questionStem, correctAnswer, studentAnswer, subject, conceptId);
            if (engineResult is { Matched: true, BuggyRuleId: not null })
            {
                var entry = MisconceptionCatalog.GetById(engineResult.BuggyRuleId);
                if (entry is not null)
                {
                    _logger.LogInformation(
                        "[MISCONCEPTION] Engine-matched {RuleId} (confidence {Confidence:F2}, engine={Engine}, elapsed_ms={Elapsed:F1})",
                        engineResult.BuggyRuleId, engineResult.Confidence, engineResult.Engine, engineResult.ElapsedMs);

                    return new MisconceptionDetectionResult(
                        Detected: true,
                        BuggyRuleId: engineResult.BuggyRuleId,
                        Confidence: engineResult.Confidence,
                        CounterExample: entry.CounterExample,
                        RemediationTask: GetRemediation(engineResult.BuggyRuleId));
                }
            }
        }

        // Legacy string-heuristic fallback (covers rules not yet lifted to IErrorPatternMatcher).
        foreach (var (ruleId, matcher) in Matchers)
        {
            var entry = MisconceptionCatalog.GetById(ruleId);
            if (entry == null) continue;

            // Skip matchers for different subjects
            if (!string.Equals(entry.Subject, subject, StringComparison.OrdinalIgnoreCase))
                continue;

            var confidence = matcher(correctAnswer, studentAnswer, subject);
            if (confidence >= 0.5)
            {
                _logger.LogInformation(
                    "[MISCONCEPTION] Detected {RuleId} (confidence {Confidence:F2}) — student: {StudentAnswer}, correct: {CorrectAnswer}",
                    ruleId, confidence, studentAnswer, correctAnswer);

                var remediation = GetRemediation(ruleId);

                return new MisconceptionDetectionResult(
                    Detected: true,
                    BuggyRuleId: ruleId,
                    Confidence: confidence,
                    CounterExample: entry.CounterExample,
                    RemediationTask: remediation);
            }
        }

        // No pattern match — check if error type hints at a misconception
        if (errorType == ExplanationErrorType.ConceptualMisunderstanding)
        {
            // Generic misconception signal from LLM classification
            // but no specific buggy rule matched
            _logger.LogDebug(
                "ConceptualMisunderstanding classified but no specific buggy rule matched for subject={Subject}",
                subject);
        }

        return new MisconceptionDetectionResult(
            Detected: false,
            BuggyRuleId: null,
            Confidence: 0.0,
            CounterExample: null,
            RemediationTask: null);
    }

    public RemediationTask? GetRemediation(string buggyRuleId)
    {
        return RemediationTemplates.GetTemplate(buggyRuleId);
    }

    /// <summary>
    /// Bridge between the sync Detect() contract (kept for source compatibility with
    /// RDY-014 call sites) and the async IErrorPatternMatcherEngine. Safe because the
    /// engine caps itself at a 100 ms budget; we do not hold any async context here.
    /// </summary>
    private ErrorPatternMatchResult? RunEngineSync(
        string questionStem,
        string correctAnswer,
        string studentAnswer,
        string subject,
        string? conceptId)
    {
        try
        {
            var context = new ErrorPatternMatchContext(
                questionStem, correctAnswer, studentAnswer, subject, conceptId);
            return _matcherEngine!
                .ClassifyAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Matcher engine threw; falling back to legacy heuristics");
            return null;
        }
    }
}
