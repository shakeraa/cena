// =============================================================================
// Cena Platform -- TutorActor (SAI-08)
// Short-lived Proto.Actor grain for conversational tutoring.
// Manages a single tutoring session: RAG retrieval, LLM calls, safety checks.
// Max 10 turns, then suggests ending the conversation.
// =============================================================================

using Cena.Actors.Gateway;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Tutoring;

// ── Messages ──

public sealed record StartTutoring(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Subject,
    string Language,
    string Methodology,
    double ConceptMastery,
    int BloomsLevel);

public sealed record TutorMessage(string StudentMessage);

public sealed record TutorResponse(string ResponseText, bool SuggestEndConversation);

public sealed record EndTutoring;

// ── Actor ──

public sealed class TutorActor : IActor
{
    private const int MaxTurns = 10;
    private const int HistoryWindowSize = 5;

    private readonly ILlmClient _llm;
    private readonly ITutorPromptBuilder _promptBuilder;
    private readonly ITutorSafetyGuard _safetyGuard;
    private readonly ILogger<TutorActor> _logger;

    // ── Session state ──
    private string _studentId = "";
    private string _sessionId = "";
    private string _conceptId = "";
    private string _subject = "";
    private string _language = "";
    private string _methodology = "";
    private double _conceptMastery;
    private int _bloomsLevel;
    private int _turnCount;
    private readonly List<ConversationTurn> _history = new();
    private DateTimeOffset _startedAt;
    private bool _initialized;

    public TutorActor(
        ILlmClient llm,
        ITutorPromptBuilder promptBuilder,
        ITutorSafetyGuard safetyGuard,
        ILogger<TutorActor> logger)
    {
        _llm = llm;
        _promptBuilder = promptBuilder;
        _safetyGuard = safetyGuard;
        _logger = logger;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            StartTutoring msg => OnStartTutoring(context, msg),
            TutorMessage msg => OnTutorMessage(context, msg),
            EndTutoring => OnEndTutoring(context),
            _ => Task.CompletedTask
        };
    }

    private Task OnStartTutoring(IContext context, StartTutoring msg)
    {
        _studentId = msg.StudentId;
        _sessionId = msg.SessionId;
        _conceptId = msg.ConceptId;
        _subject = msg.Subject;
        _language = msg.Language;
        _methodology = msg.Methodology;
        _conceptMastery = msg.ConceptMastery;
        _bloomsLevel = msg.BloomsLevel;
        _turnCount = 0;
        _history.Clear();
        _startedAt = DateTimeOffset.UtcNow;
        _initialized = true;

        _logger.LogInformation(
            "Tutoring session started: session={SessionId} concept={ConceptId} methodology={Methodology}",
            _sessionId, _conceptId, _methodology);

        return Task.CompletedTask;
    }

    private async Task OnTutorMessage(IContext context, TutorMessage msg)
    {
        if (!_initialized)
        {
            _logger.LogWarning("TutorMessage received before StartTutoring, ignoring");
            context.Respond(new TutorResponse("Session not initialized.", SuggestEndConversation: true));
            return;
        }

        _turnCount++;

        // Rate limit: reject if over max turns
        if (_turnCount > MaxTurns)
        {
            _logger.LogInformation("Max turns ({MaxTurns}) exceeded for session {SessionId}", MaxTurns, _sessionId);
            context.Respond(new TutorResponse(
                "We have covered a lot of ground. Let's wrap up and try some practice problems.",
                SuggestEndConversation: true));
            return;
        }

        // Record student turn
        _history.Add(new ConversationTurn("student", msg.StudentMessage, DateTimeOffset.UtcNow));

        // Build prompt (using last N turns as history window)
        var recentHistory = _history.Count > HistoryWindowSize
            ? _history.GetRange(_history.Count - HistoryWindowSize, HistoryWindowSize)
            : _history;

        // TODO: integrate IContentRetriever for RAG passages when available
        var retrievedPassages = Array.Empty<string>();

        var promptContext = new TutorPromptContext(
            Subject: _subject,
            ConceptName: _conceptId,
            Language: _language,
            Methodology: _methodology,
            ConceptMastery: _conceptMastery,
            BloomsLevel: _bloomsLevel,
            StudentMessage: msg.StudentMessage,
            History: recentHistory,
            RetrievedPassages: retrievedPassages);

        var (systemPrompt, userPrompt) = _promptBuilder.Build(promptContext);

        // Call LLM (no PII: we use mastery level / concept, not student ID)
        LlmResponse llmResponse;
        try
        {
            llmResponse = await _llm.CompleteAsync(new LlmRequest(
                ModelId: "claude-sonnet-4-6-20260215",
                SystemPrompt: systemPrompt,
                UserPrompt: userPrompt,
                Temperature: 0.4f,
                MaxTokens: 512));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for session {SessionId}", _sessionId);
            context.Respond(new TutorResponse(
                "I am having trouble thinking right now. Could you try asking again?",
                SuggestEndConversation: false));
            return;
        }

        // Safety validation
        var safetyResult = _safetyGuard.Validate(llmResponse.Content, _subject, _conceptId);
        string responseText;
        if (!safetyResult.IsAllowed)
        {
            _logger.LogWarning(
                "Safety guard blocked response for session {SessionId}: {Reason}",
                _sessionId, safetyResult.BlockReason);
            responseText = "Let me rephrase that. Could you tell me more about what you are finding difficult?";
        }
        else
        {
            responseText = llmResponse.Content;
        }

        // Record tutor turn
        _history.Add(new ConversationTurn("tutor", responseText, DateTimeOffset.UtcNow));

        var suggestEnd = _turnCount >= MaxTurns;
        context.Respond(new TutorResponse(responseText, suggestEnd));
    }

    private Task OnEndTutoring(IContext context)
    {
        _logger.LogInformation(
            "Tutoring session ended: session={SessionId} turns={TurnCount}",
            _sessionId, _turnCount);

        // TODO: persist TutoringSessionDocument to Marten via IDocumentSession
        // when document session is wired into the actor

        _initialized = false;
        context.Stop(context.Self);
        return Task.CompletedTask;
    }
}
