// =============================================================================
// Cena Platform -- Notifications REST Endpoints (STB-07 + STB-07b)
// Notifications list, unread count, read operations, and write endpoints
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Notifications;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        // Phase 1 (STB-07): Read endpoints
        group.MapGet("", GetNotifications).WithName("GetNotifications");
        group.MapGet("/unread-count", GetUnreadCount).WithName("GetUnreadCount");
        group.MapPost("/{id}/read", MarkAsRead).WithName("MarkNotificationRead");
        group.MapPost("/mark-all-read", MarkAllAsRead).WithName("MarkAllNotificationsRead");

        // Phase 1b (STB-07b): Write endpoints
        group.MapDelete("/{id}", DeleteNotification).WithName("DeleteNotification");
        group.MapPost("/{id}/snooze", SnoozeNotification).WithName("SnoozeNotification");
        group.MapPost("/test", CreateTestNotification).WithName("CreateTestNotification");

        // Web Push endpoints
        group.MapPost("/web-push/subscribe", SubscribeWebPush).WithName("SubscribeWebPush");
        group.MapPost("/web-push/unsubscribe", UnsubscribeWebPush).WithName("UnsubscribeWebPush");

        return app;
    }

    // GET /api/notifications?filter=all&page=1 — returns notification list
    private static async Task<IResult> GetNotifications(
        HttpContext ctx,
        IDocumentStore store,
        string? filter = "all",
        int? page = 1)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        // Query visible notifications (not deleted, not snoozed)
        var query = session.Query<NotificationDocument>()
            .Where(n => n.StudentId == studentId && n.DeletedAt == null);

        if (filter == "unread")
            query = query.Where(n => !n.Read);
        else if (filter == "read")
            query = query.Where(n => n.Read);

        var now = DateTime.UtcNow;
        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        // Filter out snoozed notifications
        var visible = notifications
            .Where(n => n.SnoozedUntil == null || n.SnoozedUntil < now)
            .Select(n => new NotificationItem(
                NotificationId: n.NotificationId,
                Kind: n.Kind,
                Priority: n.Priority,
                Title: n.Title,
                Body: n.Body,
                IconName: n.IconName ?? "mdi-bell",
                DeepLinkUrl: n.DeepLinkUrl,
                Read: n.Read,
                CreatedAt: n.CreatedAt))
            .ToList();

        const int pageSize = 10;
        var currentPage = Math.Max(1, page ?? 1);
        var skip = (currentPage - 1) * pageSize;
        var pagedItems = visible.Skip(skip).Take(pageSize).ToArray();
        var hasMore = visible.Count > skip + pagedItems.Length;

        var unreadCount = visible.Count(n => !n.Read);

        var dto = new NotificationListDto(
            Items: pagedItems,
            Page: currentPage,
            PageSize: pageSize,
            Total: visible.Count,
            HasMore: hasMore,
            UnreadCount: unreadCount);

        return Results.Ok(dto);
    }

    // GET /api/notifications/unread-count — returns unread count
    private static async Task<IResult> GetUnreadCount(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        var now = DateTime.UtcNow;
        var count = await session.Query<NotificationDocument>()
            .CountAsync(n => n.StudentId == studentId
                && !n.Read
                && n.DeletedAt == null
                && (n.SnoozedUntil == null || n.SnoozedUntil < now));

        var dto = new UnreadCountDto(Count: count);
        return Results.Ok(dto);
    }

    // POST /api/notifications/{id}/read — marks notification as read
    private static async Task<IResult> MarkAsRead(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { Error = "Notification ID is required" });

        await using var session = store.LightweightSession();

        var notification = await session.Query<NotificationDocument>()
            .FirstOrDefaultAsync(n => n.StudentId == studentId && n.NotificationId == id);

        if (notification == null)
            return Results.NotFound(new { Error = "Notification not found" });

        notification.Read = true;
        session.Store(notification);
        await session.SaveChangesAsync();

        var response = new MarkReadResponse(Ok: true, Id: id);
        return Results.Ok(response);
    }

    // POST /api/notifications/mark-all-read — marks all notifications as read
    private static async Task<IResult> MarkAllAsRead(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        var now = DateTime.UtcNow;
        var unread = await session.Query<NotificationDocument>()
            .Where(n => n.StudentId == studentId
                && !n.Read
                && n.DeletedAt == null
                && (n.SnoozedUntil == null || n.SnoozedUntil < now))
            .ToListAsync();

        foreach (var n in unread)
            n.Read = true;

        session.StoreObjects(unread);
        await session.SaveChangesAsync();

        var response = new MarkAllReadResponse(Ok: true, MarkedCount: unread.Count);
        return Results.Ok(response);
    }

    // DELETE /api/notifications/{id} — soft delete notification (STB-07b)
    private static async Task<IResult> DeleteNotification(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { Error = "Notification ID is required" });

        await using var session = store.LightweightSession();

        var notification = await session.Query<NotificationDocument>()
            .FirstOrDefaultAsync(n => n.StudentId == studentId && n.NotificationId == id);

        if (notification == null)
            return Results.NotFound(new { Error = "Notification not found" });

        // Soft delete by setting DeletedAt
        notification.DeletedAt = DateTime.UtcNow;
        session.Store(notification);

        // Append event for audit
        var evt = new NotificationDeleted_V1(studentId, id, DateTimeOffset.UtcNow);
        session.Events.Append(studentId, evt);

        await session.SaveChangesAsync();

        return Results.NoContent();
    }

    // POST /api/notifications/{id}/snooze — snooze notification (STB-07b)
    private static async Task<IResult> SnoozeNotification(
        HttpContext ctx,
        IDocumentStore store,
        string id,
        SnoozeRequestDto dto)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { Error = "Notification ID is required" });

        await using var session = store.LightweightSession();

        var notification = await session.Query<NotificationDocument>()
            .FirstOrDefaultAsync(n => n.StudentId == studentId && n.NotificationId == id);

        if (notification == null)
            return Results.NotFound(new { Error = "Notification not found" });

        // Parse duration (default 24 hours)
        var duration = ParseSnoozeDuration(dto.DurationMinutes ?? 1440);
        var snoozedUntil = DateTime.UtcNow.Add(duration);

        notification.SnoozedUntil = snoozedUntil;
        session.Store(notification);

        // Append event for audit
        var evt = new NotificationSnoozed_V1(studentId, id, snoozedUntil, DateTimeOffset.UtcNow);
        session.Events.Append(studentId, evt);

        await session.SaveChangesAsync();

        return Results.Ok(new { SnoozedUntil = snoozedUntil });
    }

    // POST /api/notifications/test — create test notification (STB-07b)
    private static async Task<IResult> CreateTestNotification(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        var notificationId = Guid.NewGuid().ToString("N")[..16];
        var notification = new NotificationDocument
        {
            Id = $"notif/{notificationId}",
            NotificationId = notificationId,
            StudentId = studentId,
            Kind = "system",
            Priority = "normal",
            Title = "Test Notification",
            Body = "This is a test notification created via the API.",
            IconName = "mdi-bell-alert",
            Read = false,
            CreatedAt = DateTime.UtcNow
        };

        session.Store(notification);
        await session.SaveChangesAsync();

        return Results.Ok(new { NotificationId = notificationId, CreatedAt = notification.CreatedAt });
    }

    // POST /api/notifications/web-push/subscribe (STB-07b)
    private static async Task<IResult> SubscribeWebPush(
        HttpContext ctx,
        IDocumentStore store,
        WebPushSubscribeDto dto)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(dto.Endpoint))
            return Results.BadRequest(new { Error = "Endpoint is required" });

        if (string.IsNullOrWhiteSpace(dto.Keys?.P256dh))
            return Results.BadRequest(new { Error = "P256dh key is required" });

        if (string.IsNullOrWhiteSpace(dto.Keys?.Auth))
            return Results.BadRequest(new { Error = "Auth key is required" });

        await using var session = store.LightweightSession();

        // Check for existing subscription
        var existing = await session.Query<WebPushSubscriptionDocument>()
            .FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint);

        if (existing != null)
        {
            // Update keys if different student (edge case)
            if (existing.StudentId != studentId)
            {
                existing.StudentId = studentId;
                existing.P256dh = dto.Keys.P256dh;
                existing.Auth = dto.Keys.Auth;
                session.Store(existing);
            }
        }
        else
        {
            var subscriptionId = Guid.NewGuid().ToString("N")[..16];
            var subscription = new WebPushSubscriptionDocument
            {
                Id = $"push/{subscriptionId}",
                StudentId = studentId,
                Endpoint = dto.Endpoint,
                P256dh = dto.Keys.P256dh,
                Auth = dto.Keys.Auth,
                CreatedAt = DateTime.UtcNow
            };
            session.Store(subscription);
        }

        // Append event for audit
        var evt = new WebPushSubscribed_V1(studentId, Guid.NewGuid().ToString("N")[..16], dto.Endpoint, DateTimeOffset.UtcNow);
        session.Events.Append(studentId, evt);

        await session.SaveChangesAsync();

        return Results.Ok(new { Subscribed = true });
    }

    // POST /api/notifications/web-push/unsubscribe (STB-07b)
    private static async Task<IResult> UnsubscribeWebPush(
        HttpContext ctx,
        IDocumentStore store,
        WebPushUnsubscribeDto dto)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(dto.Endpoint))
            return Results.BadRequest(new { Error = "Endpoint is required" });

        await using var session = store.LightweightSession();

        var subscription = await session.Query<WebPushSubscriptionDocument>()
            .FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint && s.StudentId == studentId);

        if (subscription != null)
        {
            session.Delete(subscription);

            // Append event for audit
            var evt = new WebPushUnsubscribed_V1(studentId, dto.Endpoint, DateTimeOffset.UtcNow);
            session.Events.Append(studentId, evt);

            await session.SaveChangesAsync();
        }

        return Results.Ok(new { Unsubscribed = true });
    }

    private static TimeSpan ParseSnoozeDuration(int minutes)
    {
        return minutes switch
        {
            <= 0 => TimeSpan.FromHours(24),
            <= 60 => TimeSpan.FromMinutes(minutes),
            <= 1440 => TimeSpan.FromHours(minutes / 60),
            _ => TimeSpan.FromDays(1)
        };
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}

// DTOs
public record SnoozeRequestDto(int? DurationMinutes = null);

public record WebPushSubscribeDto(
    string Endpoint,
    WebPushKeysDto? Keys
);

public record WebPushKeysDto(
    string P256dh,
    string Auth
);

public record WebPushUnsubscribeDto(string Endpoint);
