// =============================================================================
// Cena Platform -- Tutor REST Endpoints (STB-04 Phase 1 + STB-04b Phase 1b)
// AI tutor thread and message endpoints with SSE streaming
// =============================================================================

using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Cena.Api.Contracts.Tutor;
using Cena.Actors.Events;
using Cena.Actors.Tutor;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class TutorEndpoints
{
    public static IEndpointRouteBuilder MapTutorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tutor")
            .WithTags("Tutor")
            .RequireAuthorization();

        // Thread endpoints - all require ThirdPartyAI consent
        group.MapGet("/threads", GetThreads)
            .WithName("GetTutorThreads")
            .RequireConsent(ProcessingPurpose.ThirdPartyAi);
        group.MapPost("/threads", CreateThread)
            .WithName("CreateTutorThread")
            .RequireConsent(ProcessingPurpose.ThirdPartyAi);
        
        // Message endpoints - all require ThirdPartyAI consent
        group.MapGet("/threads/{threadId}/messages", GetMessages)
            .WithName("GetTutorMessages")
            .RequireConsent(ProcessingPurpose.ThirdPartyAi);
        
        // FIND-arch-004: SendMessage now calls the real LLM via ITutorMessageService,
        // so it must share the same rate limit as /stream (10 msg/min/student).
        // FIND-sec-015: Chained with global and per-tenant limits for cost protection.
        group.MapPost("/threads/{threadId}/messages", SendMessage)
            .WithName("SendTutorMessage")
            .RequireRateLimiting("tutor")      // Per-user: 10 msg/min/student
            .RequireRateLimiting("tutor-tenant") // Per-tenant: 200 msg/min/school
            .RequireRateLimiting("tutor-global") // Global: 1000 msg/min across all users
            .RequireConsent(ProcessingPurpose.ThirdPartyAi);

        // SSE streaming endpoint (HARDEN: Real LLM with rate limiting)
        // Requires ThirdPartyAI consent
        // FIND-sec-015: Chained rate limits for cost protection
        group.MapPost("/threads/{threadId}/stream", StreamMessage)
            .WithName("StreamTutorMessage")
            .RequireRateLimiting("tutor")      // Per-user: 10 msg/min/student
            .RequireRateLimiting("tutor-tenant") // Per-tenant: 200 msg/min/school  
            .RequireRateLimiting("tutor-global") // Global: 1000 msg/min across all users
            .RequireConsent(ProcessingPurpose.ThirdPartyAi);

        return app;
    }

    // GET /api/tutor/threads — list tutor threads for the student
    private static async Task<IResult> GetThreads(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        
        // Get threads for this student, ordered by most recent update
        var threads = await session.Query<TutorThreadDocument>()
            .Where(t => t.StudentId == studentId && !t.IsArchived)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();

        // If no threads exist, return empty list (Phase 1: no auto-creation)
        var dtos = threads.Select(t => new TutorThreadDto(
            ThreadId: t.ThreadId,
            Title: t.Title,
            Subject: t.Subject,
            Topic: t.Topic,
            CreatedAt: t.CreatedAt,
            UpdatedAt: t.UpdatedAt,
            MessageCount: t.MessageCount,
            IsArchived: t.IsArchived)).ToArray();

        return Results.Ok(new TutorThreadListDto(
            Items: dtos,
            TotalCount: dtos.Length));
    }

    // POST /api/tutor/threads — create a new tutor thread
    private static async Task<IResult> CreateThread(
        HttpContext ctx,
        IDocumentStore store,
        CreateThreadRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        
        var threadId = $"tutor_thread_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        
        var thread = new TutorThreadDocument
        {
            Id = threadId,
            ThreadId = threadId,
            StudentId = studentId,
            Title = request.Title ?? "New Tutoring Session",
            Subject = request.Subject,
            Topic = request.Topic,
            CreatedAt = now,
            UpdatedAt = now,
            MessageCount = 0,
            IsArchived = false
        };

        // If initial message provided, add it
        if (!string.IsNullOrWhiteSpace(request.InitialMessage))
        {
            var messageId = $"tutor_msg_{Guid.NewGuid():N}";
            var userMessage = new TutorMessageDocument
            {
                Id = messageId,
                MessageId = messageId,
                ThreadId = threadId,
                StudentId = studentId,
                Role = "user",
                Content = request.InitialMessage,
                CreatedAt = now
            };

            // Create welcome message (non-streaming for initial thread creation)
            var assistantMessageId = $"tutor_msg_{Guid.NewGuid():N}";
            var assistantMessage = new TutorMessageDocument
            {
                Id = assistantMessageId,
                MessageId = assistantMessageId,
                ThreadId = threadId,
                StudentId = studentId,
                Role = "assistant",
                Content = "Hello! I'm your AI tutor. How can I help you with your learning today?",
                CreatedAt = now.AddSeconds(1)
            };

            session.Store(userMessage);
            session.Store(assistantMessage);
            
            thread.MessageCount = 2;
            thread.UpdatedAt = assistantMessage.CreatedAt;
        }

        session.Store(thread);
        await session.SaveChangesAsync();

        return Results.Ok(new CreateThreadResponse(
            ThreadId: threadId,
            Title: thread.Title,
            CreatedAt: thread.CreatedAt));
    }

    // GET /api/tutor/threads/{threadId}/messages — get messages in a thread
    private static async Task<IResult> GetMessages(
        HttpContext ctx,
        IDocumentStore store,
        string threadId)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        
        // Verify thread exists and belongs to student
        var thread = await session.LoadAsync<TutorThreadDocument>(threadId);
        if (thread is null || thread.StudentId != studentId)
            return Results.NotFound();

        // Get messages for this thread
        var messages = await session.Query<TutorMessageDocument>()
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var dtos = messages.Select(m => new TutorMessageDto(
            MessageId: m.MessageId,
            Role: m.Role,
            Content: m.Content,
            CreatedAt: m.CreatedAt,
            Model: m.Model)).ToArray();

        return Results.Ok(new TutorMessageListDto(
            ThreadId: threadId,
            Messages: dtos,
            HasMore: false)); // Phase 1: no pagination
    }

    // POST /api/tutor/threads/{threadId}/messages — send a message and get a real AI reply.
    //
    // FIND-arch-004 (2026-04-11): This handler used to store a canned redirect
    // string as the assistant reply. That placeholder has been removed. The
    // handler now delegates to ITutorMessageService, which invokes the same
    // real LLM (ClaudeTutorLlmService → Anthropic) that the /stream endpoint
    // uses, drained into a single unary response. On LLM failure it returns
    // HTTP 503 — no fake assistant message is ever persisted.
    private static async Task<IResult> SendMessage(
        HttpContext ctx,
        ITutorMessageService tutorMessageService,
        string threadId,
        SendMessageRequest request,
        CancellationToken ct)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var result = await tutorMessageService.SendAsync(studentId, threadId, request.Content, ct);

        return result switch
        {
            SendTutorMessageResult.Success s => Results.Ok(new SendMessageResponse(
                MessageId: s.MessageId,
                Role: "assistant",
                Content: s.Content,
                CreatedAt: s.CreatedAt,
                Status: "complete")),
            // FIND-privacy-008: safeguarding concern -> return the "talk to trusted adult"
            // response as a normal assistant message so the student sees support info.
            SendTutorMessageResult.SafeguardingEscalated sg => Results.Ok(new SendMessageResponse(
                MessageId: $"safeguard_{Guid.NewGuid():N}",
                Role: "assistant",
                Content: sg.StudentResponse,
                CreatedAt: DateTime.UtcNow,
                Status: "safeguarding")),
            SendTutorMessageResult.InvalidContent ic =>
                Results.BadRequest(new { Error = ic.Reason }),
            SendTutorMessageResult.ThreadNotFound =>
                Results.NotFound(),
            SendTutorMessageResult.LlmError le =>
                Results.Problem(
                    title: "Tutor service unavailable",
                    detail: le.Reason,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    // POST /api/tutor/threads/{threadId}/stream — SSE streaming endpoint (STB-04b)
    // FIND-privacy-008: PII scrubbing + safeguarding scan added.
    private static async IAsyncEnumerable<SseEvent> StreamMessage(
        HttpContext ctx,
        IDocumentStore store,
        ITutorLlmService llmService,
        ITutorPromptScrubber scrubber,
        ISafeguardingClassifier classifier,
        ISafeguardingEscalation escalation,
        string threadId,
        StreamMessageRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
        {
            yield return new SseEvent("error", JsonSerializer.Serialize(new { error = "Unauthorized" }));
            yield break;
        }

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            yield return new SseEvent("error", JsonSerializer.Serialize(new { error = "Content is required" }));
            yield break;
        }

        // ── FIND-privacy-008: Safeguarding scan BEFORE any persistence ──
        var safeguardingResult = classifier.Scan(request.Content);
        if (safeguardingResult.IsConcern && safeguardingResult.Severity >= SafeguardingSeverity.High)
        {
            var escalationResult = await escalation.EscalateAsync(
                studentId, threadId, safeguardingResult, market: null, ct);

            yield return new SseEvent("safeguarding", JsonSerializer.Serialize(new
            {
                response = escalationResult.StudentResponse,
                severity = safeguardingResult.Severity.ToString()
            }));
            yield return new SseEvent("done", JsonSerializer.Serialize(new
            {
                messageId = escalationResult.Alert.AlertId,
                content = escalationResult.StudentResponse,
                createdAt = DateTime.UtcNow
            }));
            yield break;
        }

        await using var session = store.LightweightSession();

        // Verify thread exists and belongs to student
        var thread = await session.LoadAsync<TutorThreadDocument>(threadId);
        if (thread is null || thread.StudentId != studentId)
        {
            yield return new SseEvent("error", JsonSerializer.Serialize(new { error = "Thread not found" }));
            yield break;
        }

        var now = DateTime.UtcNow;

        // Store user message (original content for local DB)
        var userMessageId = $"tutor_msg_{Guid.NewGuid():N}";
        var userMessage = new TutorMessageDocument
        {
            Id = userMessageId,
            MessageId = userMessageId,
            ThreadId = threadId,
            StudentId = studentId,
            Role = "user",
            Content = request.Content,
            CreatedAt = now
        };
        session.Store(userMessage);

        // ── FIND-privacy-008: PII scrubbing for outbound LLM call ──
        var piiContext = new StudentPiiContext(
            StudentId: studentId,
            FirstName: null, LastName: null, Email: null,
            SchoolName: null, ParentName: null, City: null);

        // Get conversation history for context
        var history = await session.Query<TutorMessageDocument>()
            .Where(m => m.ThreadId == threadId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .ToListAsync();

        var conversationHistory = history
            .OrderBy(m => m.CreatedAt)
            .Select(m =>
            {
                var content = m.Role == "user"
                    ? scrubber.Scrub(m.Content, piiContext).ScrubbedText
                    : m.Content;
                return (m.Role, Content: content);
            })
            .ToList();

        // Prepare for assistant response
        var assistantMessageId = $"tutor_msg_{Guid.NewGuid():N}";
        var fullContent = new System.Text.StringBuilder();

        // Send message ID first
        yield return new SseEvent("message_id", JsonSerializer.Serialize(new { messageId = assistantMessageId }));

        // Build tutor context for LLM with SCRUBBED content
        var scrubbedInput = scrubber.Scrub(request.Content, piiContext).ScrubbedText;
        var tutorContext = new TutorContext(
            StudentId: studentId,
            ThreadId: threadId,
            MessageHistory: conversationHistory
                .Select(m => new global::Cena.Actors.Tutor.TutorMessage(m.Role, m.Content))
                .ToList(),
            Subject: thread.Subject,
            CurrentGrade: null // Could be loaded from student profile
        );

        // Stream tokens from real LLM (HARDEN: No stubs)
        int? totalTokensUsed = null;
        await foreach (var chunk in llmService.StreamCompletionAsync(tutorContext, ct))
        {
            if (!string.IsNullOrEmpty(chunk.Delta))
            {
                fullContent.Append(chunk.Delta);
                yield return new SseEvent("token", JsonSerializer.Serialize(new { token = chunk.Delta }));
            }

            // Capture token usage from final chunk
            if (chunk.Finished && chunk.TokensUsed.HasValue)
            {
                totalTokensUsed = chunk.TokensUsed.Value;
            }
        }

        // Store the complete assistant message with token accounting (HARDEN)
        var assistantMessage = new TutorMessageDocument
        {
            Id = assistantMessageId,
            MessageId = assistantMessageId,
            ThreadId = threadId,
            StudentId = studentId,
            Role = "assistant",
            Content = fullContent.ToString().Trim(),
            CreatedAt = DateTime.UtcNow,
            Model = "claude-3-sonnet-20240229", // HARDEN: Real model name
            TokensUsed = totalTokensUsed // HARDEN: Persist for billing/throttling
        };
        session.Store(assistantMessage);

        // Update thread
        thread.MessageCount += 2;
        thread.UpdatedAt = assistantMessage.CreatedAt;
        session.Store(thread);

        // Append tutoring session event for analytics (using internal tutoring event)
        var tutoringEvent = new TutoringMessageSent_V1(
            StudentId: studentId,
            SessionId: threadId,
            TutoringSessionId: threadId,
            TurnNumber: (int)thread.MessageCount / 2,
            Role: "tutor",
            MessagePreview: assistantMessage.Content[..Math.Min(200, assistantMessage.Content.Length)],
            SourceCount: 0, // Future: RAG source count when implemented
            Timestamp: DateTimeOffset.UtcNow);

        session.Events.Append(studentId, tutoringEvent);
        await session.SaveChangesAsync(ct);

        // Signal completion
        yield return new SseEvent("done", JsonSerializer.Serialize(new {
            messageId = assistantMessageId,
            content = assistantMessage.Content,
            createdAt = assistantMessage.CreatedAt
        }));
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}

/// <summary>
/// SSE event structure for streaming responses.
/// </summary>
public record SseEvent(string Event, string Data);

/// <summary>
/// Request body for SSE streaming endpoint.
/// </summary>
public record StreamMessageRequest(string Content);
