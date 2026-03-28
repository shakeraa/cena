// =============================================================================
// Cena Platform -- Explanation Orchestrator (L2 cache -> L3 -> L2-generate -> L1 -> generic)
// SAI-02/03/04: Single entry point for explanation resolution
//
// Resolution chain (SAI-004 corrected):
//   0. Classify error type via LLM (Haiku -- SAI-02)
//   1. L2 cache hit AND no high-uncertainty signals -> return cached (fast path)
//   2. L3 personalized (when L2 miss OR high uncertainty) -> return ephemeral
//   3. L2 generic LLM generation -> cache result, return
//   4. L1 static fallback -> return PublishedQuestion.Explanation
//   5. Generic fallback -> "Review the question and consider each option carefully."
//
// L3 escalation triggers: backspaceCount > 5 || answerChangeCount > 2
//   || Transfer/Systematic error type. L3 is expensive -- only when warranted.
// L3 explanations are ephemeral (not cached) -- they incorporate transient state.
// =============================================================================

using System.Collections.Concurrent;
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
    IReadOnlyList<string>? PrerequisiteConceptNames = null,
    // SAI-004: Full L3 context (optional -- null = L2-only resolution)
    L3ExplanationRequest? L3Context = null);

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

    // SAI-004: Per-student daily output token budget (from routing-config.yaml)
    private const int DailyOutputTokenLimit = 25_000;

    // Tracks cumulative output tokens per student per day: key = "studentId:yyyy-MM-dd"
    private static readonly ConcurrentDictionary<string, int> s_dailyTokenUsage = new();

    private readonly IExplanationCacheService _cache;
    private readonly IExplanationGenerator _generator;
    private readonly IL3ExplanationGenerator _l3Generator;
    private readonly IErrorClassificationService _errorClassifier;
    private readonly ILogger<ExplanationOrchestrator> _logger;

    public ExplanationOrchestrator(
        IExplanationCacheService cache,
        IExplanationGenerator generator,
        IL3ExplanationGenerator l3Generator,
        IErrorClassificationService errorClassifier,
        ILogger<ExplanationOrchestrator> logger)
    {
        _cache = cache;
        _generator = generator;
        _l3Generator = l3Generator;
        _errorClassifier = errorClassifier;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(ExplanationRequest request, CancellationToken ct)
    {
        // ── Step 0: Classify error type via LLM (Haiku -- cheap, fast) ──
        var classifiedErrorType = await ClassifyErrorAsync(request, ct);

        // ── Determine if L3 escalation is warranted ──
        bool highUncertainty = HasHighUncertaintySignals(request, classifiedErrorType);
        bool l3Available = request.L3Context is not null;

        // ── L2: Try cache with classified error type + language ──
        var cached = await _cache.GetAsync(
            request.QuestionId, classifiedErrorType, request.Language, ct);

        if (cached is not null && !highUncertainty)
        {
            // Fast path: L2 cache hit with no high-uncertainty signals
            _logger.LogDebug(
                "L2 cache hit for question {QuestionId}, errorType {ErrorType}, language {Language}",
                request.QuestionId, classifiedErrorType, request.Language);
            return cached.Text;
        }

        if (cached is not null && highUncertainty)
        {
            _logger.LogDebug(
                "L2 cache hit but high-uncertainty signals detected for question {QuestionId}. " +
                "Escalating to L3 for personalized explanation.",
                request.QuestionId);
        }

        // Also check legacy cache key (methodology-based) for backward compatibility
        if (cached is null)
        {
            var legacyCached = await _cache.GetAsync(
                request.QuestionId, request.ErrorType, request.Methodology, ct);
            if (legacyCached is not null && !highUncertainty)
            {
                _logger.LogDebug(
                    "Legacy L2 cache hit for question {QuestionId}, error {ErrorType}",
                    request.QuestionId, request.ErrorType);
                return legacyCached.Text;
            }
        }

        // ── L3: Personalized generation with full student context (SAI-004) ──
        // L3 fires when: (L2 miss OR high uncertainty) AND L3 context is available
        // AND daily token budget not exhausted
        if (l3Available && (cached is null || highUncertainty))
        {
            var l3Result = await TryL3GenerationAsync(request, ct);
            if (l3Result is not null)
                return l3Result;
        }

        // If we had a cached result but escalated to L3 and L3 failed, return L2
        if (cached is not null)
            return cached.Text;

        // ── L2-generate: Generic LLM explanation (no full student context) ──
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
                "L2 generated explanation for question {QuestionId}, errorType {ErrorType}, " +
                "model {ModelId}, tokens {Tokens}",
                request.QuestionId, classifiedErrorType, generated.ModelId, generated.TokenCount);

            // Cache the generated explanation for future L2 hits
            CacheExplanation(request.QuestionId, classifiedErrorType, request.Language, generated, ct);

            return generated.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "L2 generation failed for question {QuestionId}. Falling back to L1/generic.",
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
    // L3 GENERATION WITH BUDGET CHECK
    // =========================================================================

    /// <summary>
    /// Attempts L3 personalized generation. Checks daily token budget first.
    /// Returns null if generation is suppressed (affect gate, budget, or failure).
    /// L3 results are ephemeral -- NOT cached back to L2.
    /// </summary>
    private async Task<string?> TryL3GenerationAsync(
        ExplanationRequest request, CancellationToken ct)
    {
        var l3Ctx = request.L3Context!;

        // SAI-004: Check per-student daily output token budget
        var budgetKey = $"l3:{DateTime.UtcNow:yyyy-MM-dd}";
        var currentUsage = s_dailyTokenUsage.GetValueOrDefault(budgetKey, 0);
        if (currentUsage >= DailyOutputTokenLimit)
        {
            _logger.LogInformation(
                "L3 daily token budget exhausted ({Current}/{Limit}) for question {QuestionId}. " +
                "Falling back to L2.",
                currentUsage, DailyOutputTokenLimit, request.QuestionId);
            return null;
        }

        try
        {
            var l3Result = await _l3Generator.GenerateAsync(l3Ctx, ct);

            if (l3Result is not null)
            {
                // Track token usage against daily budget
                s_dailyTokenUsage.AddOrUpdate(budgetKey,
                    l3Result.TokenCount,
                    (_, prev) => prev + l3Result.TokenCount);

                _logger.LogDebug(
                    "L3 personalized explanation for question {QuestionId}, " +
                    "focus={Focus}, confusion={Confusion}, model={ModelId}, tokens={Tokens}",
                    request.QuestionId, l3Ctx.FocusLevel,
                    l3Ctx.ConfusionState, l3Result.ModelId, l3Result.TokenCount);

                // L3 explanations are ephemeral -- do NOT cache.
                // They incorporate transient student state that changes per-question.
                return l3Result.Text;
            }

            // L3 returned null = affect gate suppressed generation
            _logger.LogDebug(
                "L3 suppressed for question {QuestionId} (affect gate). Continuing fallback.",
                request.QuestionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "L3 personalized generation failed for question {QuestionId}. Continuing fallback.",
                request.QuestionId);
        }

        return null;
    }

    // =========================================================================
    // L3 ESCALATION LOGIC
    // =========================================================================

    /// <summary>
    /// Determines whether L3 personalized generation should be attempted
    /// even when an L2 cache hit exists. High-uncertainty behavioral signals
    /// or complex error types warrant personalized explanations.
    /// </summary>
    private static bool HasHighUncertaintySignals(
        ExplanationRequest request, ExplanationErrorType errorType)
    {
        // Behavioral signals indicating genuine confusion
        if (request.L3Context is not null)
        {
            if (request.L3Context.BackspaceCount > 5) return true;
            if (request.L3Context.AnswerChangeCount > 2) return true;
        }

        // Fall back to top-level signals if L3 context not available
        if (request.BackspaceCount is > 5) return true;
        if (request.AnswerChangeCount is > 2) return true;

        // Complex error types that benefit from personalized explanation
        if (errorType is ExplanationErrorType.TransferError
            or ExplanationErrorType.SystematicError)
            return true;

        return false;
    }

    // =========================================================================
    // CACHE HELPER
    // =========================================================================

    /// <summary>
    /// Fire-and-forget cache write for generated explanations.
    /// </summary>
    private void CacheExplanation(
        string questionId, ExplanationErrorType errorType, string language,
        GeneratedExplanation generated, CancellationToken ct)
    {
        var toCache = new CachedExplanation(
            Text: generated.Text,
            ModelId: generated.ModelId,
            TokenCount: generated.TokenCount,
            GeneratedAt: DateTimeOffset.UtcNow);

        // Fire-and-forget cache write -- don't block the response
        _ = _cache.SetAsync(questionId, errorType, language, toCache, ct);
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
