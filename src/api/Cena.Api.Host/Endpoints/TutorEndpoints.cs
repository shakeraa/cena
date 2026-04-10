// =============================================================================
// Cena Platform -- Tutor REST Endpoints (STB-04 Phase 1)
// AI tutor thread and message endpoints (stub responses, no streaming)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Tutor;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class TutorEndpoints
{
    public static IEndpointRouteBuilder MapTutorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tutor")
            .WithTags("Tutor")
            .RequireAuthorization();

        // Thread endpoints
        group.MapGet("/threads", GetThreads).WithName("GetTutorThreads");
        group.MapPost("/threads", CreateThread).WithName("CreateTutorThread");
        
        // Message endpoints
        group.MapGet("/threads/{threadId}/messages", GetMessages).WithName("GetTutorMessages");
        group.MapPost("/threads/{threadId}/messages", SendMessage).WithName("SendTutorMessage");

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

            // Create stub assistant response
            var assistantMessageId = $"tutor_msg_{Guid.NewGuid():N}";
            var assistantMessage = new TutorMessageDocument
            {
                Id = assistantMessageId,
                MessageId = assistantMessageId,
                ThreadId = threadId,
                StudentId = studentId,
                Role = "assistant",
                Content = "Great question! I'd be happy to help with that. (STB-04b will wire real LLM streaming)",
                CreatedAt = now.AddSeconds(1),
                Model = "gpt-4-stub"
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

    // POST /api/tutor/threads/{threadId}/messages — send a message and get stub response
    private static async Task<IResult> SendMessage(
        HttpContext ctx,
        IDocumentStore store,
        string threadId,
        SendMessageRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { Error = "Content is required" });
        }

        await using var session = store.LightweightSession();
        
        // Verify thread exists and belongs to student
        var thread = await session.LoadAsync<TutorThreadDocument>(threadId);
        if (thread is null || thread.StudentId != studentId)
            return Results.NotFound();

        var now = DateTime.UtcNow;

        // Store user message
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

        // Generate stub assistant response (Phase 1: no real LLM)
        var assistantMessageId = $"tutor_msg_{Guid.NewGuid():N}";
        var assistantMessage = new TutorMessageDocument
        {
            Id = assistantMessageId,
            MessageId = assistantMessageId,
            ThreadId = threadId,
            StudentId = studentId,
            Role = "assistant",
            Content = "Great question! I'd be happy to help with that. (STB-04b will wire real LLM streaming)",
            CreatedAt = now.AddSeconds(1),
            Model = "gpt-4-stub"
        };
        session.Store(assistantMessage);

        // Update thread
        thread.MessageCount += 2;
        thread.UpdatedAt = assistantMessage.CreatedAt;
        session.Store(thread);

        await session.SaveChangesAsync();

        return Results.Ok(new SendMessageResponse(
            MessageId: assistantMessageId,
            Role: "assistant",
            Content: assistantMessage.Content,
            CreatedAt: assistantMessage.CreatedAt,
            Status: "complete")); // Phase 1: always complete (no streaming)
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
