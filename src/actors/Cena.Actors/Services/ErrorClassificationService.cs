// =============================================================================
// Cena Platform -- Error Classification Service (SAI-02)
// Classifies incorrect answers into ExplanationErrorType via LLM.
//
// Routing config: error_classification -> Kimi K2 primary, Haiku fallback.
// Since Kimi is not yet implemented, uses Haiku (claude-haiku-4-5-20260101).
//
// Classification is fast and cheap (~200 tokens output, ~$0.001/call).
// All calls go through ILlmClient which respects circuit breaker routing.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Cena.Actors.Gateway;
using Cena.Infrastructure.Llm;

namespace Cena.Actors.Services;

// =============================================================================
// CLASSIFICATION INPUT
// =============================================================================

/// <summary>
/// Input context for error classification. Contains question and answer data
/// needed to determine the type of mistake the student made.
/// </summary>
public sealed record ErrorClassificationInput(
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    string? DistractorRationale,
    string Subject,
    string Language,
    float? QuestionDifficulty = null,                 // 0.0-1.0 intrinsic difficulty
    float? StudentMastery = null);                    // Current mastery for difficulty-gap context

// =============================================================================
// INTERFACE
// =============================================================================

/// <summary>
/// Classifies a student's incorrect answer into an ExplanationErrorType.
/// Uses LLM (Haiku -- cheap, fast) per routing-config.yaml error_classification task.
/// </summary>
public interface IErrorClassificationService
{
    /// <summary>
    /// Classify the error type for a wrong answer.
    /// Returns PartialUnderstanding as a safe default if classification fails.
    /// </summary>
    Task<ExplanationErrorType> ClassifyAsync(ErrorClassificationInput input, CancellationToken ct);
}

// =============================================================================
// IMPLEMENTATION
// =============================================================================

// ADR-0045: 5-class enum classification (ConceptualMisunderstanding, ProceduralError,
// CarelessMistake, Guessing, PartialUnderstanding) — 200 output tokens, temp=0.
// Canonical tier-2 path. Routing row: contracts/llm/routing-config.yaml
// §task_routing.error_classification (Kimi primary, Haiku fallback).
//
// prr-047: classification output depends on the free-form StudentAnswer string,
// which has infinite variety. Caching by exact answer text would yield ~0% hit
// rate and waste Redis space. This is a legitimate cache bypass — error
// classification is the cheapest call in the explain chain (~$0.001) and its
// result is what KEYs the downstream explanation cache, so caching here would
// just push one indirection deeper without cutting tokens.
// prr-046: finops cost-center "classification". Tier-2 error classification
// shares the routing row with future wrong-answer classifiers; the feature
// label lets finops separate wrong-answer spend from other tier-2 usage.
// ADR-0047: this service composes StudentAnswer free-text into its user prompt.
// It injects IPiiPromptScrubber and fails closed (returns the safe default
// PartialUnderstanding) if the scrubber detects residual PII — that path is
// the canonical demonstration of ADR-0047 Decision 4 ("fail-closed on counter
// increment"). An unscrubbed call is never made to the LLM.
[TaskRouting("tier2", "error_classification")]
[FeatureTag("classification")]
[AllowsUncachedLlm("Classification keyed by free-form student answer text — no repeatable prompt to cache. Result is itself a cache key for the explain tier.")]
public sealed class ErrorClassificationService : IErrorClassificationService
{
    // Haiku fallback for error_classification (routing-config.yaml: Kimi primary, Haiku fallback)
    // Temperature 0.0 for consistent classification (routing-config.yaml section 2)
    // Max tokens 200 (routing-config.yaml section 2)
    private const string HaikuModelId = "claude-haiku-4-5-20260101";
    private const float ClassificationTemperature = 0.0f;
    private const int ClassificationMaxTokens = 200;

    /// <summary>
    /// Feature tag reused for the cost metric AND for the PII-scrub counter —
    /// must match the <see cref="FeatureTagAttribute"/> value on the class.
    /// </summary>
    private const string FeatureLabel = "classification";

    private readonly ILlmClient _llm;
    private readonly ILogger<ErrorClassificationService> _logger;
    private readonly Histogram<double> _classificationLatency;
    private readonly Counter<long> _classificationCounter;
    private readonly ILlmCostMetric _costMetric;
    private readonly IPiiPromptScrubber _piiScrubber;

    public ErrorClassificationService(
        ILlmClient llm,
        ILogger<ErrorClassificationService> logger,
        IMeterFactory meterFactory,
        ILlmCostMetric costMetric,
        IPiiPromptScrubber piiScrubber)
    {
        _llm = llm;
        _logger = logger;
        _costMetric = costMetric;
        _piiScrubber = piiScrubber;

        var meter = meterFactory.Create("Cena.Actors.ErrorClassification", "1.0.0");
        _classificationLatency = meter.CreateHistogram<double>(
            "cena.error_classification.latency_ms",
            unit: "ms",
            description: "Error classification LLM call latency");
        _classificationCounter = meter.CreateCounter<long>(
            "cena.error_classification.total",
            description: "Total error classifications performed");
    }

