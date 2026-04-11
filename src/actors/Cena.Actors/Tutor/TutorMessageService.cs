// =============================================================================
// Cena Platform — Tutor Message Service (FIND-arch-004)
// Production-grade non-streaming tutor message handler.
//
// Replaces the canned placeholder that used to live inline in
// TutorEndpoints.SendMessage (which redirected callers to /stream instead of
// actually answering). All LLM calls now go through the real ITutorLlmService
// — the same implementation used by the /stream endpoint.
//
// The service is extracted from the endpoint handler to make it:
//   1. Directly unit-testable via ITutorMessageRepository (no Marten host test).
//   2. Bounded-context clean (tutor domain logic lives in Cena.Actors.Tutor,
//      not in an HTTP endpoint file).
//   3. Shareable across hosts (Cena.Student.Api.Host + legacy Cena.Api.Host
//      call the same code path — no drift possible).
// =============================================================================

using Cena.Actors.Tutoring;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Tutor;

/// <summary>
/// Outcome of a non-streaming tutor message request.
/// One of: Success | ThreadNotFound | InvalidContent | LlmError.
/// </summary>
public abstract record SendTutorMessageResult
{
    public sealed record Success(
        string MessageId,
        string Content,
        DateTime CreatedAt,
        string Model,
        int? TokensUsed) : SendTutorMessageResult;

    public sealed record ThreadNotFound : SendTutorMessageResult;

    public sealed record InvalidContent(string Reason) : SendTutorMessageResult;

    public sealed record LlmError(string Reason) : SendTutorMessageResult;
}

/// <summary>
/// Persistence boundary for <see cref="TutorMessageService"/>.
/// Abstracts Marten so the service can be unit-tested without a DB.
/// </summary>
public interface ITutorMessageRepository
{
    /// <summary>
    /// Load a tutor thread and verify it belongs to the given student.
    /// Returns null if the thread does not exist or belongs to another student.
    /// </summary>
    Task<TutorThreadDocument?> LoadOwnedThreadAsync(string threadId, string studentId, CancellationToken ct);

    /// <summary>
    /// Persist a user message and bump the thread's message count + UpdatedAt.
    /// Called before the LLM request so the user's turn is durable even on LLM failure.
    /// </summary>
    Task PersistUserMessageAsync(TutorThreadDocument thread, TutorMessageDocument userMessage, CancellationToken ct);

    /// <summary>
    /// Load up to <paramref name="maxMessages"/> recent messages for the thread,
    /// ordered oldest → newest, for building LLM context.
    /// </summary>
    Task<IReadOnlyList<TutorMessage>> LoadRecentHistoryAsync(string threadId, int maxMessages, CancellationToken ct);

    /// <summary>
    /// Persist the assistant message, bump the thread, and append the tutoring analytics event.
    /// </summary>
    Task PersistAssistantMessageAsync(
        TutorThreadDocument thread,
        TutorMessageDocument assistantMessage,
        TutoringMessageSent_V1 analyticsEvent,
        CancellationToken ct);
}

/// <summary>
/// Service that handles the non-streaming AI tutor message flow.
/// </summary>
public interface ITutorMessageService
{
    /// <summary>
    /// Persist a student message, invoke the real LLM for an assistant reply, persist
    /// the reply, append the analytics event, and return a structured result.
    /// </summary>
    /// <remarks>
    /// This method never returns a canned/placeholder response. If the LLM fails,
    /// the result is <see cref="SendTutorMessageResult.LlmError"/> — the caller
    /// must translate that into an HTTP error response, never a fake "assistant" message.
    /// </remarks>
    Task<SendTutorMessageResult> SendAsync(
        string studentId,
        string threadId,
        string content,
        CancellationToken ct = default);
}

/// <summary>
/// Production implementation of <see cref="ITutorMessageService"/>.
/// </summary>
public sealed class TutorMessageService : ITutorMessageService
{
    private readonly ITutorMessageRepository _repository;
    private readonly ITutorLlmService _llmService;
    private readonly ILogger<TutorMessageService> _logger;

