// =============================================================================
// Cena Platform -- Personalized Explanation Service (SAI-003 / Task 03)
// L3 personalized explanation generation with full student context.
//
// Assembles: mastery, scaffolding, methodology, confusion state, behavioral
// signals (backspace, answer changes). Methodology-aware prompts: Socratic
// asks questions, WorkedExample shows steps, Feynman challenges articulation.
//
// Scaffolding-depth mapping:
//   Full (mastery < 0.20)     -> complete worked example
//   Partial (mastery < 0.40)  -> acknowledge what's right, fill the gap
//   HintsOnly (mastery < 0.70)-> brief pointer, no full solution
//   None (mastery >= 0.70)    -> redirect to L2 (do NOT call LLM)
//
// Confusion-aware delivery:
//   ConfusionResolving -> suppress L3, return L2 cached explanation
//   ConfusionStuck     -> upgrade scaffolding one level
//
// Prompt caching: system prompt cached with ephemeral cache_control (5-min TTL).
// Cost guard: daily_output_token_limit 25000 per student.
// Fallback chain: L3 fail -> L2 cache -> L1 static -> generic message.
//
// PRIVACY: No student ID, name, or PII is ever included in LLM prompts.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Actors.Gateway;
using Cena.Actors.Infrastructure;
using Cena.Actors.Mastery;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

// =============================================================================
// CONTEXT RECORD
// =============================================================================

/// <summary>
/// Full student context for L3 personalized explanation generation.
/// All data is available from the student's actor state.
/// </summary>
public sealed record PersonalizedExplanationContext(
    // Question
    string QuestionId,
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    ExplanationErrorType ErrorType,
    string Language,
    string Subject,
    string? StaticExplanation,
    string? DistractorRationale,

    // Mastery state (from StudentActor -> ConceptMasteryState)
    float MasteryProbability,
    int BloomLevel,
    ScaffoldingLevel Scaffolding,
    float PrerequisiteSatisfactionIndex,

    // Methodology (from MethodologyResolver)
    string ActiveMethodology,

    // Affect (from services)
    ConfusionState ConfusionState,
    string? DisengagementType,

    // Behavioral signals (from SubmitAnswer / BusConceptAttempt)
    int BackspaceCount,
    int AnswerChangeCount,
    int HintsUsed,
    int ResponseTimeMs,
    double MedianResponseTimeMs,

    // Question difficulty (0.0-1.0 from PublishedQuestion.Difficulty)
    float? QuestionDifficulty = null,

    // Student budget key (hashed, not PII)
    string StudentBudgetKey = "");

// =============================================================================
// RESULT RECORD
// =============================================================================

/// <summary>
/// Result of personalized explanation resolution. Includes the tier that served
/// the explanation for observability.
/// </summary>
public sealed record PersonalizedExplanationResult(
    string Text,
    string Tier,         // "L3", "L2", "L1", "generic"
    int OutputTokens,
    string? ModelId);

// =============================================================================
// INTERFACE
// =============================================================================

/// <summary>
/// Generates L3 personalized explanations incorporating full student context.
/// Implements the fallback chain: L3 -> L2 cache -> L1 static -> generic.
/// </summary>
public interface IPersonalizedExplanationService
{
    /// <summary>
    /// Resolve a personalized explanation for the student's wrong answer.
    /// Respects confusion gates, scaffolding redirects, and daily token budgets.
    /// </summary>
    Task<PersonalizedExplanationResult> ResolveAsync(
        PersonalizedExplanationContext context, CancellationToken ct);
}

// =============================================================================
// IMPLEMENTATION
// =============================================================================

// ADR-0026: L3 personalized explanations are Sonnet-grade (complex reasoning
// over full student context). Primary model is claude_sonnet_4_6 per
// contracts/llm/routing-config.yaml §2 (task_routing.answer_evaluation).
[TaskRouting("tier3", "answer_evaluation")]
public sealed class PersonalizedExplanationService : IPersonalizedExplanationService
{
    private const string GenericFallback =
        "Review this concept and try again.";

