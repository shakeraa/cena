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

public sealed class ErrorClassificationService : IErrorClassificationService
{
    // Haiku fallback for error_classification (routing-config.yaml: Kimi primary, Haiku fallback)
    // Temperature 0.0 for consistent classification (routing-config.yaml section 2)
    // Max tokens 200 (routing-config.yaml section 2)
    private const string HaikuModelId = "claude-haiku-4-5-20260101";
    private const float ClassificationTemperature = 0.0f;
    private const int ClassificationMaxTokens = 200;

    private readonly ILlmClient _llm;
    private readonly ILogger<ErrorClassificationService> _logger;
    private readonly Histogram<double> _classificationLatency;
    private readonly Counter<long> _classificationCounter;

    public ErrorClassificationService(
        ILlmClient llm,
        ILogger<ErrorClassificationService> logger,
        IMeterFactory meterFactory)
    {
        _llm = llm;
        _logger = logger;

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
            var request = new LlmRequest(
                SystemPrompt: BuildSystemPrompt(),
                UserPrompt: BuildUserPrompt(input),
                Temperature: ClassificationTemperature,
                MaxTokens: ClassificationMaxTokens,
                ModelId: HaikuModelId);

            var response = await _llm.CompleteAsync(request, ct);
            sw.Stop();

            _classificationLatency.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("model_id", response.ModelId));
            _classificationCounter.Add(1,
                new KeyValuePair<string, object?>("model_id", response.ModelId));

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
