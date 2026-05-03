// =============================================================================
// Cena Platform — Error Pattern Matcher Engine (RDY-033, ADR-0031)
//
// Iterates registered IErrorPatternMatcher instances and returns the highest-
// confidence match for a given (questionStem, correctAnswer, studentAnswer)
// triple. Bounded by a 100 ms wall-clock budget per classification. Emits a
// structured log event for every unmatched error so the corpus can be mined
// for new buggy-rule proposals.
//
// All misconception data is session-scoped (ADR-0003): this engine does not
// persist anything. It is a pure query service.
// =============================================================================

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services.ErrorPatternMatching;

public interface IErrorPatternMatcherEngine
{
    /// <summary>
    /// Classify a wrong answer against all registered matchers whose subject applies.
    /// Returns the highest-confidence match, or an unmatched result if no matcher
    /// reached the 0.5 confidence threshold.
    /// </summary>
    Task<ErrorPatternMatchResult> ClassifyAsync(ErrorPatternMatchContext context, CancellationToken ct = default);
}

public sealed class ErrorPatternMatcherEngine : IErrorPatternMatcherEngine
{
    private readonly IReadOnlyList<IErrorPatternMatcher> _matchers;
    private readonly ILogger<ErrorPatternMatcherEngine> _logger;

    /// <summary>
    /// Wall-clock budget for a full classification call. ADR-0031 requires &lt;100 ms
    /// per answer; we set 100 ms as the absolute ceiling.
    /// </summary>
    public static readonly TimeSpan BudgetPerClassification = TimeSpan.FromMilliseconds(100);

    /// <summary>Threshold above which a match is considered "detected".</summary>
    public const double MatchThreshold = 0.5;

    public ErrorPatternMatcherEngine(
        IEnumerable<IErrorPatternMatcher> matchers,
        ILogger<ErrorPatternMatcherEngine> logger)
    {
        _matchers = matchers?.ToList() ?? throw new ArgumentNullException(nameof(matchers));
        _logger = logger;
    }

    public async Task<ErrorPatternMatchResult> ClassifyAsync(
        ErrorPatternMatchContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Trivial short-circuit: student matched the correct answer literally.
        // (A CAS equivalence check is too expensive to spend here — that decision
        //  lives upstream in the answer evaluator.)
        if (string.Equals(
                context.StudentAnswer?.Trim(),
                context.CorrectAnswer?.Trim(),
                StringComparison.Ordinal))
        {
            return ErrorPatternMatchResult.NoMatch("none", "student answer identical to correct answer", 0, "short-circuit");
        }

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(BudgetPerClassification);

        var overallSw = Stopwatch.StartNew();
        ErrorPatternMatchResult? best = null;
        int tried = 0, skippedBySubject = 0;

        foreach (var matcher in _matchers)
        {
            if (budgetCts.IsCancellationRequested) break;

            // Cheap subject filter — avoids paying CAS budget on irrelevant matchers.
            if (!string.Equals(matcher.Subject, context.Subject, StringComparison.OrdinalIgnoreCase))
            {
                skippedBySubject++;
                continue;
            }

            tried++;
            ErrorPatternMatchResult result;
            try
            {
                result = await matcher.TryMatchAsync(context, budgetCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Budget exhausted during matcher {RuleId}", matcher.BuggyRuleId);
                break;
            }

            if (result.Matched && result.Confidence >= MatchThreshold)
            {
                if (best is null || result.Confidence > best.Confidence)
                    best = result;

                // Early exit: exact symbolic match is unambiguous, no need to keep spending budget.
                if (result.Confidence >= 1.0) break;
            }
        }

        if (best is not null)
        {
            _logger.LogInformation(
                "[PATTERN_MATCH] {RuleId} conf={Confidence:F2} engine={Engine} subject={Subject} tried={Tried} elapsed_ms={Elapsed:F1}",
                best.BuggyRuleId, best.Confidence, best.Engine, context.Subject, tried, overallSw.Elapsed.TotalMilliseconds);
            return best;
        }

        _logger.LogInformation(
            "[ERROR_UNMATCHED] subject={Subject} correct={Correct} student={Student} matchers_tried={Tried} skipped_by_subject={Skipped} elapsed_ms={Elapsed:F1}",
            context.Subject,
            context.CorrectAnswer,
            context.StudentAnswer,
            tried,
            skippedBySubject,
            overallSw.Elapsed.TotalMilliseconds);

        return ErrorPatternMatchResult.NoMatch("none",
            $"no matcher reached {MatchThreshold:F1} confidence (tried {tried}, skipped {skippedBySubject})",
            overallSw.Elapsed.TotalMilliseconds,
            "engine");
    }
}