    // routing-config.yaml section 2: answer_evaluation -> Sonnet 4.6, temp 0.3
    private const string SonnetModelId = "claude-sonnet-4-6-20260215";
    private const float ExplanationTemperature = 0.3f;

    // routing-config.yaml section 4: daily_output_token_limit per student
    private const int DailyOutputTokenLimit = 25_000;

    // Per-student daily token tracking: key = "student:{budgetKey}:{yyyy-MM-dd}"
    private static readonly ConcurrentDictionary<string, int> s_dailyTokens = new();

    private readonly ILlmClient _llm;
    private readonly IExplanationCacheService _cache;
    private readonly ILogger<PersonalizedExplanationService> _logger;
    private readonly IClock _clock;
    private readonly Histogram<double> _latencyHistogram;
    private readonly Counter<long> _generationCounter;
    private readonly Counter<long> _budgetExhaustedCounter;
    private readonly Counter<long> _confusionSuppressedCounter;

    public PersonalizedExplanationService(
        ILlmClient llm,
        IExplanationCacheService cache,
        ILogger<PersonalizedExplanationService> logger,
        IMeterFactory meterFactory,
        IClock clock)
    {
        _llm = llm;
        _cache = cache;
        _logger = logger;
        _clock = clock;

        var meter = meterFactory.Create("Cena.Actors.PersonalizedExplanation", "1.0.0");
        _latencyHistogram = meter.CreateHistogram<double>(
            "cena.personalized_explanation.latency_ms",
            unit: "ms",
            description: "L3 personalized explanation generation latency");
        _generationCounter = meter.CreateCounter<long>(
            "cena.personalized_explanation.total",
            description: "Total personalized explanation attempts by tier");
        _budgetExhaustedCounter = meter.CreateCounter<long>(
            "cena.personalized_explanation.budget_exhausted_total",
            description: "Times daily token budget forced L2 fallback");
        _confusionSuppressedCounter = meter.CreateCounter<long>(
            "cena.personalized_explanation.confusion_suppressed_total",
            description: "Times ConfusionResolving suppressed L3");
    }

