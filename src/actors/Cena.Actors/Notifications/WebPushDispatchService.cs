// =============================================================================
// Cena Platform — Web Push Dispatch Service (PWA-BE-002)
// Sends push notifications to all active student subscriptions with
// Redis-backed rate limiting and auto-cleanup of expired endpoints.
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications;

/// <summary>
/// Dispatches push notifications to student devices with rate limiting
/// and automatic removal of expired subscriptions.
/// </summary>
public interface IWebPushDispatchService
{
    /// <summary>
    /// Sends a push notification to all active subscriptions for a student,
    /// subject to daily/weekly rate limits and per-type preferences.
    /// </summary>
    Task<bool> DispatchAsync(
        string studentId,
        string notificationType,
        string title,
        string body,
        string? icon = null,
        string? deepLink = null,
        CancellationToken ct = default);
}

/// <summary>
/// Production implementation using IWebPushClient, IDocumentStore, and Redis rate limits.
/// </summary>
public class WebPushDispatchService : IWebPushDispatchService
{
    private readonly IDocumentStore _store;
    private readonly IWebPushClient _webPush;
    private readonly IPushNotificationRateLimiter _rateLimiter;
    private readonly ILogger<WebPushDispatchService> _logger;

    public WebPushDispatchService(
        IDocumentStore store,
        IWebPushClient webPush,
        IPushNotificationRateLimiter rateLimiter,
        ILogger<WebPushDispatchService> logger)
    {
        _store = store;
        _webPush = webPush;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<bool> DispatchAsync(
        string studentId,
        string notificationType,
        string title,
        string body,
        string? icon = null,
        string? deepLink = null,
        CancellationToken ct = default)
    {
        if (!_webPush.IsConfigured)
        {
            _logger.LogWarning("WebPush not configured — skipping dispatch for {StudentId}", studentId);
            return false;
        }

        // Check per-type preference
        if (!await IsTypeEnabledAsync(studentId, notificationType, ct))
        {
            _logger.LogInformation(
                "Notification type {NotificationType} disabled for {StudentId} — skipping",
                notificationType, studentId);
            return false;
        }

        // Check rate limit
        if (!await _rateLimiter.CanSendAsync(studentId, ct))
        {
            _logger.LogWarning(
                "Push notification rate limit exceeded for {StudentId}", studentId);
            return false;
        }

        var subscriptions = await GetSubscriptionsAsync(studentId, ct);

        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("No push subscriptions for {StudentId}", studentId);
            return false;
        }

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            icon,
            url = deepLink,
            type = notificationType
        });

        await using var session = _store.LightweightSession();
        var anySuccess = false;
        foreach (var sub in subscriptions)
        {
            var result = await _webPush.SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct);
            if (result.Success)
            {
                anySuccess = true;
                sub.LastUsedAt = DateTime.UtcNow;
                session.Store(sub);
                _logger.LogInformation(
                    "Push delivered to {StudentId} at {Endpoint}",
                    studentId, MaskEndpoint(sub.Endpoint));
            }
            else if (result.ErrorCode == "SUBSCRIPTION_GONE")
            {
                session.Delete(sub);
                OnSubscriptionDeleted(sub);
                _logger.LogInformation(
                    "Removed expired push subscription for {StudentId}: {Endpoint}",
                    studentId, MaskEndpoint(sub.Endpoint));
            }
            else
            {
                _logger.LogWarning(
                    "Push failed for {StudentId}: {ErrorCode} — {ErrorMessage}",
                    studentId, result.ErrorCode, result.ErrorMessage);
            }
        }

        if (anySuccess)
        {
            await _rateLimiter.RecordSentAsync(studentId, ct);
        }

        await session.SaveChangesAsync(ct);
        return anySuccess;
    }

    /// <summary>
    /// Looks up push subscriptions for a student. Virtual so tests can substitute
    /// the lookup without a live Marten LINQ provider.
    /// </summary>
    protected virtual async Task<IReadOnlyList<WebPushSubscriptionDocument>> GetSubscriptionsAsync(
        string studentId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        return await session
            .Query<WebPushSubscriptionDocument>()
            .Where(s => s.StudentId == studentId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Checks whether the given notification type is enabled for the student.
    /// Virtual so tests can substitute preferences without wiring up Marten.
    /// </summary>
    protected virtual async Task<bool> IsTypeEnabledAsync(string studentId, string notificationType, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var prefs = await session
            .Query<global::Cena.Infrastructure.Documents.NotificationPreferencesDocument>()
            .FirstOrDefaultAsync(p => p.StudentId == studentId, ct);

        if (prefs == null)
        {
            // Defaults: all enabled except weekly summary
            return notificationType.ToLowerInvariant() != "weeklysummary";
        }

        return notificationType.ToLowerInvariant() switch
        {
            "sessionreminder" => prefs.SessionReminderEnabled,
            "assignmentdue" => prefs.AssignmentDueEnabled,
            "teachermessage" => prefs.TeacherMessageEnabled,
            "weeklysummary" => prefs.WeeklySummaryEnabled,
            "mastermilestone" => prefs.MasteryMilestoneEnabled,
            _ => true
        };
    }

    /// <summary>
    /// Hook called when an expired subscription is deleted. Virtual for testability.
    /// </summary>
    protected virtual void OnSubscriptionDeleted(WebPushSubscriptionDocument sub) { }

    private static string MaskEndpoint(string endpoint)
    {
        if (endpoint.Length <= 20) return endpoint;
        return endpoint[..20] + "...";
    }
}
