// =============================================================================
// Cena Platform -- Messaging Admin Endpoints
// ADM-025: REST endpoints for admin messaging/chat
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Admin.Api.Validation;
using Cena.Actors.Messaging;
using Cena.Infrastructure.Auth;
using Marten;
using Marten.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NATS.Client.Core;

namespace Cena.Admin.Api;

public static class MessagingAdminEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapMessagingAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/messaging")
            .WithTags("Messaging Admin")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/threads", async (
            string? type,
            string? participantId,
            string? search,
            int? page,
            int? pageSize,
            string? since,
            ClaimsPrincipal user,
            IMessagingAdminService service) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            DateTimeOffset? sinceTime = null;
            if (!string.IsNullOrEmpty(since)
                && DateTimeOffset.TryParse(since, out var parsed))
                sinceTime = parsed;
            var result = await service.GetThreadsAsync(
                type, participantId, search, validPage, validPageSize, user, sinceTime);
            return Results.Ok(result);
        }).WithName("GetMessagingThreads");

        group.MapGet("/threads/{threadId}", async (
            string threadId,
            string? before,
            int? limit,
            ClaimsPrincipal user,
            IMessagingAdminService service) =>
        {
            var validLimit = Math.Clamp(limit ?? 50, 1, 100);
            var detail = await service.GetThreadDetailAsync(threadId, before, validLimit, user);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetMessagingThreadDetail");

        group.MapPost("/threads", async (
            CreateThreadRequest request,
            HttpContext httpContext,
            INatsConnection nats,
            IDocumentStore store) =>
        {
            var userId = httpContext.User.FindFirst("user_id")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var userName = httpContext.User.FindFirst("name")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                ?? "Admin";

            var threadId = $"thread-{Guid.NewGuid():N}";
            var allParticipantIds = request.ParticipantIds.Append(userId).Distinct().ToArray();

            // Resolve participant names from AdminUser documents
            await using var session = store.LightweightSession();
            var users = await session.Query<Cena.Infrastructure.Documents.AdminUser>()
                .Where(u => u.Id.IsOneOf(allParticipantIds))
                .ToListAsync();

            var nameMap = users.ToDictionary(u => u.Id, u => u.FullName);
            var participantNames = allParticipantIds
                .Select(id => nameMap.GetValueOrDefault(id, id == userId ? userName : id))
                .ToArray();

            // Store ThreadSummary directly in Marten (immediate consistency)
            var summary = new Cena.Actors.Messaging.ThreadSummary
            {
                Id = threadId,
                ThreadType = request.ThreadType,
                ParticipantIds = allParticipantIds,
                ParticipantNames = participantNames,
                ClassRoomId = request.ClassRoomId,
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                LastMessageAt = DateTimeOffset.UtcNow,
                MessageCount = 0,
                LastMessagePreview = request.InitialMessage ?? "",
            };

            if (!string.IsNullOrEmpty(request.InitialMessage))
                summary.MessageCount = 1;

            session.Store(summary);
            await session.SaveChangesAsync();

            // Also publish to NATS for Actor Host event sourcing
            var createCmd = new
            {
                ThreadId = threadId,
                request.ThreadType,
                ParticipantIds = allParticipantIds,
                ParticipantNames = participantNames,
                CreatedById = userId,
                CreatedByName = userName
            };

            await nats.PublishAsync(
                MessagingNatsSubjects.CmdBroadcastToClass,
                JsonSerializer.SerializeToUtf8Bytes(createCmd, JsonOpts));

            if (!string.IsNullOrEmpty(request.InitialMessage))
            {
                var sendCmd = new SendMessage(
                    threadId, userId, userName, MessageRole.Teacher,
                    null, null,
                    new MessageContent(request.InitialMessage, "text", null, null),
                    MessageChannel.InApp, null);

                await nats.PublishAsync(
                    MessagingNatsSubjects.CmdSendMessage,
                    JsonSerializer.SerializeToUtf8Bytes(sendCmd, JsonOpts));
            }

            return Results.Created($"/api/admin/messaging/threads/{threadId}",
                new { ThreadId = threadId });
        }).WithName("CreateMessagingThread");

        group.MapPost("/threads/{threadId}/messages", async (
            string threadId,
            SendMessageRequest request,
            HttpContext httpContext,
            INatsConnection nats) =>
        {
            var userId = httpContext.User.FindFirst("user_id")?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var userName = httpContext.User.FindFirst("name")?.Value ?? "Admin";

            var sendCmd = new SendMessage(
                threadId, userId, userName, MessageRole.Teacher,
                null, null,
                new MessageContent(
                    request.Text,
                    request.ContentType ?? "text",
                    request.ResourceUrl, null),
                MessageChannel.InApp,
                request.ReplyToMessageId);

            await nats.PublishAsync(
                MessagingNatsSubjects.CmdSendMessage,
                JsonSerializer.SerializeToUtf8Bytes(sendCmd, JsonOpts));

            return Results.Accepted();
        }).WithName("SendMessageInThread");

        group.MapPut("/threads/{threadId}/mute", async (
            string threadId,
            HttpContext httpContext,
            INatsConnection nats) =>
        {
            var userId = httpContext.User.FindFirst("user_id")?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var muteCmd = new MuteThread(threadId, userId, DateTimeOffset.MaxValue);
            await nats.PublishAsync(
                $"cena.messaging.commands.MuteThread",
                JsonSerializer.SerializeToUtf8Bytes(muteCmd, JsonOpts));

            return Results.Ok();
        }).WithName("MuteMessagingThread");

        group.MapGet("/contacts", async (
            string? search,
            ClaimsPrincipal user,
            IMessagingAdminService service) =>
        {
            var result = await service.GetContactsAsync(search, user);
            return Results.Ok(result);
        }).WithName("GetMessagingContacts");

        return app;
    }
}
