// =============================================================================
// Cena Platform -- Explanation Orchestrator (L1 -> L2 -> L3)
// SAI-02/03/04: Single entry point for explanation resolution
//
// Resolution chain:
//   0. Classify error type via LLM (Haiku -- SAI-02)
//   1. L2 cache hit -> return cached explanation (keyed by questionId + errorType + language)
//   2. L3 LLM generation -> cache result, return
//   3. L1 static fallback -> return PublishedQuestion.Explanation
//   4. Generic fallback -> "Review the question and consider each option carefully."
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Request for explanation resolution. The orchestrator decides which tier to use.
/// </summary>
public sealed record ExplanationRequest(
    string QuestionId,
    string? StaticExplanation,   // L1 from PublishedQuestion.Explanation
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    string ErrorType,
    string Methodology,
    string? DistractorRationale,
    int BloomsLevel,
    string Subject,
    string Language,
    // L3 enrichment (optional -- all null = L2-only resolution)
    double? ConceptMastery = null,
    string? FatigueLevel = null,
    int? BackspaceCount = null,
    int? AnswerChangeCount = null,
    IReadOnlyList<string>? RecentErrorTypes = null,
    IReadOnlyList<string>? PrerequisiteConceptNames = null);

/// <summary>
/// The single entry point for all explanation resolution.
/// Consumers should ONLY use this interface, never call cache or generator directly.
/// </summary>
public interface IExplanationOrchestrator
{
    Task<string> ResolveAsync(ExplanationRequest request, CancellationToken ct);
}

public sealed class ExplanationOrchestrator : IExplanationOrchestrator
{
    private const string GenericFallback =
        "Review the question and consider each option carefully.";

    private readonly IExplanationCacheService _cache;
    private readonly IExplanationGenerator _generator;
    private readonly IErrorClassificationService _errorClassifier;
    private readonly ILogger<ExplanationOrchestrator> _logger;

    public ExplanationOrchestrator(
        IExplanationCacheService cache,
        IExplanationGenerator generator,
        IErrorClassificationService errorClassifier,
        ILogger<ExplanationOrchestrator> logger)
    {
        _cache = cache;
        _generator = generator;
        _errorClassifier = errorClassifier;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(ExplanationRequest request, CancellationToken ct)
    {
        // ── Step 0: Classify error type via LLM (Haiku -- cheap, fast) ──
        var classifiedErrorType = await ClassifyErrorAsync(request, ct);

        // ── L2: Try cache with classified error type + language ──
        var cached = await _cache.GetAsync(
            request.QuestionId, classifiedErrorType, request.Language, ct);

        if (cached is not null)
        {
            _logger.LogDebug(
                "L2 cache hit for question {QuestionId}, errorType {ErrorType}, language {Language}",
                request.QuestionId, classifiedErrorType, request.Language);
            return cached.Text;
        }

        // Also check legacy cache key (methodology-based) for backward compatibility
        var legacyCached = await _cache.GetAsync(
            request.QuestionId, request.ErrorType, request.Methodology, ct);
        if (legacyCached is not null)
        {
            _logger.LogDebug(
                "Legacy L2 cache hit for question {QuestionId}, error {ErrorType}",
                request.QuestionId, request.ErrorType);
            return legacyCached.Text;
        }

        // ── L3: Generate via LLM (Sonnet -- answer_evaluation task type) ──
        try
        {
            var context = new ExplanationContext(
                QuestionStem: request.QuestionStem,
                CorrectAnswer: request.CorrectAnswer,
                StudentAnswer: request.StudentAnswer,
                ErrorType: classifiedErrorType.ToString(),
                Methodology: request.Methodology,
                DistractorRationale: request.DistractorRationale,
                BloomsLevel: request.BloomsLevel,
                Subject: request.Subject,
                Language: request.Language,
                ConceptMastery: request.ConceptMastery,
                FatigueLevel: request.FatigueLevel,
                BackspaceCount: request.BackspaceCount,
                AnswerChangeCount: request.AnswerChangeCount,
                RecentErrorTypes: request.RecentErrorTypes,
                PrerequisiteConceptNames: request.PrerequisiteConceptNames);

            var generated = await _generator.GenerateAsync(context, ct);

            _logger.LogDebug(
                "L3 generated explanation for question {QuestionId}, errorType {ErrorType}, model {ModelId}, tokens {Tokens}",
                request.QuestionId, classifiedErrorType, generated.ModelId, generated.TokenCount);

            // Cache the generated explanation for future L2 hits
            var toCache = new CachedExplanation(
                Text: generated.Text,
                ModelId: generated.ModelId,
                TokenCount: generated.TokenCount,
                GeneratedAt: DateTimeOffset.UtcNow);

            // Fire-and-forget cache write -- don't block the response
            _ = _cache.SetAsync(
                request.QuestionId, classifiedErrorType, request.Language, toCache, ct);

            return generated.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "L3 generation failed for question {QuestionId}. Falling back to L1/generic.",
                request.QuestionId);
        }

        // ── L1: Static explanation from question bank ──
        if (!string.IsNullOrWhiteSpace(request.StaticExplanation))
        {
            _logger.LogDebug(
                "L1 static fallback for question {QuestionId}", request.QuestionId);
            return request.StaticExplanation;
        }

        // ── Generic fallback ──
        _logger.LogWarning(
            "All explanation tiers exhausted for question {QuestionId}. Using generic fallback.",
            request.QuestionId);
        return GenericFallback;
    }

    // =========================================================================
    // ERROR CLASSIFICATION
    // =========================================================================

    /// <summary>
    /// Classify the error type using LLM (Haiku). On failure, falls back to
    /// PartialUnderstanding (the service itself handles this).
    /// </summary>
    private async Task<ExplanationErrorType> ClassifyErrorAsync(
        ExplanationRequest request, CancellationToken ct)
    {
        try
        {
            var input = new ErrorClassificationInput(
                QuestionStem: request.QuestionStem,
                CorrectAnswer: request.CorrectAnswer,
                StudentAnswer: request.StudentAnswer,
                DistractorRationale: request.DistractorRationale,
                Subject: request.Subject,
                Language: request.Language);

            return await _errorClassifier.ClassifyAsync(input, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error classification failed for question {QuestionId}. Defaulting to PartialUnderstanding.",
                request.QuestionId);
            return ExplanationErrorType.PartialUnderstanding;
        }
    }
}
