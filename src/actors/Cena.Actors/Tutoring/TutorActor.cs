// =============================================================================
// Cena Platform -- TutorActor (SAI-07: Conversational Tutoring)
// Proto.Actor classic child actor for multi-turn tutoring dialogue.
// Created by LearningSessionActor when tutoring is triggered, destroyed on end.
//
// 4 entry points: confusion annotation, question annotation,
//                 ConfusionStuck auto-trigger, post-wrong-answer follow-up.
// RAG: IContentRetriever (pgvector) injects top 3 passages per turn.
// Budget: daily_output_token_limit 25000, fallback to L2 cached explanations.
// Guardrails: 10-turn cap, 5-min ReceiveTimeout, off-topic redirect, safety.
// Methodology gate: DrillAndPractice/SpacedRepetition refuse tutoring.
// Ephemeral conversation -- NOT event-sourced, dies with session.
// =============================================================================

using System.Collections.Concurrent;
using Cena.Actors.Gateway;
using Cena.Actors.Infrastructure;
using Cena.Actors.Services;
using Cena.Actors.Sessions;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Tutoring;

// ── Messages ──

/// <summary>Start tutoring from a confusion annotation (kind='confusion').</summary>
public sealed record StartTutoringFromConfusion(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Subject,
    string Language,
    string Methodology,
    double ConceptMastery,
    int BloomsLevel,
    float QuestionDifficulty = 0f);

/// <summary>Start tutoring from a question annotation (kind='question').</summary>
public sealed record StartTutoringFromQuestion(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Subject,
    string Language,
    string Methodology,
    double ConceptMastery,
    int BloomsLevel,
    string StudentQuestion,
    float QuestionDifficulty = 0f);

/// <summary>Auto-triggered tutoring from ConfusionStuck detection.</summary>
public sealed record StartTutoringFromConfusionStuck(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Subject,
    string Language,
    string Methodology,
    double ConceptMastery,
    int BloomsLevel,
    float QuestionDifficulty = 0f);

/// <summary>Post-wrong-answer follow-up: offer "want to discuss further?".</summary>
public sealed record StartTutoringPostWrongAnswer(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Subject,
    string Language,
    string Methodology,
    double ConceptMastery,
    int BloomsLevel,
    string ErrorType,
    string QuestionId,
    float QuestionDifficulty = 0f);

/// <summary>Student sends a message during tutoring dialogue.</summary>
public sealed record TutorMessage(string StudentMessage);

/// <summary>Response from the tutor to the student.</summary>
public sealed record TutorResponse(
    int TurnNumber,
    string ResponseText,
    bool IsComplete,
    int RemainingTurns);

/// <summary>End the tutoring session.</summary>
public sealed record EndTutoring;

/// <summary>Start tutoring (legacy, kept for backward compatibility).</summary>
public sealed record StartTutoring(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Subject,
    string Language,
    string Methodology,
    double ConceptMastery,
    int BloomsLevel,
    float QuestionDifficulty = 0f);

/// <summary>Sent by TutorActor to parent when methodology rejects tutoring.</summary>
public sealed record TutoringRejected(string Methodology, string Reason);

// ── Actor ──

