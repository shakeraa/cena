// =============================================================================
// Cena Platform — Push Notification Trigger Service (PWA-BE-002)
// Background service that triggers the five PWA notification types.
// =============================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Cena.Infrastructure.Time;

namespace Cena.Actors.Notifications;

/// <summary>
/// Hosts timers and event hooks for the five push notification types:
/// session reminder, assignment due, teacher message, weekly summary, mastery milestone.
/// </summary>
public sealed class PushNotificationTriggerService : BackgroundService
{
    private readonly IWebPushDispatchService _dispatch;
    private readonly ILogger<PushNotificationTriggerService> _logger;

    public PushNotificationTriggerService(
        IWebPushDispatchService dispatch,
        ILogger<PushNotificationTriggerService> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("PushNotificationTriggerService started");

        // Run scheduled checks every 5 minutes
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckScheduledNotificationsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduled notification check failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckScheduledNotificationsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Weekly summary: Sunday 08:00 Israel time (prr-157: cross-platform resolver)
        var ilTime = IsraelTimeZoneResolver.ConvertFromUtc(now);
        if (ilTime.DayOfWeek == DayOfWeek.Sunday && ilTime.Hour == 8 && ilTime.Minute < 5)
        {
            await SendWeeklySummariesAsync(ct);
        }

        // Session reminders and assignment deadlines require domain documents
        // that are not yet in the model (scheduled sessions, assignments).
        // When those documents are added, query them here and call:
        //   await SendSessionReminderAsync(studentId, sessionTitle, ct);
        //   await SendAssignmentDueAsync(studentId, assignmentTitle, hoursRemaining, ct);
    }

    // ── Public trigger methods ──

    /// <summary>
    /// Triggered 15 minutes before a scheduled session starts.
    /// </summary>
    public Task SendSessionReminderAsync(string studentId, string sessionTitle, CancellationToken ct = default)
    {
        return _dispatch.DispatchAsync(
            studentId,
            "sessionreminder",
            "Ready to learn?",
            $"Your {sessionTitle} session starts soon",
            icon: "mdi-book-open",
            deepLink: "/sessions",
            ct);
    }

    /// <summary>
    /// Triggered 24 hours before an assignment deadline.
    /// </summary>
    public Task SendAssignmentDueAsync(string studentId, string assignmentTitle, int hoursRemaining, CancellationToken ct = default)
    {
        return _dispatch.DispatchAsync(
            studentId,
            "assignmentdue",
            "Assignment due soon",
            $"{assignmentTitle} is due in {hoursRemaining} hours",
            icon: "mdi-clipboard-text",
            deepLink: "/assignments",
            ct);
    }

    /// <summary>
    /// Triggered immediately when a teacher sends a message.
    /// </summary>
    public Task SendTeacherMessageAsync(string studentId, string messagePreview, CancellationToken ct = default)
    {
        return _dispatch.DispatchAsync(
            studentId,
            "teachermessage",
            "New message from your teacher",
            messagePreview,
            icon: "mdi-message-text",
            deepLink: "/messages",
            ct);
    }

    /// <summary>
    /// Triggered on Sunday mornings for the weekly summary.
    /// </summary>
    private async Task SendWeeklySummariesAsync(CancellationToken ct)
    {
        // In production this would query all students with push enabled.
        // Stub: the dispatch service handles per-student preferences.
        _logger.LogInformation("Weekly summary push notification cycle started");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Triggered when a student masters a skill.
    /// </summary>
    public Task SendMasteryMilestoneAsync(string studentId, string skillName, CancellationToken ct = default)
    {
        return _dispatch.DispatchAsync(
            studentId,
            "mastermilestone",
            "Milestone reached!",
            $"You mastered {skillName}!",
            icon: "mdi-trophy",
            deepLink: "/progress",
            ct);
    }
}