    public TutorMessageService(
        ITutorMessageRepository repository,
        ITutorLlmService llmService,
        ILogger<TutorMessageService> logger)
    {
        _repository = repository;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<SendTutorMessageResult> SendAsync(
        string studentId,
        string threadId,
        string content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new SendTutorMessageResult.InvalidContent("Content is required");
        if (string.IsNullOrWhiteSpace(studentId))
            return new SendTutorMessageResult.InvalidContent("StudentId is required");
        if (string.IsNullOrWhiteSpace(threadId))
            return new SendTutorMessageResult.InvalidContent("ThreadId is required");

        var thread = await _repository.LoadOwnedThreadAsync(threadId, studentId, ct);
        if (thread is null)
            return new SendTutorMessageResult.ThreadNotFound();

        var now = DateTime.UtcNow;

        // Persist the user message up front so we don't lose it if the LLM fails.
        var userMessageId = $"tutor_msg_{Guid.NewGuid():N}";
        var userMessage = new TutorMessageDocument
        {
            Id = userMessageId,
            MessageId = userMessageId,
            ThreadId = threadId,
            StudentId = studentId,
            Role = "user",
            Content = content,
            CreatedAt = now
        };
        thread.MessageCount += 1;
        thread.UpdatedAt = now;
        await _repository.PersistUserMessageAsync(thread, userMessage, ct);

        // Build conversation history (last 10 messages) for LLM context.
        var history = await _repository.LoadRecentHistoryAsync(threadId, 10, ct);

        var tutorContext = new TutorContext(
            StudentId: studentId,
            ThreadId: threadId,
            MessageHistory: history,
            Subject: thread.Subject,
            CurrentGrade: null);

        // Real LLM call. We reuse the same ITutorLlmService that /stream uses
        // (ClaudeTutorLlmService → Anthropic), draining the async stream into a
        // single complete response for this unary endpoint. No placeholder text.
        var fullContent = new System.Text.StringBuilder();
        int? totalTokensUsed = null;
        string? model = null;

        try
        {
            await foreach (var chunk in _llmService.StreamCompletionAsync(tutorContext, ct))
            {
                if (!string.IsNullOrEmpty(chunk.Delta))
                    fullContent.Append(chunk.Delta);
                if (chunk.Model is not null)
                    model = chunk.Model;
                if (chunk.Finished && chunk.TokensUsed.HasValue)
                    totalTokensUsed = chunk.TokensUsed.Value;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Tutor LLM call failed for student {StudentId}, thread {ThreadId}",
                studentId, threadId);
            return new SendTutorMessageResult.LlmError("The tutor service is temporarily unavailable. Please try again.");
        }

        var assistantContent = fullContent.ToString().Trim();
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            _logger.LogWarning(
                "Tutor LLM returned empty content for student {StudentId}, thread {ThreadId}",
                studentId, threadId);
            return new SendTutorMessageResult.LlmError("The tutor service returned no content. Please try again.");
        }

        // Persist the assistant message + analytics event + updated thread.
        var assistantMessageId = $"tutor_msg_{Guid.NewGuid():N}";
        var assistantCreatedAt = DateTime.UtcNow;
        var assistantMessage = new TutorMessageDocument
        {
            Id = assistantMessageId,
            MessageId = assistantMessageId,
            ThreadId = threadId,
            StudentId = studentId,
            Role = "assistant",
            Content = assistantContent,
            CreatedAt = assistantCreatedAt,
            Model = model,
            TokensUsed = totalTokensUsed
        };

        thread.MessageCount += 1;
        thread.UpdatedAt = assistantCreatedAt;

        // Analytics event: mirror the /stream endpoint so both paths produce
        // comparable tutoring telemetry for billing/throttling.
        var preview = assistantContent[..Math.Min(200, assistantContent.Length)];
        var tutoringEvent = new TutoringMessageSent_V1(
            StudentId: studentId,
            SessionId: threadId,
            TutoringSessionId: threadId,
            TurnNumber: (int)(thread.MessageCount / 2),
            Role: "tutor",
            MessagePreview: preview,
            SourceCount: 0,
            Timestamp: DateTimeOffset.UtcNow);

        await _repository.PersistAssistantMessageAsync(thread, assistantMessage, tutoringEvent, ct);

        return new SendTutorMessageResult.Success(
            MessageId: assistantMessageId,
            Content: assistantContent,
            CreatedAt: assistantCreatedAt,
            Model: model ?? "unknown",
            TokensUsed: totalTokensUsed);
    }
}