    public async Task<ExplanationErrorType> ClassifyAsync(
        ErrorClassificationInput input, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var userPrompt = BuildUserPrompt(input);

            // ADR-0047 Decision 4 — fail-closed on scrubber increment.
            // The PII scrubber returns RedactionCount > 0 iff it found a
            // residual pattern (email, phone, government_id, address, postal
            // code). By design of ADR-0047, that MUST NOT reach the LLM —
            // serve the pedagogically-neutral PartialUnderstanding default
            // and trust the scrubber's fail-closed metric to raise a
            // severity-1 alert for the on-call engineer.
            var scrub = _piiScrubber.Scrub(userPrompt, FeatureLabel);
            if (scrub.RedactionCount > 0)
            {
                _logger.LogWarning(
                    "[ADR-0047] PII detected in error-classification prompt — refusing LLM call. " +
                    "Categories=[{Categories}]. Returning PartialUnderstanding. See ADR-0047 runbook.",
                    string.Join(",", scrub.Categories));
                return ExplanationErrorType.PartialUnderstanding;
            }

            var request = new LlmRequest(
                SystemPrompt: BuildSystemPrompt(),
                UserPrompt: scrub.ScrubbedText,
                Temperature: ClassificationTemperature,
                MaxTokens: ClassificationMaxTokens,
                ModelId: HaikuModelId);

            var response = await _llm.CompleteAsync(request, ct);
            sw.Stop();

            _classificationLatency.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("model_id", response.ModelId));
            _classificationCounter.Add(1,
                new KeyValuePair<string, object?>("model_id", response.ModelId));

            // prr-046: per-feature cost tag on success path.
            _costMetric.Record(
                feature: FeatureLabel,
                tier: "tier2",
                task: "error_classification",
                modelId: response.ModelId,
                inputTokens: response.InputTokens,
                outputTokens: response.OutputTokens);

            var errorType = ParseClassification(response.Content);

            _logger.LogDebug(
                "Error classified as {ErrorType} in {ElapsedMs}ms (model={ModelId}, tokens={Tokens})",
                errorType, sw.ElapsedMilliseconds, response.ModelId, response.OutputTokens);

            return errorType;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "Error classification failed after {ElapsedMs}ms. Defaulting to PartialUnderstanding.",
                sw.ElapsedMilliseconds);

            // Safe default: PartialUnderstanding is the most pedagogically neutral choice
            return ExplanationErrorType.PartialUnderstanding;
        }
    }

    // =========================================================================
    // PROMPT CONSTRUCTION
    // =========================================================================

    private static string BuildSystemPrompt()
    {
        return """
            You are an educational error classifier. Given a question, the correct answer, and the student's wrong answer, classify the type of error into EXACTLY ONE of these categories:

            - ConceptualMisunderstanding: The student has a wrong mental model of the concept.
            - ProceduralError: The student understands the concept but executed the procedure incorrectly.
            - CarelessMistake: The student understands and can do it, but was sloppy or rushed.
            - Guessing: No evidence the student understands the concept at all.
            - PartialUnderstanding: The student is close but missed a key step or detail.

            Respond with ONLY the category name, nothing else. No explanation, no punctuation.
            """;
    }

    private static string BuildUserPrompt(ErrorClassificationInput input)
    {
        var parts = new List<string>(7)
        {
            $"SUBJECT: {input.Subject}",
            $"QUESTION: {input.QuestionStem}",
            $"CORRECT ANSWER: {input.CorrectAnswer}",
            $"STUDENT'S ANSWER: {input.StudentAnswer}"
        };

        if (!string.IsNullOrEmpty(input.DistractorRationale))
            parts.Add($"DISTRACTOR RATIONALE: {input.DistractorRationale}");

        // Difficulty context helps the LLM distinguish stretch-challenge failures
        // from regression failures. A 0.9-difficulty question wrong is different
        // from a 0.3-difficulty question wrong.
        if (input.QuestionDifficulty.HasValue)
        {
            parts.Add($"QUESTION DIFFICULTY: {input.QuestionDifficulty.Value:F2} (0=easy, 1=hard)");

            if (input.StudentMastery.HasValue)
            {
                var gap = input.QuestionDifficulty.Value - input.StudentMastery.Value;
                if (gap > 0.25f)
                    parts.Add("NOTE: This question was significantly above the student's current mastery level (stretch challenge). Be lenient toward Guessing or PartialUnderstanding.");
                else if (gap < -0.20f)
                    parts.Add("NOTE: This question was well below the student's mastery level. Consider CarelessMistake or ConceptualMisunderstanding more strongly.");
            }
        }

        return string.Join("\n", parts);
    }

    // =========================================================================
    // RESPONSE PARSING
    // =========================================================================

    /// <summary>
    /// Parse the LLM response into an ExplanationErrorType.
    /// Performs fuzzy matching to handle minor variations in LLM output.
    /// </summary>
    internal static ExplanationErrorType ParseClassification(string response)
    {
        var normalized = response.Trim().ToLowerInvariant()
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "");

        return normalized switch
        {
            "conceptualmisunderstanding" or "conceptual" => ExplanationErrorType.ConceptualMisunderstanding,
            "proceduralerror" or "procedural" => ExplanationErrorType.ProceduralError,
            "carelessmistake" or "careless" => ExplanationErrorType.CarelessMistake,
            "guessing" or "guess" => ExplanationErrorType.Guessing,
            "partialunderstanding" or "partial" => ExplanationErrorType.PartialUnderstanding,
            _ => FuzzyMatch(normalized)
        };
    }

    private static ExplanationErrorType FuzzyMatch(string normalized)
    {
        if (normalized.Contains("conceptual") || normalized.Contains("misunderstand"))
            return ExplanationErrorType.ConceptualMisunderstanding;
        if (normalized.Contains("procedur"))
            return ExplanationErrorType.ProceduralError;
        if (normalized.Contains("careless") || normalized.Contains("sloppy"))
            return ExplanationErrorType.CarelessMistake;
        if (normalized.Contains("guess") || normalized.Contains("random"))
            return ExplanationErrorType.Guessing;

        // Default: PartialUnderstanding is the safest pedagogical choice
        return ExplanationErrorType.PartialUnderstanding;
    }
}
