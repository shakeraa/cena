// =============================================================================
// Cena Platform — Claude Tutor LLM Service (HARDEN TutorEndpoints + prr-012)
// Real Anthropic Claude integration with simulated streaming, now gated by
// SocraticCallBudget (3-call/session cap) and DailyTutorTimeBudget (30-min/day
// per student). On cap-hit, routes the turn through StaticHintLadderFallback
// instead of calling Anthropic — no LLM, zero per-turn cost.
//
// prr-012 (finops lens, 2026-04-20 pre-release review):
//   Default Sonnet routing at 10k students × 5 problems/hr × 3 turns ≈ 150k
//   calls/hr ≈ $480k/mo vs the $30k global cap (16× overrun). Hard-capping per
//   session collapses projected spend to ~$25k/mo.
//
// SAI-003 L2 cache (IExplanationCacheService) reuse is scoped out of this
// phase — the cache is keyed by (questionId, errorType, language) and the
// TutorContext currently doesn't carry those fields. See TODO(prr-047) below
// to thread them through once the StuckClassifier output reaches this seam.
// =============================================================================

using Anthropic;
using Anthropic.Models.Messages;
using Cena.Actors.RateLimit;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Cena.Actors.Tutor;

/// <summary>
/// Production-grade Anthropic Claude integration for AI tutoring.
/// Uses the official Anthropic SDK (v12.9.0).
/// </summary>
// ADR-0026: Socratic tutoring is the canonical tier-3 (Sonnet) path. Primary
// model is claude_sonnet_4_6 per contracts/llm/routing-config.yaml §2
// (task_routing.socratic_question).
// prr-046: finops cost-center "socratic" — highest-volume student-facing path;
// the bulk of projected tier-3 spend lives here.
[TaskRouting("tier3", "socratic_question")]
[FeatureTag("socratic")]
public sealed class ClaudeTutorLlmService : ITutorLlmService
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly string _systemPromptTemplate;
    private readonly ISocraticCallBudget _callBudget;
    private readonly IStaticHintLadderFallback _staticFallback;
    private readonly IDailyTutorTimeBudget _dailyBudget;
    private readonly ILogger<ClaudeTutorLlmService> _logger;
    private readonly ILlmCostMetric _costMetric;

    /// <summary>
    /// Synthetic model id emitted when a turn is served from the static hint
    /// ladder rather than the LLM. Keeps telemetry readable downstream.
    /// </summary>
    public const string StaticFallbackModelId = "cena-static-hint-ladder-v1";

    /// <summary>
    /// Synthetic model id emitted when a turn is refused because the student
    /// hit the daily 30-minute cap.
    /// </summary>
    public const string DailyCapModelId = "cena-daily-cap-rest-v1";

    public ClaudeTutorLlmService(
        IConfiguration configuration,
        ISocraticCallBudget callBudget,
        IStaticHintLadderFallback staticFallback,
        IDailyTutorTimeBudget dailyBudget,
        ILogger<ClaudeTutorLlmService> logger,
        ILlmCostMetric costMetric)
    {
        _logger = logger;
        _callBudget = callBudget;
        _staticFallback = staticFallback;
        _dailyBudget = dailyBudget;
        _costMetric = costMetric;

        var apiKey = configuration["Cena:Llm:ApiKey"]
            ?? throw new InvalidOperationException("Cena:Llm:ApiKey is required for ClaudeTutorLlmService");

        _client = new AnthropicClient { ApiKey = apiKey };
        _model = configuration["Cena:Llm:Model"] ?? "claude-sonnet-4-6";
        _systemPromptTemplate = configuration["Cena:Llm:SystemPromptTemplate"]
            ?? GetDefaultSystemPrompt();

        _logger.LogInformation("ClaudeTutorLlmService initialized with model: {Model}", _model);
    }

    public async IAsyncEnumerable<LlmChunk> StreamCompletionAsync(
        TutorContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var turnStopwatch = Stopwatch.StartNew();

        // ── prr-012 gate 1: daily tutor-time cap ──
        // 30-min/student/day hard stop (tenant-overridable per prr-048).
        // On cap-hit, return the "take a break" response without touching any
        // LLM or static ladder. instituteId is threaded through for per-tenant
        // cap override + per-tenant metric labels (cena_student_daily_*).
        var dailyCheck = await _dailyBudget.CheckAsync(
            context.StudentId, context.InstituteId, ct);
        if (!dailyCheck.Allowed)
        {
            yield return new LlmChunk(
                Delta: DailyTutorTimeBudget.TakeBreakMessage,
                Finished: false,
                TokensUsed: null,
                Model: DailyCapModelId);
            yield return new LlmChunk(
                Delta: "",
                Finished: true,
                TokensUsed: 0,
                Model: DailyCapModelId);
            yield break;
        }

        // ── prr-012 gate 2: Socratic LLM budget (3 calls/session) ──
        // TODO(prr-047): before falling through to the LLM, consult the SAI-003
        // IExplanationCacheService with (questionId, errorType, language). The
        // TutorContext currently lacks those fields — the StuckClassifier
        // output needs to be wired through TutorMessageService first.
        // Cache key template (for reference): cena:explain:{questionId}:{errorType}:{language}
        var canCall = await _callBudget.CanMakeLlmCallAsync(context.ThreadId, ct);
        if (!canCall)
        {
            // Cap hit → static hint ladder, no LLM. Fallback index = how many
            // prior fallback turns this session has already shown. We use the
            // post-cap count offset (count - cap) so the first fallback turn
            // shows L1, the second L2, etc.
            var count = await _callBudget.GetCallCountAsync(context.ThreadId, ct);
            var fallbackIndex = (int)Math.Max(0, count - SocraticCallBudget.MaxLlmCallsPerSession);

            var hint = _staticFallback.GetHint(context, fallbackIndex);
            // Record the fallback as a "call" so repeated fallback turns
            // advance the ladder (index increments on each turn).
            await _callBudget.RecordLlmCallAsync(context.ThreadId, ct);

            yield return new LlmChunk(
                Delta: hint.Text,
                Finished: false,
                TokensUsed: null,
                Model: StaticFallbackModelId);
            yield return new LlmChunk(
                Delta: "",
                Finished: true,
                TokensUsed: 0,
                Model: StaticFallbackModelId);

            turnStopwatch.Stop();
            await _dailyBudget.RecordUsageAsync(
                context.StudentId,
                (int)turnStopwatch.Elapsed.TotalSeconds,
                context.InstituteId,
                ct);
            yield break;
        }

        // ── Budget OK → real LLM call ──
        var systemPrompt = BuildSystemPrompt(context);
        var messages = BuildMessages(context);

        _logger.LogDebug("Streaming completion for student {StudentId}, thread {ThreadId}",
            context.StudentId, context.ThreadId);

        string fullText;
        int? totalTokens;
        bool hasError = false;

        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 1024,
                System = systemPrompt,
                Temperature = 0.7,
                Messages = messages
            }, ct);

            fullText = string.Join("", response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(b => b.Text));

            var inputTokens = response.Usage?.InputTokens ?? 0;
            var outputTokens = response.Usage?.OutputTokens ?? 0;
            totalTokens = (int)(inputTokens + outputTokens);

            // prr-046: per-feature cost tag on success path. institute_id is
            // intentionally omitted — TutorContext does not thread tenant
            // scope through yet; the metric falls back to "unknown" so the
            // global socratic spend is still counted. See prr-084 follow-up.
            _costMetric.Record(
                feature: "socratic",
                tier: "tier3",
                task: "socratic_question",
                modelId: _model,
                inputTokens: inputTokens,
                outputTokens: outputTokens);

            // Only record budget after a successful LLM response — failed
            // attempts do not consume the 3-call session cap.
            await _callBudget.RecordLlmCallAsync(context.ThreadId, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming cancelled for thread {ThreadId}", context.ThreadId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming from Claude for thread {ThreadId}", context.ThreadId);
            fullText = "I'm sorry, I encountered an error. Please try again.";
            totalTokens = null;
            hasError = true;
        }

        if (hasError)
        {
            yield return new LlmChunk(
                Delta: fullText,
                Finished: true,
                TokensUsed: null,
                Model: _model);
            turnStopwatch.Stop();
            await _dailyBudget.RecordUsageAsync(
                context.StudentId,
                (int)turnStopwatch.Elapsed.TotalSeconds,
                context.InstituteId,
                ct);
            yield break;
        }

        // Simulate streaming by yielding word by word
        var words = fullText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            yield return new LlmChunk(
                Delta: word + " ",
                Finished: false,
                TokensUsed: null,
                Model: _model);
            await Task.Delay(20, ct); // Small delay for natural feel
        }

        // Final chunk with token usage
        yield return new LlmChunk(
            Delta: "",
            Finished: true,
            TokensUsed: totalTokens,
            Model: _model);

        turnStopwatch.Stop();
        await _dailyBudget.RecordUsageAsync(
            context.StudentId,
            (int)turnStopwatch.Elapsed.TotalSeconds,
            context.InstituteId,
            ct);
    }

    private string BuildSystemPrompt(TutorContext context)
    {
        var gradeInfo = context.CurrentGrade.HasValue
            ? $"grade {context.CurrentGrade.Value}"
            : "their current level";

        var subjectInfo = !string.IsNullOrEmpty(context.Subject)
            ? context.Subject
            : "the subject at hand";

        return _systemPromptTemplate
            .Replace("{{Grade}}", gradeInfo)
            .Replace("{{Subject}}", subjectInfo);
    }

    private List<MessageParam> BuildMessages(TutorContext context)
    {
        var messages = new List<MessageParam>();

        // Add conversation history (last 10 messages for context window)
        foreach (var msg in context.MessageHistory.TakeLast(10))
        {
            var role = msg.Role switch
            {
                "user" => "user",
                "assistant" => "assistant",
                _ => "user"
            };

            messages.Add(new MessageParam { Role = role, Content = msg.Content });
        }

        return messages;
    }

    private static string GetDefaultSystemPrompt()
    {
        return """
        You are a patient, encouraging AI tutor helping a minor student at {{Grade}} level with {{Subject}}.

        SAFETY RULES (MANDATORY):
        - You are tutoring a minor. Never share, request, or store personal information.
        - Never ask for or repeat the student's name, email, school, address, or phone number.
        - If the student shares personal information, do not repeat it back.
        - If the student expresses distress, emotional pain, or mentions self-harm, respond with empathy and suggest they talk to a trusted adult such as a parent, teacher, or school counsellor.
        - Never provide medical, legal, or psychiatric advice.
        - Stay on the educational topic. If the conversation goes off-topic, gently redirect.

        Guidelines:
        - Use the Socratic method: guide students to answers through questions, don't just give answers
        - Adapt explanations to the student's grade level
        - Be encouraging and supportive, especially when students struggle
        - Break complex problems into smaller, manageable steps
        - Use analogies and examples appropriate to the subject and age
        - If a student is stuck, offer hints rather than solutions
        - Celebrate progress and effort, not just correct answers
        - Keep responses concise but thorough (2-4 paragraphs typically)

        Current context: Tutoring session for {{Subject}}.
        """;
    }
}