    public async Task<PersonalizedExplanationResult> ResolveAsync(
        PersonalizedExplanationContext ctx, CancellationToken ct)
    {
        // ── Gate 1: ScaffoldingLevel.None -> redirect to L2 (do NOT call LLM) ──
        if (ctx.Scaffolding == ScaffoldingLevel.None)
        {
            _logger.LogDebug(
                "L3 skipped for question {QuestionId}: ScaffoldingLevel=None (mastery >= 0.70). " +
                "Redirecting to L2.",
                ctx.QuestionId);
            return await FallbackToL2Async(ctx, ct);
        }

        // ── Gate 2: ConfusionResolving -> suppress L3, serve L2 only ──
        if (ctx.ConfusionState == ConfusionState.ConfusionResolving)
        {
            _confusionSuppressedCounter.Add(1);
            _logger.LogDebug(
                "L3 suppressed for question {QuestionId}: student is in productive confusion " +
                "(ConfusionResolving). Serving L2 to avoid disrupting resolution.",
                ctx.QuestionId);
            return await FallbackToL2Async(ctx, ct);
        }

        // ── Gate 3: Daily token budget check ──
        var budgetKey = BuildBudgetKey(ctx.StudentBudgetKey, _clock.UtcDateTime);
        var currentUsage = s_dailyTokens.GetValueOrDefault(budgetKey, 0);
        if (currentUsage >= DailyOutputTokenLimit)
        {
            _budgetExhaustedCounter.Add(1);
            _logger.LogInformation(
                "L3 budget exhausted for question {QuestionId} ({Current}/{Limit} tokens). " +
                "Falling back to L2.",
                ctx.QuestionId, currentUsage, DailyOutputTokenLimit);
            return await FallbackToL2Async(ctx, ct);
        }

        // ── Scaffolding upgrade for ConfusionStuck ──
        var effectiveScaffolding = ctx.Scaffolding;
        if (ctx.ConfusionState == ConfusionState.ConfusionStuck)
        {
            effectiveScaffolding = UpgradeScaffolding(ctx.Scaffolding);
            _logger.LogDebug(
                "L3 scaffolding upgraded from {Original} to {Effective} for question {QuestionId}: " +
                "student is ConfusionStuck.",
                ctx.Scaffolding, effectiveScaffolding, ctx.QuestionId);
        }

        // ── L3: Generate personalized explanation ──
        var sw = Stopwatch.StartNew();
        try
        {
            var systemPrompt = BuildSystemPrompt(ctx, effectiveScaffolding);
            var userPrompt = BuildUserPrompt(ctx, effectiveScaffolding);
            var maxTokens = DetermineMaxTokens(
                effectiveScaffolding, ctx.BloomLevel, ctx.QuestionDifficulty, ctx.MasteryProbability);

            var llmRequest = new LlmRequest(
                SystemPrompt: systemPrompt,
                UserPrompt: userPrompt,
                Temperature: ExplanationTemperature,
                MaxTokens: maxTokens,
                ModelId: SonnetModelId,
                CacheSystemPrompt: true);  // routing-config section 6: 5-min TTL

            var response = await _llm.CompleteAsync(llmRequest, ct);
            sw.Stop();

            // Track token usage against daily budget
            s_dailyTokens.AddOrUpdate(budgetKey,
                response.OutputTokens,
                (_, prev) => prev + response.OutputTokens);

            _latencyHistogram.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("model_id", response.ModelId));
            _generationCounter.Add(1,
                new KeyValuePair<string, object?>("tier", "L3"));

            _logger.LogDebug(
                "L3 generated for question {QuestionId}, model={ModelId}, tokens={Tokens}, " +
                "latency={LatencyMs}ms, methodology={Methodology}, scaffolding={Scaffolding}",
                ctx.QuestionId, response.ModelId, response.OutputTokens,
                sw.ElapsedMilliseconds, ctx.ActiveMethodology, effectiveScaffolding);

            // L3 explanations are ephemeral -- do NOT cache in Redis.
            return new PersonalizedExplanationResult(
                response.Content, "L3", response.OutputTokens, response.ModelId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "L3 generation failed for question {QuestionId} after {LatencyMs}ms. " +
                "Falling back to L2/L1/generic.",
                ctx.QuestionId, sw.ElapsedMilliseconds);
        }

