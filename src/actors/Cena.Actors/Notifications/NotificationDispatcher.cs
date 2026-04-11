// =============================================================================
// Cena Platform — Notification Dispatcher (STB-07b)
// Listens to events and creates in-app + push notifications
// =============================================================================

using Marten;
using Marten.Events;
using NATS.Client.Core;
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
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        INatsConnection nats,
        IDocumentStore store,
        ILogger<NotificationDispatcher> logger)
    {
        _nats = nats;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Notification Dispatcher started");

        // Subscribe to XP awarded events
        await foreach (var msg in _nats.SubscribeAsync<XpAwarded_V1>("events.xp.awarded", cancellationToken: ct))
        {
            if (msg.Data == null) continue;

            try
            {
                await HandleXpAwardedAsync(msg.Data, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling XP awarded event");
            }
        }
    }

    private async Task HandleXpAwardedAsync(XpAwarded_V1 evt, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();

        // Create in-app notification
        var notificationId = Guid.NewGuid().ToString("N")[..16];
        var notification = new NotificationDocument
        {
            Id = $"notif/{notificationId}",
            NotificationId = notificationId,
            StudentId = evt.StudentId,
            Kind = "xp",
            Priority = "normal",
            Title = "XP Gained!",
            Body = $"You earned {evt.XpAmount} XP from {evt.Source}",
            IconName = "award",
            CreatedAt = DateTime.UtcNow
        };

        session.Store(notification);
        await session.SaveChangesAsync(ct);

        // Try to send web push notification
        var subscriptions = await session
            .Query<WebPushSubscriptionDocument>()
            .Where(s => s.StudentId == evt.StudentId)
            .ToListAsync(ct);

        if (subscriptions.Count > 0)
        {
            _logger.LogInformation(
                "Would send push to {Count} subscriptions for student {StudentId}",
                subscriptions.Count, evt.StudentId);
            // Actual push sending would happen here with a Web Push library
        }
    }
}
