// =============================================================================
// Cena Platform -- Notifications REST Endpoints (STB-07 Phase 1)
// Notifications list, unread count, and read operations (stub data)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Notifications;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
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

        group.MapGet("", GetNotifications).WithName("GetNotifications");
        group.MapGet("/unread-count", GetUnreadCount).WithName("GetUnreadCount");
        group.MapPost("/{id}/read", MarkAsRead).WithName("MarkNotificationRead");
        group.MapPost("/mark-all-read", MarkAllAsRead).WithName("MarkAllNotificationsRead");

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

        // Phase 1: Return 10 hardcoded items mixing 3 unread + 7 read
        // Kinds variety: 2 badges, 2 xp, 2 streak, 1 friend request, 1 review-due, 2 system
        // Timestamps descending from now
        var now = DateTime.UtcNow;
        const int pageSize = 10;
        var currentPage = Math.Max(1, page ?? 1);

        var allItems = new[]
        {
            // Unread notifications (3)
            new NotificationItem(
                NotificationId: "notif_001",
                Kind: "badge",
                Priority: "normal",
                Title: "New Badge Earned!",
                Body: "Congratulations! You've earned the 'Week Streak' badge.",
                IconName: "mdi-trophy",
                DeepLinkUrl: "/achievements",
                Read: false,
                CreatedAt: now.AddMinutes(-5)),
            new NotificationItem(
                NotificationId: "notif_002",
                Kind: "friend-request",
                Priority: "normal",
                Title: "New Friend Request",
                Body: "Casey wants to be your friend.",
                IconName: "mdi-account-plus",
                DeepLinkUrl: "/friends",
                Read: false,
                CreatedAt: now.AddMinutes(-30)),
            new NotificationItem(
                NotificationId: "notif_003",
                Kind: "review-due",
                Priority: "high",
                Title: "Review Due",
                Body: "You have 5 concepts ready for review. Keep your knowledge fresh!",
                IconName: "mdi-refresh",
                DeepLinkUrl: "/review",
                Read: false,
                CreatedAt: now.AddHours(-1)),
            
            // Read notifications (7)
            new NotificationItem(
                NotificationId: "notif_004",
                Kind: "xp",
                Priority: "low",
                Title: "XP Earned",
                Body: "You earned 150 XP from today's session!",
                IconName: "mdi-star",
                DeepLinkUrl: "/progress",
                Read: true,
                CreatedAt: now.AddHours(-2)),
            new NotificationItem(
                NotificationId: "notif_005",
                Kind: "badge",
                Priority: "normal",
                Title: "Badge Unlocked",
                Body: "You've unlocked the 'Quiz Master' badge!",
                IconName: "mdi-medal",
                DeepLinkUrl: "/achievements",
                Read: true,
                CreatedAt: now.AddHours(-3)),
            new NotificationItem(
                NotificationId: "notif_006",
                Kind: "streak",
                Priority: "normal",
                Title: "Streak Alert",
                Body: "You're on a 7-day streak! Keep it up!",
                IconName: "mdi-fire",
                DeepLinkUrl: "/home",
                Read: true,
                CreatedAt: now.AddHours(-5)),
            new NotificationItem(
                NotificationId: "notif_007",
                Kind: "xp",
                Priority: "low",
                Title: "Daily Goal Complete",
                Body: "You completed your daily goal of 30 minutes!",
                IconName: "mdi-check-circle",
                DeepLinkUrl: "/progress",
                Read: true,
                CreatedAt: now.AddHours(-8)),
            new NotificationItem(
                NotificationId: "notif_008",
                Kind: "system",
                Priority: "normal",
                Title: "New Content Available",
                Body: "New Algebra II content is now available!",
                IconName: "mdi-new-box",
                DeepLinkUrl: "/content",
                Read: true,
                CreatedAt: now.AddHours(-12)),
            new NotificationItem(
                NotificationId: "notif_009",
                Kind: "streak",
                Priority: "high",
                Title: "Streak at Risk!",
                Body: "Don't lose your 7-day streak! Study today.",
                IconName: "mdi-alert",
                DeepLinkUrl: "/home",
                Read: true,
                CreatedAt: now.AddHours(-20)),
            new NotificationItem(
                NotificationId: "notif_010",
                Kind: "system",
                Priority: "low",
                Title: "Weekly Summary",
                Body: "Your weekly progress summary is ready to view.",
                IconName: "mdi-chart-bar",
                DeepLinkUrl: "/analytics",
                Read: true,
                CreatedAt: now.AddDays(-1))
        };

        // Pagination: page 2 is empty
        NotificationItem[] items;
        bool hasMore;
        int total;

        if (currentPage == 1)
        {
            items = allItems;
            hasMore = false; // Only 10 items total
            total = allItems.Length;
        }
        else
        {
            items = Array.Empty<NotificationItem>();
            hasMore = false;
            total = allItems.Length;
        }

        var unreadCount = items.Count(i => !i.Read);

        var dto = new NotificationListDto(
            Items: items,
            Page: currentPage,
            PageSize: pageSize,
            Total: total,
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

        // Phase 1: Always return 3
        var dto = new UnreadCountDto(Count: 3);
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

        // Phase 1: Stub - does not actually persist
        // Just return success for any valid-looking ID
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { Error = "Notification ID is required" });
        }

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

        // Phase 1: Stub - returns markedCount: 3
        var response = new MarkAllReadResponse(Ok: true, MarkedCount: 3);
        return Results.Ok(response);
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