        // ── Fallback: L2 -> L1 -> generic ──
        return await FallbackToL2Async(ctx, ct);
    }

    // =========================================================================
    // FALLBACK CHAIN: L2 cache -> L1 static -> generic
    // =========================================================================

    private async Task<PersonalizedExplanationResult> FallbackToL2Async(
        PersonalizedExplanationContext ctx, CancellationToken ct)
    {
        // L2: Redis cache by (questionId, errorType, language)
        try
        {
            var cached = await _cache.GetAsync(
                ctx.QuestionId, ctx.ErrorType, ctx.Language, ct);

            if (cached is not null)
            {
                _generationCounter.Add(1,
                    new KeyValuePair<string, object?>("tier", "L2"));
                return new PersonalizedExplanationResult(
                    cached.Text, "L2", cached.TokenCount, cached.ModelId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "L2 cache lookup failed for question {QuestionId}. Continuing to L1.",
                ctx.QuestionId);
        }

        // L1: Static explanation from question bank
        if (!string.IsNullOrWhiteSpace(ctx.StaticExplanation))
        {
            _generationCounter.Add(1,
                new KeyValuePair<string, object?>("tier", "L1"));
            return new PersonalizedExplanationResult(
                ctx.StaticExplanation, "L1", 0, null);
        }

        // Generic: never leave the student with nothing
        _logger.LogWarning(
            "All explanation tiers exhausted for question {QuestionId}. Using generic fallback.",
            ctx.QuestionId);
        _generationCounter.Add(1,
            new KeyValuePair<string, object?>("tier", "generic"));
        return new PersonalizedExplanationResult(
            GenericFallback, "generic", 0, null);
    }

    // =========================================================================
    // SYSTEM PROMPT (methodology + scaffolding + language + confusion)
    // Cached via cache_control: { type: "ephemeral" } (5-min TTL)
    // =========================================================================

    private static string BuildSystemPrompt(
        PersonalizedExplanationContext ctx, ScaffoldingLevel scaffolding)
    {
        var lang = MapLanguage(ctx.Language);
        var methodologyInstruction = MapMethodology(ctx.ActiveMethodology);
        var scaffoldingInstruction = MapScaffolding(scaffolding);
        var depthInstruction = MapBloomsDepth(ctx.BloomLevel);

        return $"""
            You are an expert {ctx.Subject} tutor. Respond ONLY in {lang}.

            METHODOLOGY: {methodologyInstruction}
            DEPTH: {depthInstruction}
            SCAFFOLDING: {scaffoldingInstruction}

            RULES:
            - Address the specific misconception revealed by the student's answer.
            - Never mention the student's identity, name, or any personal information.
            - Be encouraging but precise.
            - Use mathematical notation where appropriate.
            - Keep the explanation focused on exactly one concept.
            - Adjust your language complexity to match the student's demonstrated level.
            """;
    }

    // =========================================================================
    // USER PROMPT (question + mastery + behavioral signals)
    // NOT cached -- changes every question.
    // =========================================================================

    private static string BuildUserPrompt(
        PersonalizedExplanationContext ctx, ScaffoldingLevel scaffolding)
    {
        var parts = new List<string>(16);

        // ── Question context ──
        parts.Add($"QUESTION: {ctx.QuestionStem}");
        parts.Add($"CORRECT ANSWER: {ctx.CorrectAnswer}");
        parts.Add($"STUDENT'S ANSWER: {ctx.StudentAnswer}");
        parts.Add($"ERROR TYPE: {ctx.ErrorType}");

        if (!string.IsNullOrEmpty(ctx.DistractorRationale))
            parts.Add($"WHY STUDENT CHOSE THIS: {ctx.DistractorRationale}");

        // ── Mastery context (no PII) ──
        parts.Add($"CONCEPT MASTERY: {ctx.MasteryProbability:P0}");
        parts.Add($"PREREQUISITE READINESS (PSI): {ctx.PrerequisiteSatisfactionIndex:P0}");

        // ── Behavioral signals (indicate hesitation/confusion -- no PII) ──
        if (ctx.BackspaceCount > 3)
            parts.Add(
                "NOTE: Student showed significant hesitation while answering " +
                "(reconsidered their response multiple times).");

        if (ctx.AnswerChangeCount > 1)
            parts.Add(
                $"NOTE: Student changed their answer {ctx.AnswerChangeCount} times, " +
                "indicating uncertainty about the concept.");

        if (ctx.HintsUsed > 0)
            parts.Add($"NOTE: Student used {ctx.HintsUsed} hint(s) before answering.");

        // ── Response time relative to baseline ──
        if (ctx.MedianResponseTimeMs > 0 && ctx.ResponseTimeMs > 0)
        {
            double ratio = ctx.ResponseTimeMs / ctx.MedianResponseTimeMs;
            if (ratio > 2.0)
                parts.Add("NOTE: Student took significantly longer than usual on this question.");
            else if (ratio < 0.5)
                parts.Add("NOTE: Student answered very quickly (may not have fully engaged).");
        }

        // ── Difficulty-gap awareness: stretch challenge vs regression ──
        if (ctx.QuestionDifficulty.HasValue)
        {
            var diffGap = ctx.QuestionDifficulty.Value - ctx.MasteryProbability;
            if (diffGap > 0.25f)
                parts.Add(
                    $"CONTEXT: This was a stretch question (difficulty {ctx.QuestionDifficulty.Value:F1}, " +
                    $"mastery {ctx.MasteryProbability:P0}). " +
                    "Acknowledge that this was challenging. Be encouraging — " +
                    "\"this was a hard one, let's break it down together.\"");
            else if (diffGap < -0.20f)
                parts.Add(
                    $"CONTEXT: This question should have been within the student's ability " +
                    $"(difficulty {ctx.QuestionDifficulty.Value:F1}, mastery {ctx.MasteryProbability:P0}). " +
                    "Investigate what went wrong — this may indicate a gap " +
                    "in foundational understanding rather than challenge level.");
        }

        // ── Low PSI: prerequisite gaps ──
        if (ctx.PrerequisiteSatisfactionIndex < 0.5f)
            parts.Add(
                "IMPORTANT: Student has weak prerequisite knowledge. " +
                "Start the explanation from foundational concepts.");

        // ── ConfusionStuck: extra scaffolding instruction ──
        if (ctx.ConfusionState == ConfusionState.ConfusionStuck)
            parts.Add(
                "IMPORTANT: Student has been confused for several questions without resolution. " +
                "Provide a very clear, step-by-step explanation. " +
                "Start from the most basic relevant principle.");

        // ── Disengagement awareness ──
        if (ctx.DisengagementType is "Fatigued_Cognitive" or "Fatigued_Motor")
            parts.Add(
                "IMPORTANT: Student is fatigued. Keep explanation BRIEF (2-3 sentences max). " +
                "Focus on the single most important point.");

        parts.Add("Generate the explanation now.");

        return string.Join("\n\n", parts);
    }

    // =========================================================================
    // METHODOLOGY MAPPING (9 methodologies per task spec)
    // =========================================================================

    internal static string MapMethodology(string methodology) =>
        methodology.ToLowerInvariant() switch
        {
            "socratic" =>
                "Use the Socratic method. Ask a guiding question that leads the student " +
                "to discover the error. NEVER reveal the answer.",

            "workedexample" or "worked_example" or "worked-example" =>
                "Show a step-by-step solution to a similar problem. " +
                "Let the student apply the pattern.",

            "feynman" =>
                "Ask the student to explain the concept. " +
                "Point out where their explanation breaks.",

            "analogy" =>
                "Explain this concept through a comparison to a prerequisite concept " +
                "the student already understands.",

            "retrievalpractice" or "retrieval_practice" or "retrieval-practice" =>
                "Before explaining, ask: 'What do you remember about this concept?' " +
                "Then fill the gaps in their recall.",

            "drillandpractice" or "drill_and_practice" or "drill-and-practice" =>
                "Provide a brief, direct correction. State what went wrong and the " +
                "correct approach in 1-2 concise sentences.",

            "directinstruction" or "direct_instruction" or "direct-instruction" =>
                "Explain the error clearly: what went wrong, why, and the correct approach.",

            "spacedrepetition" or "spaced_repetition" or "spaced-repetition" =>
                "Provide a concise review-focused explanation. Highlight the key fact " +
                "or rule the student needs to recall. Use mnemonics if appropriate.",

            "bloomsprogression" or "blooms_progression" or "blooms-progression" =>
                "Match explanation depth to the Bloom's level being tested. " +
                "Lower levels get definitions, higher levels get analytical comparisons.",

            // Default: Direct instruction tone
            _ => "Explain the error clearly: what went wrong, why, and the correct approach."
        };

    // =========================================================================
    // SCAFFOLDING-DEPTH MAPPING
    // =========================================================================

    internal static string MapScaffolding(ScaffoldingLevel level) => level switch
    {
        ScaffoldingLevel.Full =>
            "Provide a COMPLETE worked example. Show every step from start to finish " +
            "with clear reasoning at each transition.",

        ScaffoldingLevel.Partial =>
            "Acknowledge what the student got right. Then explain the specific gap: " +
            "what step they missed and how to correct it.",

        ScaffoldingLevel.HintsOnly =>
            "Give ONE brief pointer toward the correct approach. " +
            "Do NOT reveal the full solution.",

        ScaffoldingLevel.None =>
            "Student is at an independent level. Provide minimal feedback only.",

        _ => "Adjust explanation depth to match the student's level."
    };

    // =========================================================================
    // SCAFFOLDING UPGRADE (ConfusionStuck -> next level up)
    // =========================================================================

    internal static ScaffoldingLevel UpgradeScaffolding(ScaffoldingLevel current) =>
        current switch
        {
            ScaffoldingLevel.HintsOnly => ScaffoldingLevel.Partial,
            ScaffoldingLevel.Partial => ScaffoldingLevel.Full,
            // Full stays Full; None should not reach here (gated above)
            _ => current
        };

    // =========================================================================
    // BLOOM'S DEPTH CALIBRATION
    // =========================================================================

    private static string MapBloomsDepth(int bloomsLevel) => bloomsLevel switch
    {
        1 or 2 => "SIMPLE: Use plain language. One key point. A concrete example if helpful.",
        3 or 4 => "MODERATE: Explain the underlying principle. Connect to related concepts.",
        5 or 6 => "ANALYTICAL: Encourage deeper reasoning. Compare approaches. Ask 'why' not just 'what'.",
        _ => "MODERATE: Explain the underlying principle clearly."
    };

    // =========================================================================
    // LANGUAGE MAPPING
    // =========================================================================

    private static string MapLanguage(string language) => language.ToLowerInvariant() switch
    {
        "he" => "Hebrew (\u05E2\u05D1\u05E8\u05D9\u05EA)",
        "ar" => "Arabic (\u0627\u0644\u0639\u0631\u0628\u064A\u0629)",
        "en" => "English",
        _ => "Hebrew (\u05E2\u05D1\u05E8\u05D9\u05EA)"
    };

    // =========================================================================
    // TOKEN BUDGET
    // =========================================================================

    /// <summary>
    /// Scaffolding-based max tokens: Full=600, Partial=400, HintsOnly=200.
    /// Bloom's level can increase the cap for analytical questions.
    /// Difficulty frame adjusts: stretch +50%, regression -30%.
    /// </summary>
    internal static int DetermineMaxTokens(
        ScaffoldingLevel scaffolding, int bloomsLevel, float? questionDifficulty = null, float masteryProbability = 0.5f)
    {
        var baseTokens = scaffolding switch
        {
            ScaffoldingLevel.Full => 600,
            ScaffoldingLevel.Partial => 400,
            ScaffoldingLevel.HintsOnly => 200,
            _ => 300
        };

        // Higher Bloom's levels get more room for analytical depth
        if (bloomsLevel >= 5)
            baseTokens = (int)(baseTokens * 1.3);

        // Difficulty-aware scaling: stretch questions deserve longer explanations,
        // regression questions need shorter, more targeted responses.
        if (questionDifficulty.HasValue)
        {
            var gap = DifficultyGap.Compute(questionDifficulty.Value, masteryProbability);
            var frame = DifficultyGap.Classify(gap);
            baseTokens = DifficultyGap.AdjustMaxTokens(baseTokens, frame);
        }

        return baseTokens;
    }

    // =========================================================================
    // BUDGET KEY
    // =========================================================================

    private static string BuildBudgetKey(string studentBudgetKey, DateTime now) =>
        $"l3:{studentBudgetKey}:{now:yyyy-MM-dd}";

    // =========================================================================
    // TEST HELPERS (internal for unit tests)
    // =========================================================================

    /// <summary>
    /// Returns current daily token usage for a student. For testing and monitoring.
    /// </summary>
    internal int GetDailyTokenUsage(string studentBudgetKey)
    {
        var key = BuildBudgetKey(studentBudgetKey, _clock.UtcDateTime);
        return s_dailyTokens.GetValueOrDefault(key, 0);
    }

    /// <summary>
    /// Resets daily token tracking. For testing only.
    /// </summary>
    internal static void ResetDailyTokenTracking() => s_dailyTokens.Clear();
}
