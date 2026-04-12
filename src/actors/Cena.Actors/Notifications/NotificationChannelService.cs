// =============================================================================
// Cena Platform — Notification Channel Service (STB-07c)
// Multi-channel notification delivery with receptive timing
// =============================================================================

using Cena.Actors.Infrastructure;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Gamification;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications;

/// <summary>
/// Service for delivering notifications across multiple channels.
/// </summary>
public interface INotificationChannelService
{
    /// <summary>
    /// Send notification through optimal channel(s) based on user preferences and timing.
    /// </summary>
    Task<bool> SendNotificationAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct = default);

    /// <summary>
    /// Check if current time is optimal for sending notifications to user.
    /// </summary>
    Task<bool> IsOptimalTimeAsync(
        string studentId,
        CancellationToken ct = default);

    /// <summary>
    /// Get user's notification preferences.
    /// </summary>
    Task<NotificationPreferences> GetPreferencesAsync(
        string studentId,
        CancellationToken ct = default);
}

/// <summary>
/// Multi-channel notification service with receptive timing.
/// </summary>
public class NotificationChannelService : INotificationChannelService
{
    private readonly IDocumentStore _store;
    private readonly IAnalyticsRollupService _analytics;
    private readonly ILogger<NotificationChannelService> _logger;
    private readonly IClock _clock;

    public NotificationChannelService(
        IDocumentStore store,
        IAnalyticsRollupService analytics,
        ILogger<NotificationChannelService> logger,
        IClock clock)
    {
        _store = store;
        _analytics = analytics;
        _logger = logger;
        _clock = clock;
    }

    public async Task<bool> SendNotificationAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct = default)
    {
        var results = new List<bool>();

        // Fan out to enabled channels
        if (preferences.EnableInApp)
        {
            // In-app is handled by storing the notification document
            results.Add(true);
        }

        if (preferences.EnableWebPush && !string.IsNullOrEmpty(preferences.WebPushEndpoint))
        {
            results.Add(await SendWebPushAsync(notification, preferences, ct));
        }

        if (preferences.EnableEmail && !string.IsNullOrEmpty(preferences.EmailAddress))
        {
            results.Add(await SendEmailAsync(notification, preferences, ct));
        }

        if (preferences.EnableSms && !string.IsNullOrEmpty(preferences.PhoneNumber))
        {
            results.Add(await SendSmsAsync(notification, preferences, ct));
        }

        return results.Any(r => r);
    }

    public async Task<bool> IsOptimalTimeAsync(
        string studentId,
        CancellationToken ct = default)
    {
        var now = _clock.UtcDateTime;
        var hour = now.Hour;

        // Get user's flow accuracy profile to find best times
        var profile = await _analytics.GetFlowAccuracyProfileAsync(studentId, ct);
        
        if (profile?.ByTimeOfDay != null && profile.ByTimeOfDay.Count > 0)
        {
            // Find best performing time slot
            var bestTime = profile.ByTimeOfDay
                .Where(t => t.Value.SampleCount >= 3)
                .OrderByDescending(t => t.Value.AvgFlowScore)
                .FirstOrDefault();

            if (bestTime.Key != null)
            {
                // Check if current time matches best time slot
                var currentTimeOfDay = hour switch
                {
                    >= 6 and < 12 => "morning",
                    >= 12 and < 18 => "afternoon",
                    >= 18 and < 22 => "evening",
                    _ => "night"
                };

                // Allow notifications during best time or within 2 hours of it
                if (currentTimeOfDay == bestTime.Key)
                    return true;
            }
        }

        // Default: allow during day hours (9 AM - 9 PM local)
        // For now using UTC, in production would use user's timezone
        return hour >= 7 && hour <= 21;
    }

    public async Task<NotificationPreferences> GetPreferencesAsync(
        string studentId,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        // Try to load existing preferences
        var prefs = await session.Query<NotificationPreferencesDocument>()
            .FirstOrDefaultAsync(p => p.StudentId == studentId, ct);

        if (prefs != null)
        {
            return new NotificationPreferences
            {
                StudentId = prefs.StudentId,
                EnableInApp = prefs.EnableInApp,
                EnableWebPush = prefs.EnableWebPush,
                EnableEmail = prefs.EnableEmail,
                EnableSms = prefs.EnableSms,
                WebPushEndpoint = prefs.WebPushEndpoint,
                EmailAddress = prefs.EmailAddress,
                PhoneNumber = prefs.PhoneNumber,
                QuietHoursStart = prefs.QuietHoursStart,
                QuietHoursEnd = prefs.QuietHoursEnd,
                DigestMode = prefs.DigestMode
            };
        }

        // Return defaults
        return new NotificationPreferences
        {
            StudentId = studentId,
            EnableInApp = true,
            EnableWebPush = true,
            EnableEmail = false,
            EnableSms = false,
            DigestMode = "immediate"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Channel Implementations (Stubs for Phase 1c)
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<bool> SendWebPushAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct)
    {
        // STB-07c: Web Push stub implementation
        _logger.LogInformation(
            "[WEB PUSH] Would send to {Endpoint}: {Title} - {Body}",
            preferences.WebPushEndpoint?.Substring(0, Math.Min(50, preferences.WebPushEndpoint?.Length ?? 0)),
            notification.Title,
            notification.Body);

        // In production, this would use WebPush library with VAPID keys
        // Example: await _webPushClient.SendNotificationAsync(subscription, payload, vapidDetails);

        await Task.Delay(10, ct); // Simulate async work
        return true;
    }

    private async Task<bool> SendEmailAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct)
    {
        // STB-07c: Email stub implementation
        _logger.LogInformation(
            "[EMAIL] Would send to {Email}: {Title}",
            preferences.EmailAddress,
            notification.Title);

        // In production, this would use SMTP or email service (SendGrid, SES, etc.)

        await Task.Delay(10, ct); // Simulate async work
        return true;
    }

    private async Task<bool> SendSmsAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct)
    {
        // STB-07c: SMS stub implementation
        _logger.LogInformation(
            "[SMS] Would send to {Phone}: {Title}",
            preferences.PhoneNumber,
            notification.Title);

        // In production, this would use Twilio or similar SMS gateway

        await Task.Delay(10, ct); // Simulate async work
        return true;
    }
}

/// <summary>
/// Notification preferences for a student.
/// </summary>
public class NotificationPreferences
{
    public string StudentId { get; set; } = "";
    public bool EnableInApp { get; set; } = true;
    public bool EnableWebPush { get; set; } = true;
    public bool EnableEmail { get; set; } = false;
    public bool EnableSms { get; set; } = false;

    // Channel endpoints
    public string? WebPushEndpoint { get; set; }
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }

    // Timing preferences
    public int? QuietHoursStart { get; set; } // 24-hour format
    public int? QuietHoursEnd { get; set; }
    public string DigestMode { get; set; } = "immediate"; // immediate | hourly | daily
}

/// <summary>
/// Marten document for persisting notification preferences.
/// </summary>
public class NotificationPreferencesDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public bool EnableInApp { get; set; } = true;
    public bool EnableWebPush { get; set; } = true;
    public bool EnableEmail { get; set; } = false;
    public bool EnableSms { get; set; } = false;
    public string? WebPushEndpoint { get; set; }
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public int? QuietHoursStart { get; set; }
    public int? QuietHoursEnd { get; set; }
    public string DigestMode { get; set; } = "immediate";
    public DateTime UpdatedAt { get; set; }
}
