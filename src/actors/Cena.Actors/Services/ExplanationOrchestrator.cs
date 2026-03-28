// =============================================================================
// Cena Platform -- Explanation Orchestrator (L1 -> L2 -> L3)
// SAI-03/04: Single entry point for explanation resolution
//
// Resolution chain:
//   1. L2 cache hit -> return cached explanation
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
    // L3 enrichment (optional — all null = L2-only resolution)
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
    private readonly ILogger<ExplanationOrchestrator> _logger;

    public ExplanationOrchestrator(
        IExplanationCacheService cache,
        IExplanationGenerator generator,
        ILogger<ExplanationOrchestrator> logger)
    {
        _cache = cache;
        _generator = generator;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(ExplanationRequest request, CancellationToken ct)
    {
        // ── L2: Try cache ──
        var cached = await _cache.GetAsync(
            request.QuestionId, request.ErrorType, request.Methodology, ct);

        if (cached is not null)
        {
            _logger.LogDebug(
                "L2 cache hit for question {QuestionId}, error {ErrorType}",
                request.QuestionId, request.ErrorType);
            return cached.Text;
        }

        // ── L3: Generate via LLM ──
        try
        {
            var context = new ExplanationContext(
                QuestionStem: request.QuestionStem,
                CorrectAnswer: request.CorrectAnswer,
                StudentAnswer: request.StudentAnswer,
                ErrorType: request.ErrorType,
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
                "L3 generated explanation for question {QuestionId}, model {ModelId}, tokens {Tokens}",
                request.QuestionId, generated.ModelId, generated.TokenCount);

            // Cache the generated explanation for future L2 hits
            var toCache = new CachedExplanation(
                Text: generated.Text,
                ModelId: generated.ModelId,
                TokenCount: generated.TokenCount,
                GeneratedAt: DateTimeOffset.UtcNow);

            // Fire-and-forget cache write — don't block the response
            _ = _cache.SetAsync(
                request.QuestionId, request.ErrorType, request.Methodology, toCache, ct);

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
}