// ADR-0045: Multi-turn Socratic dialogue via Sonnet with cache-enabled system
// prompt (see GenerateOpeningAsync/GenerateResponseAsync using
// claude-sonnet-4-6-20260215). Canonical tier-3 path. Routing row:
// contracts/llm/routing-config.yaml §task_routing.socratic_question.
//
// prr-047: live conversational tutoring — every turn is conditioned on a
// unique {history, student message} tuple, so a content-level cache would
// yield ~0% hits. The system prompt IS cached via Anthropic's 5-min
// cache_control (routing-config §6.student_context), which is the right
// caching tier for this workload. No Redis-level response cache possible.
// prr-046: finops cost-center "socratic" — TutorActor shares the same
// cost-center as ClaudeTutorLlmService because both are the Socratic
// conversational surface. The SpendPerTurn difference between the two is
// visible via the model_id label on the counter.
// ADR-0046: TutorActor composes the student's free-text message directly into
// its user prompt (see GenerateOpeningAsync / GenerateResponseAsync). Injects
// IPiiPromptScrubber and fails closed to the safety fallback on any scrub
// event. IPiiPromptScrubber is injected; DI wires it from AddPiiPromptScrubber.
[TaskRouting("tier3", "socratic_question")]
[FeatureTag("socratic")]
[AllowsUncachedLlm("Multi-turn dialogue: each turn uniquely conditioned on conversation history and student message. System-prompt cache handled by CacheSystemPrompt flag on LlmRequest.")]
public sealed class TutorActor : IActor
{
    private const int MaxTurns = 10;
    private const int TurnWarningThreshold = 8; // warn at turn 8: "2 more exchanges"
    private const int HistoryWindowSize = 6; // last 3 student + 3 tutor = 6 turns
    private const int DailyOutputTokenLimit = 25_000;
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Methodologies that do not benefit from conversational tutoring.</summary>
    private static readonly HashSet<string> NonTutoringMethodologies = new(StringComparer.OrdinalIgnoreCase)
    {
        "DrillAndPractice", "drill_and_practice", "drill-and-practice",
        "SpacedRepetition", "spaced_repetition", "spaced-repetition"
    };

    private readonly ILlmClient _llm;
    private readonly ITutorPromptBuilder _promptBuilder;
    private readonly ITutorSafetyGuard _safetyGuard;
    private readonly IContentRetriever _contentRetriever;
    private readonly IExplanationCacheService _explanationCache;
    private readonly ILogger<TutorActor> _logger;
    private readonly IClock _clock;
    private readonly ILlmCostMetric _costMetric;
    private readonly IPiiPromptScrubber _piiScrubber;

    // Per-student daily token tracking (shared across all TutorActor instances).
    // Key: "tutor:{studentId}:{yyyy-MM-dd}", Value: cumulative output tokens.
    internal static readonly ConcurrentDictionary<string, int> DailyTokenUsage = new();

    // ── Session state (ephemeral -- dies with actor) ──
    private string _studentId = "";
    private string _sessionId = "";
    private string _tutoringSessionId = "";
    private string _conceptId = "";
    private string _subject = "";
    private string _language = "";
    private string _methodology = "";
    private double _conceptMastery;
    private int _bloomsLevel;
    private int _turnCount;
    private string _triggerType = "confusion";
    private string _errorType = "";
    private string _questionId = "";
    private float _questionDifficulty;
    private readonly List<ConversationTurn> _history = new();
    private DateTimeOffset _startedAt;
    private bool _initialized;

