// =============================================================================
// Cena Platform — Notification Dispatcher (STB-07b)
// Listens to events and creates in-app + push notifications
//
// Subscribes to per-student XP events using the typed subject registry. The
// publisher (SessionNatsPublisher.PublishXpAwardedAsync) emits on
//   cena.events.student.{studentId}.xp_awarded
// so this dispatcher subscribes on the wildcard
//   cena.events.student.*.xp_awarded
// and trusts the NATS subject — not the payload — as the source of truth for
// studentId. A drift between publisher and subscriber silently drops every
// XP notification (see FIND-arch-002).
// =============================================================================

using Marten;
using NATS.Client.Core;
using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Cena.Actors.Notifications;

/// <summary>
/// Listens to event stream and dispatches notifications.
/// </summary>
public class NotificationDispatcher : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IDocumentStore _store;
    private readonly INotificationChannelService? _channelService;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        INatsConnection nats,
        IDocumentStore store,
        ILogger<NotificationDispatcher> logger,
        INotificationChannelService? channelService = null)
    {
        _nats = nats;
        _store = store;
        _logger = logger;
        _channelService = channelService;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Start both subscriptions in parallel
        var xpTask = SubscribeToXpEventsAsync(ct);
        var moderationTask = SubscribeToModerationEventsAsync(ct);
        
        await Task.WhenAll(xpTask, moderationTask);
    }

    private async Task SubscribeToXpEventsAsync(CancellationToken ct)
    {
        var subject = NatsSubjects.StudentEventTypeWildcard(NatsSubjects.StudentXpAwarded);
        _logger.LogInformation(
            "Notification Dispatcher started — subscribing to {Subject}", subject);

        // Subscribe to per-student XP events via NATS subject wildcard.
        // Pattern: cena.events.student.*.xp_awarded
        await foreach (var msg in _nats.SubscribeAsync<XpAwarded_V1>(
            subject, cancellationToken: ct))
        {
            if (msg.Data == null)
            {
                _logger.LogWarning(
                    "XP event on {Subject} had null payload — ignoring", msg.Subject);
                continue;
            }

            // Trust the subject, not the payload, for studentId. A malicious or
            // buggy publisher cannot inject events for a different student because
            // the subject is validated by NATS against the subscription pattern.
            var studentId = NatsSubjects.TryParseStudentIdFromSubject(msg.Subject);
            if (string.IsNullOrEmpty(studentId))
            {
                _logger.LogWarning(
                    "XP event on {Subject} had unparseable studentId — ignoring", msg.Subject);
                continue;
            }

            try
            {
                await HandleXpAwardedAsync(studentId, msg.Data, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error handling XP awarded event for student {StudentId}", studentId);
            }
        }
    }

    private async Task SubscribeToModerationEventsAsync(CancellationToken ct)
    {
        // Subscribe to content moderation events for audit logging
        // Pattern: cena.review.item.approved and cena.review.item.rejected
        var approvedSub = _nats.SubscribeAsync<byte[]>("cena.review.item.approved", cancellationToken: ct);
        var rejectedSub = _nats.SubscribeAsync<byte[]>("cena.review.item.rejected", cancellationToken: ct);

        var approvedTask = HandleModerationEventsAsync(approvedSub, "approved", ct);
        var rejectedTask = HandleModerationEventsAsync(rejectedSub, "rejected", ct);

        await Task.WhenAll(approvedTask, rejectedTask);
    }

    private async Task HandleModerationEventsAsync(
        IAsyncEnumerable<NatsMsg<byte[]>> subscription, 
        string action, 
        CancellationToken ct)
    {
        await foreach (var msg in subscription)
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<ModerationEventPayload>(msg.Data);
                if (payload != null)
                {
                    _logger.LogInformation(
                        "Content moderation: question {QuestionId} {Action} by moderator {ModeratorId}",
                        payload.QuestionId, action, payload.ModeratorId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process moderation {Action} event", action);
            }
        }
    }

    private record ModerationEventPayload(string QuestionId, string ModeratorId, string? Reason);

    internal async Task HandleXpAwardedAsync(string studentId, XpAwarded_V1 evt, CancellationToken ct)
    {
        // Persist the in-app notification via the injected store abstraction
        // so the handler is unit-testable without a live Marten connection.
        var notification = BuildNotification(studentId, evt);
        await PersistNotificationAsync(notification, ct);

        // Dispatch to external channels (best-effort, never blocks in-app persistence)
        if (_channelService != null)
        {
            try
            {
                var prefs = await _channelService.GetPreferencesAsync(studentId, ct);
                var sent = await _channelService.SendNotificationAsync(notification, prefs, ct);
                _logger.LogInformation(
                    "Notification dispatched via channel service. " +
                    "StudentId={StudentId}, NotificationId={NotificationId}, Result={Result}",
                    studentId, notification.NotificationId, sent ? "delivered" : "no_external_channels");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "External channel dispatch failed (in-app notification preserved). " +
                    "StudentId={StudentId}, NotificationId={NotificationId}, ErrorCode={ErrorCode}",
                    studentId, notification.NotificationId, "CHANNEL_DISPATCH_ERROR");
            }
        }
    }

    /// <summary>
    /// Build the NotificationDocument for an XP event. Pure function — easy to
    /// unit-test assertions about title/body/kind.
    /// </summary>
    internal static NotificationDocument BuildNotification(string studentId, XpAwarded_V1 evt)
    {
        var notificationId = Guid.NewGuid().ToString("N")[..16];
        return new NotificationDocument
        {
            Id = $"notif/{notificationId}",
            NotificationId = notificationId,
            StudentId = studentId,
            Kind = "xp",
            Priority = "normal",
            Title = "XP Gained!",
            Body = $"You earned {evt.XpAmount} XP from {evt.Source}",
            IconName = "award",
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Persist a notification through the configured document store. Virtual so
    /// tests can substitute persistence without wiring up Marten.
    /// </summary>
    protected virtual async Task PersistNotificationAsync(
        NotificationDocument notification, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        session.Store(notification);
        await session.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Count web-push subscriptions for a student. Virtual so tests can
    /// substitute the lookup without a live Marten LINQ provider.
    /// </summary>
    protected virtual async Task<int> CountPushSubscriptionsAsync(
        string studentId, CancellationToken ct)
    {
        try
        {
            await using var session = _store.QuerySession();
            var subscriptions = await session
                .Query<WebPushSubscriptionDocument>()
                .Where(s => s.StudentId == studentId)
                .ToListAsync(ct);
            return subscriptions.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to look up push subscriptions for student {StudentId}", studentId);
            return 0;
        }
    }
}