    public TutorActor(
        ILlmClient llm,
        ITutorPromptBuilder promptBuilder,
        ITutorSafetyGuard safetyGuard,
        IContentRetriever contentRetriever,
        IExplanationCacheService explanationCache,
        ILogger<TutorActor> logger,
        IClock clock,
        ILlmCostMetric costMetric,
        IPiiPromptScrubber? piiScrubber = null)
    {
        _llm = llm;
        _promptBuilder = promptBuilder;
        _safetyGuard = safetyGuard;
        _contentRetriever = contentRetriever;
        _explanationCache = explanationCache;
        _logger = logger;
        _clock = clock;
        _costMetric = costMetric;
        // ADR-0046: scrubber defaults to NullPiiPromptScrubber for unit tests
        // that construct TutorActor directly. Production DI wires the real
        // scrubber via AddPiiPromptScrubber().
        _piiScrubber = piiScrubber ?? NullPiiPromptScrubber.Instance;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            StartTutoringFromConfusion msg => OnStartFromConfusion(context, msg),
            StartTutoringFromQuestion msg => OnStartFromQuestion(context, msg),
            StartTutoringFromConfusionStuck msg => OnStartFromConfusionStuck(context, msg),
            StartTutoringPostWrongAnswer msg => OnStartPostWrongAnswer(context, msg),
            StartTutoring msg => OnStartLegacy(context, msg),
            TutorMessage msg => OnTutorMessage(context, msg),
            EndTutoring => OnEndTutoring(context),
            ReceiveTimeout => OnReceiveTimeout(context),
            Stopping => OnStopping(context),
            _ => Task.CompletedTask
        };
    }

    // =========================================================================
    // METHODOLOGY GATE: reject DrillAndPractice / SpacedRepetition
    // =========================================================================

    private bool ShouldRejectMethodology(IContext context, string methodology)
    {
        if (!NonTutoringMethodologies.Contains(methodology))
            return false;

        _logger.LogInformation(
            "Tutoring rejected for methodology {Methodology} -- present next question instead",
            methodology);

        if (context.Parent != null)
            context.Send(context.Parent, new TutoringRejected(methodology,
                "Methodology does not benefit from conversational tutoring"));

        context.Respond(new TutorResponse(0,
            "This methodology works best with practice questions. Let's continue with the next one!",
            true, 0));

        context.Stop(context.Self);
        return true;
    }

    // =========================================================================
    // ENTRY POINT 1: Confusion Annotation (kind='confusion')
    // =========================================================================

    private async Task OnStartFromConfusion(IContext context, StartTutoringFromConfusion msg)
    {
        if (ShouldRejectMethodology(context, msg.Methodology))
            return;

        _questionDifficulty = msg.QuestionDifficulty;

        InitSession(context, msg.StudentId, msg.SessionId, msg.ConceptId, msg.Subject,
            msg.Language, msg.Methodology, msg.ConceptMastery, msg.BloomsLevel, "confusion_annotation");

        DelegateStartedEvent(context);

        // Tutor initiates: ask what the student is confused about
        var openingResponse = await GenerateOpeningAsync(
            "The student has flagged confusion about the current concept. " +
            "Begin by asking what specifically they find confusing.");

        AppendTutorTurn(openingResponse);
        RespondTutoringResponse(context, openingResponse);
    }

    // =========================================================================
    // ENTRY POINT 2: Question Annotation (kind='question')
    // =========================================================================

    private async Task OnStartFromQuestion(IContext context, StartTutoringFromQuestion msg)
    {
        if (ShouldRejectMethodology(context, msg.Methodology))
            return;

        _questionDifficulty = msg.QuestionDifficulty;

        InitSession(context, msg.StudentId, msg.SessionId, msg.ConceptId, msg.Subject,
            msg.Language, msg.Methodology, msg.ConceptMastery, msg.BloomsLevel, "question_annotation");

        DelegateStartedEvent(context);

        // Student's question is the first turn
        _history.Add(new ConversationTurn("student", msg.StudentQuestion, DateTimeOffset.UtcNow));

        DelegateMessageEvent(context, 1, "student", msg.StudentQuestion, 0);

        // Generate tutoring response to the question
        var response = await GenerateResponseAsync(msg.StudentQuestion);
        AppendTutorTurn(response);
        RespondTutoringResponse(context, response);
    }

    // =========================================================================
    // ENTRY POINT 3: ConfusionStuck Auto-Trigger
    // =========================================================================

    private async Task OnStartFromConfusionStuck(IContext context, StartTutoringFromConfusionStuck msg)
    {
        if (ShouldRejectMethodology(context, msg.Methodology))
            return;

        _questionDifficulty = msg.QuestionDifficulty;

        InitSession(context, msg.StudentId, msg.SessionId, msg.ConceptId, msg.Subject,
            msg.Language, msg.Methodology, msg.ConceptMastery, msg.BloomsLevel, "confusion_stuck");

        DelegateStartedEvent(context);

        // Proactive: tutor reaches out
        var openingResponse = await GenerateOpeningAsync(
            "The student appears stuck on this concept for several questions. " +
            "Proactively offer help. Be warm and encouraging. " +
            "Start by acknowledging this is a challenging topic.");

        AppendTutorTurn(openingResponse);
        RespondTutoringResponse(context, openingResponse);
    }

    // =========================================================================
    // ENTRY POINT 4: Post-Wrong-Answer Follow-Up
    // =========================================================================

    private async Task OnStartPostWrongAnswer(IContext context, StartTutoringPostWrongAnswer msg)
    {
        if (ShouldRejectMethodology(context, msg.Methodology))
            return;

        _errorType = msg.ErrorType;
        _questionId = msg.QuestionId;
        _questionDifficulty = msg.QuestionDifficulty;

        InitSession(context, msg.StudentId, msg.SessionId, msg.ConceptId, msg.Subject,
            msg.Language, msg.Methodology, msg.ConceptMastery, msg.BloomsLevel, "post_wrong_answer");

        DelegateStartedEvent(context);

        // Offer follow-up discussion
        var openingResponse = await GenerateOpeningAsync(
            $"The student just answered a question incorrectly (error type: {msg.ErrorType}). " +
            "Offer to discuss the concept further. Be encouraging and non-judgmental. " +
            "Ask if they would like to explore why their approach did not work.");

        AppendTutorTurn(openingResponse);
        RespondTutoringResponse(context, openingResponse);
    }

    // =========================================================================
    // LEGACY ENTRY POINT (backward compatibility)
    // =========================================================================

    private Task OnStartLegacy(IContext context, StartTutoring msg)
    {
        if (ShouldRejectMethodology(context, msg.Methodology))
            return Task.CompletedTask;

        InitSession(context, msg.StudentId, msg.SessionId, msg.ConceptId, msg.Subject,
            msg.Language, msg.Methodology, msg.ConceptMastery, msg.BloomsLevel, "legacy");

        DelegateStartedEvent(context);

        _logger.LogInformation(
            "Tutoring session started (legacy): session={SessionId} concept={ConceptId}",
            _sessionId, _conceptId);

        return Task.CompletedTask;
    }

    // =========================================================================
    // STUDENT MESSAGE HANDLING
    // =========================================================================

    private async Task OnTutorMessage(IContext context, TutorMessage msg)
    {
        if (!_initialized)
        {
            _logger.LogWarning("TutorMessage received before initialization, ignoring");
            context.Respond(new TutorResponse(0, "Session not initialized.", true, 0));
            return;
        }

        // Reset inactivity timer on each message
        context.SetReceiveTimeout(InactivityTimeout);

        _turnCount++;

        // Guardrail: hard cap at 10 turns
        if (_turnCount > MaxTurns)
        {
            _logger.LogInformation("Max turns ({MaxTurns}) reached for session {SessionId}",
                MaxTurns, _sessionId);

            var capMessage = _language == "he"
                ? "כיסינו הרבה חומר! בוא ננסה שאלה חדשה כדי לתרגל את מה שדיברנו עליו."
                : _language == "ar"
                    ? "لقد غطينا الكثير! لنجرب سؤالاً جديداً لممارسة ما ناقشناه."
                    : "Let's try a new question to practice what we discussed.";

            DelegateEndedEvent(context, "turn_limit");

            context.Respond(new TutorResponse(_turnCount, capMessage, true, 0));
            return;
        }

        // Record student turn
        _history.Add(new ConversationTurn("student", msg.StudentMessage, DateTimeOffset.UtcNow));
        DelegateMessageEvent(context, _turnCount, "student", msg.StudentMessage, 0);

        // Generate and respond
        var response = await GenerateResponseAsync(msg.StudentMessage);

        // Turn-8 warning: prepend approaching-limit notice
        var remaining = MaxTurns - _turnCount;
        if (_turnCount == TurnWarningThreshold)
        {
            var warning = _language == "he"
                ? $"נשארו לנו עוד {remaining} חילופי דברים. הנה סיכום של מה שכיסינו:\n\n"
                : _language == "ar"
                    ? $"لدينا {remaining} تبادلات أخرى. إليك ملخص لما ناقشناه:\n\n"
                    : $"We have {remaining} more exchanges. Let me summarize what we've covered.\n\n";
            response = warning + response;
        }

        AppendTutorTurn(response);

        DelegateMessageEvent(context, _turnCount, "tutor", response,
            _history.Count(h => h.Role == "tutor"));

        var isComplete = remaining <= 0;

        if (isComplete)
            DelegateEndedEvent(context, "turn_limit");

        context.Respond(new TutorResponse(_turnCount, response, isComplete, Math.Max(0, remaining)));
    }

    // =========================================================================
    // RECEIVE TIMEOUT: 5-minute inactivity graceful end
    // =========================================================================

    private Task OnReceiveTimeout(IContext context)
    {
        if (!_initialized)
            return Task.CompletedTask;

        _logger.LogInformation(
            "Tutoring session timed out after {Timeout} for session {SessionId}",
            InactivityTimeout, _sessionId);

        DelegateEndedEvent(context, "timeout");

        _initialized = false;
        context.CancelReceiveTimeout();
        context.Stop(context.Self);
        return Task.CompletedTask;
    }

    // =========================================================================
    // CORE GENERATION LOGIC
    // =========================================================================

    private async Task<string> GenerateOpeningAsync(string directive)
    {
        _turnCount++;

        // Budget check
        if (IsBudgetExhausted())
            return GetBudgetExhaustedFallback();

        // RAG: retrieve relevant content for the concept
        var passages = await RetrieveRagPassagesAsync(_conceptId);

        var promptContext = new TutorPromptContext(
            Subject: _subject,
            ConceptName: _conceptId,
            Language: _language,
            Methodology: _methodology,
            ConceptMastery: _conceptMastery,
            BloomsLevel: _bloomsLevel,
            StudentMessage: directive,
            History: [],
            RetrievedPassages: passages,
            QuestionDifficulty: _questionDifficulty > 0f ? _questionDifficulty : null);

        var (systemPrompt, userPrompt) = _promptBuilder.Build(promptContext);

        // ADR-0046 Decision 4 — fail-closed on scrubber increment. The student
        // message is in userPrompt; if the scrubber finds residual PII,
        // refuse the LLM call and serve the safety fallback.
        var scrub = _piiScrubber.Scrub(userPrompt, "socratic");
        if (scrub.RedactionCount > 0)
        {
            _logger.LogWarning(
                "[ADR-0046] PII detected in tutor-opening prompt — refusing LLM call for session {SessionId}. " +
                "Categories=[{Categories}].",
                _sessionId, string.Join(",", scrub.Categories));
            return GetSafetyFallback();
        }

        try
        {
            var llmResponse = await _llm.CompleteAsync(new LlmRequest(
                SystemPrompt: systemPrompt,
                UserPrompt: scrub.ScrubbedText,
                Temperature: 0.4f,
                MaxTokens: 500,
                ModelId: "claude-sonnet-4-6-20260215",
                CacheSystemPrompt: true));

            TrackTokenUsage(llmResponse.OutputTokens);

            // prr-046: per-feature cost tag on success path.
            _costMetric.Record(
                feature: "socratic",
                tier: "tier3",
                task: "socratic_question",
                modelId: llmResponse.ModelId,
                inputTokens: llmResponse.InputTokens,
                outputTokens: llmResponse.OutputTokens);

            var safetyResult = _safetyGuard.Validate(llmResponse.Content, _subject, _conceptId);
            if (!safetyResult.IsAllowed)
            {
                _logger.LogWarning("Safety guard blocked opening for session {SessionId}: {Reason}",
                    _sessionId, safetyResult.BlockReason);
                return GetSafetyFallback();
            }

            return llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for opening in session {SessionId}", _sessionId);
            return GetErrorFallback();
        }
    }

    private async Task<string> GenerateResponseAsync(string studentMessage)
    {
        // Budget check: fallback to L2 cached explanation if exhausted
        if (IsBudgetExhausted())
        {
            _logger.LogInformation("Token budget exhausted for student {StudentId}, falling back to L2",
                _studentId);
            return await GetL2FallbackAsync() ?? GetBudgetExhaustedFallback();
        }

        // Guardrail: off-topic detection
        if (IsOffTopic(studentMessage))
        {
            var redirect = _language == "he"
                ? $"זו שאלה מעניינת, אבל בוא נתמקד ב{_conceptId}. האם תוכל להסביר לי..."
                : _language == "ar"
                    ? $"سؤال مثير للاهتمام، لكن دعنا نركز على {_conceptId}. هل يمكنك أن تشرح لي..."
                    : $"That's interesting, but let's focus on {_conceptId}. Can you tell me...";
            return redirect;
        }

        // Guardrail: safety (personal advice, opinions)
        if (IsPersonalAdviceRequest(studentMessage))
            return GetSafetyDeflection();

        // RAG: retrieve relevant content
        var passages = await RetrieveRagPassagesAsync(studentMessage);

        // Build prompt with conversation history window
        var recentHistory = _history.Count > HistoryWindowSize
            ? _history.GetRange(_history.Count - HistoryWindowSize, HistoryWindowSize)
            : _history;

        var promptContext = new TutorPromptContext(
            Subject: _subject,
            ConceptName: _conceptId,
            Language: _language,
            Methodology: _methodology,
            ConceptMastery: _conceptMastery,
            BloomsLevel: _bloomsLevel,
            StudentMessage: studentMessage,
            History: recentHistory,
            RetrievedPassages: passages,
            QuestionDifficulty: _questionDifficulty > 0f ? _questionDifficulty : null);

        var (systemPrompt, userPrompt) = _promptBuilder.Build(promptContext);

        // ADR-0046 Decision 4 — fail-closed on scrubber increment. Student
        // free-text enters the prompt; if the scrubber fires, refuse the
        // LLM call and serve the safety deflection.
        var scrub = _piiScrubber.Scrub(userPrompt, "socratic");
        if (scrub.RedactionCount > 0)
        {
            _logger.LogWarning(
                "[ADR-0046] PII detected in tutor-response prompt — refusing LLM call for session {SessionId}. " +
                "Categories=[{Categories}].",
                _sessionId, string.Join(",", scrub.Categories));
            return GetSafetyDeflection();
        }

        try
        {
            var llmResponse = await _llm.CompleteAsync(new LlmRequest(
                SystemPrompt: systemPrompt,
                UserPrompt: scrub.ScrubbedText,
                Temperature: 0.4f,
                MaxTokens: 500,
                ModelId: "claude-sonnet-4-6-20260215",
                CacheSystemPrompt: true));

            TrackTokenUsage(llmResponse.OutputTokens);

            // prr-046: per-feature cost tag on success path.
            _costMetric.Record(
                feature: "socratic",
                tier: "tier3",
                task: "socratic_question",
                modelId: llmResponse.ModelId,
                inputTokens: llmResponse.InputTokens,
                outputTokens: llmResponse.OutputTokens);

            var safetyResult = _safetyGuard.Validate(llmResponse.Content, _subject, _conceptId);
            if (!safetyResult.IsAllowed)
            {
                _logger.LogWarning("Safety guard blocked response for session {SessionId}: {Reason}",
                    _sessionId, safetyResult.BlockReason);
                return GetSafetyFallback();
            }

            return llmResponse.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for session {SessionId}", _sessionId);
            return GetErrorFallback();
        }
    }

    // =========================================================================
    // RAG RETRIEVAL
    // =========================================================================

    private async Task<IReadOnlyList<string>> RetrieveRagPassagesAsync(string queryText)
    {
        try
        {
            var ragContext = new TutoringContext(
                StudentQuestion: queryText,
                CurrentQuestionStem: "",
                ConceptId: _conceptId,
                Subject: _subject,
                Language: _language);

            var matches = await _contentRetriever.RetrieveAsync(ragContext, CancellationToken.None);

            var passages = matches
                .Take(3)
                .Select(m => m.Text)
                .ToList();

            _logger.LogDebug("RAG retrieval for session {SessionId}: {Count} passages",
                _sessionId, passages.Count);

            return passages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG retrieval failed for session {SessionId}, continuing without context",
                _sessionId);
            return [];
        }
    }

    // =========================================================================
    // BUDGET ENFORCEMENT
    // =========================================================================

    private bool IsBudgetExhausted()
    {
        var budgetKey = BuildBudgetKey();
        var current = DailyTokenUsage.GetValueOrDefault(budgetKey, 0);
        return current >= DailyOutputTokenLimit;
    }

    private void TrackTokenUsage(int outputTokens)
    {
        var budgetKey = BuildBudgetKey();
        DailyTokenUsage.AddOrUpdate(budgetKey, outputTokens, (_, prev) => prev + outputTokens);
    }

    private string BuildBudgetKey() =>
        $"tutor:{_studentId}:{_clock.UtcDateTime:yyyy-MM-dd}";

    private async Task<string?> GetL2FallbackAsync()
    {
        if (string.IsNullOrEmpty(_questionId))
            return null;

        try
        {
            // Try to get a cached explanation as fallback
            var errorType = Enum.TryParse<ExplanationErrorType>(_errorType, true, out var parsed)
                ? parsed
                : ExplanationErrorType.ConceptualMisunderstanding;

            var cached = await _explanationCache.GetAsync(
                _questionId, errorType, _language, CancellationToken.None);

            if (cached is not null)
            {
                _logger.LogDebug("L2 fallback hit for question {QuestionId}", _questionId);
                return cached.Text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "L2 fallback failed for session {SessionId}", _sessionId);
        }

        return null;
    }

    // =========================================================================
    // GUARDRAILS
    // =========================================================================

    private static bool IsOffTopic(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length < 10)
            return false;

        var lower = message.ToLowerInvariant();
        var offTopicPatterns = new[]
        {
            "what is your name",
            "who made you",
            "tell me a joke",
            "what's the weather",
            "play a game",
            "sing a song",
            "write me a story",
            "what do you think about",
            "who is the president",
            "מה השם שלך",
            "ספר לי בדיחה",
            "ما اسمك",
            "احكي لي نكتة"
        };

        return offTopicPatterns.Any(p => lower.Contains(p));
    }

    private static bool IsPersonalAdviceRequest(string message)
    {
        var lower = message.ToLowerInvariant();
        var personalPatterns = new[]
        {
            "should i drop out",
            "i feel depressed",
            "i want to hurt",
            "personal advice",
            "relationship advice",
            "medical advice",
            "אני מרגיש רע",
            "أشعر بالحزن"
        };

        return personalPatterns.Any(p => lower.Contains(p));
    }

    private string GetSafetyDeflection()
    {
        return _language == "he"
            ? "אני מתורגל רק ללימוד. אם אתה צריך לדבר עם מישהו, פנה למורה או ליועץ שלך. בוא נחזור ללימודים?"
            : _language == "ar"
                ? "أنا مخصص للتعليم فقط. إذا كنت بحاجة للتحدث مع شخص ما، تواصل مع معلمك أو مستشارك. هل نعود للدراسة؟"
                : "I am only able to help with learning. If you need to talk to someone, please reach out to your teacher or counselor. Shall we get back to studying?";
    }

    private string GetBudgetExhaustedFallback()
    {
        return _language == "he"
            ? "נגמר לנו הזמן של השיחות להיום. בוא ננסה לתרגל עם שאלות חדשות!"
            : _language == "ar"
                ? "لقد انتهى وقت المحادثات لهذا اليوم. لنجرب التمرن مع أسئلة جديدة!"
                : "We have used up our conversation time for today. Let's practice with some new questions!";
    }

    private string GetSafetyFallback()
    {
        return _language == "he"
            ? "תן לי לנסח מחדש. מה בדיוק קשה לך בנושא הזה?"
            : _language == "ar"
                ? "دعني أعيد الصياغة. ما الذي تجده صعباً بالتحديد في هذا الموضوع؟"
                : "Let me rephrase that. What specifically do you find difficult about this topic?";
    }

    private string GetErrorFallback()
    {
        return _language == "he"
            ? "יש לי בעיה טכנית רגע. תנסה לשאול שוב?"
            : _language == "ar"
                ? "أواجه مشكلة تقنية حالياً. هل يمكنك المحاولة مرة أخرى؟"
                : "I am having trouble thinking right now. Could you try asking again?";
    }

    // =========================================================================
    // SESSION LIFECYCLE
    // =========================================================================

    private void InitSession(
        IContext context,
        string studentId, string sessionId, string conceptId,
        string subject, string language, string methodology,
        double conceptMastery, int bloomsLevel, string triggerType)
    {
        _studentId = studentId;
        _sessionId = sessionId;
        _tutoringSessionId = $"tutor-{Guid.NewGuid():N}";
        _conceptId = conceptId;
        _subject = subject;
        _language = language;
        _methodology = methodology;
        _conceptMastery = conceptMastery;
        _bloomsLevel = bloomsLevel;
        _triggerType = triggerType;
        _turnCount = 0;
        _history.Clear();
        _startedAt = DateTimeOffset.UtcNow;
        _initialized = true;

        // Set 5-minute inactivity timeout
        context.SetReceiveTimeout(InactivityTimeout);

        _logger.LogInformation(
            "Tutoring session started: id={TutoringSessionId} session={SessionId} " +
            "concept={ConceptId} methodology={Methodology} trigger={Trigger}",
            _tutoringSessionId, _sessionId, _conceptId, _methodology, _triggerType);
    }

    private void AppendTutorTurn(string responseText)
    {
        _history.Add(new ConversationTurn("tutor", responseText, DateTimeOffset.UtcNow));
    }

    private void RespondTutoringResponse(IContext context, string responseText)
    {
        var remaining = MaxTurns - _turnCount;
        context.Respond(new TutorResponse(_turnCount, responseText, remaining <= 0, Math.Max(0, remaining)));
    }

    private Task OnEndTutoring(IContext context)
    {
        if (_initialized)
        {
            DelegateEndedEvent(context, "student_ended");

            _logger.LogInformation(
                "Tutoring session ended: id={TutoringSessionId} session={SessionId} turns={TurnCount}",
                _tutoringSessionId, _sessionId, _turnCount);
        }

        _initialized = false;
        context.CancelReceiveTimeout();
        context.Stop(context.Self);
        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        if (_initialized)
        {
            DelegateEndedEvent(context, "session_end");
            _initialized = false;
        }

        _logger.LogDebug("TutorActor stopping for session {SessionId}", _sessionId);
        return Task.CompletedTask;
    }

    // =========================================================================
    // EVENT DELEGATION (to parent LearningSessionActor -> StudentActor)
    // =========================================================================

    private void DelegateStartedEvent(IContext context)
    {
        if (context.Parent == null) return;

        context.Send(context.Parent, new DelegateEvent(new TutoringSessionStarted_V1(
            _studentId, _sessionId, _tutoringSessionId, _conceptId,
            _subject, _methodology, _language, _conceptMastery, _bloomsLevel,
            DateTimeOffset.UtcNow)));
    }

    private void DelegateMessageEvent(IContext context, int turnNumber, string role,
        string content, int sourceCount)
    {
        if (context.Parent == null) return;

        var preview = content.Length > 200 ? content[..200] : content;
        context.Send(context.Parent, new DelegateEvent(new TutoringMessageSent_V1(
            _studentId, _sessionId, _tutoringSessionId, turnNumber,
            role, preview, sourceCount, DateTimeOffset.UtcNow)));
    }

    private void DelegateEndedEvent(IContext context, string reason)
    {
        if (context.Parent == null) return;

        var duration = DateTimeOffset.UtcNow - _startedAt;
        var durationSeconds = (int)duration.TotalSeconds;

        // Emit TutoringSessionEnded_V1 (existing lifecycle event)
        context.Send(context.Parent, new DelegateEvent(new TutoringSessionEnded_V1(
            _studentId, _sessionId, _tutoringSessionId, reason,
            _turnCount, durationSeconds, DateTimeOffset.UtcNow)));

        // Emit TutoringEpisodeCompleted_V1 (SAI-07 summary event for analytics)
        context.Send(context.Parent, new DelegateEvent(new TutoringEpisodeCompleted_V1(
            StudentId: _studentId,
            SessionId: _sessionId,
            ConceptId: _conceptId,
            TriggerType: _triggerType,
            Methodology: _methodology,
            TurnCount: _turnCount,
            Duration: duration,
            ResolutionStatus: reason,
            Timestamp: DateTimeOffset.UtcNow)));
    }
}
