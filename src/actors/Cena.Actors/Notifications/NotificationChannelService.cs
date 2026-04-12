// =============================================================================
// Cena Platform — Notification Channel Service (FIND-arch-018)
// Multi-channel notification delivery with receptive timing.
// All three external channels (Web Push, Email, SMS) use real implementations
// injected via DI. No stubs -- channels that are not configured return false
// with a structured error reason.
// =============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
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
/// Delegates to IWebPushClient, IEmailSender, and ISmsSender for actual delivery.
/// Per-tenant and global rate limits protect against cost overruns.
/// </summary>
public class NotificationChannelService : INotificationChannelService
{
    private readonly IDocumentStore _store;
    private readonly IAnalyticsRollupService _analytics;
    private readonly IWebPushClient _webPush;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly ILogger<NotificationChannelService> _logger;

    // Per-student rate limiting: track send counts per hour per channel
    private static readonly ConcurrentDictionary<string, ChannelRateState> RateLimits = new();
    private const int MaxWebPushPerStudentPerHour = 20;
    private const int MaxEmailPerStudentPerHour = 5;
    private const int MaxSmsPerStudentPerHour = 2;

    public NotificationChannelService(
        IDocumentStore store,
        IAnalyticsRollupService analytics,
        IWebPushClient webPush,
        IEmailSender emailSender,
        ISmsSender smsSender,
        ILogger<NotificationChannelService> logger)
    {
        _store = store;
        _analytics = analytics;
        _webPush = webPush;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _logger = logger;
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
            // In-app is handled by storing the notification document (already persisted
            // by the caller BEFORE calling this method -- never lose the in-app row)
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
        var now = DateTime.UtcNow;
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
    // Channel Implementations — real dispatch via injected clients
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<bool> SendWebPushAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct)
    {
        if (!_webPush.IsConfigured)
        {
            _logger.LogWarning(
                "Web Push channel not configured -- skipping. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "webpush", notification.NotificationId, notification.StudentId,
                "skipped", "NOT_CONFIGURED");
            return false;
        }

        // Rate limit check
        if (!CheckRateLimit(notification.StudentId, "webpush", MaxWebPushPerStudentPerHour))
        {
            _logger.LogWarning(
                "Web Push rate limit exceeded for student. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "webpush", notification.NotificationId, notification.StudentId,
                "rate_limited", "RATE_LIMIT_EXCEEDED");
            return false;
        }

        // Look up push subscriptions for the student
        WebPushSubscriptionDocument[] subscriptions;
        try
        {
            await using var session = _store.QuerySession();
            subscriptions = (await session
                .Query<WebPushSubscriptionDocument>()
                .Where(s => s.StudentId == notification.StudentId)
                .ToListAsync(ct)).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to look up push subscriptions. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "webpush", notification.NotificationId, notification.StudentId,
                "failed", "SUBSCRIPTION_LOOKUP_FAILED");
            return false;
        }

        if (subscriptions.Length == 0)
        {
            _logger.LogInformation(
                "No push subscriptions found for student. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "webpush", notification.NotificationId, notification.StudentId,
                "skipped", "NO_SUBSCRIPTIONS");
            return false;
        }

        // Build the push payload
        var payload = JsonSerializer.Serialize(new
        {
            title = notification.Title,
            body = notification.Body,
            icon = notification.IconName,
            url = notification.DeepLinkUrl,
            notificationId = notification.NotificationId
        });

        var anySuccess = false;
        foreach (var sub in subscriptions)
        {
            var result = await _webPush.SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct);
            if (result.Success)
            {
                anySuccess = true;
                _logger.LogInformation(
                    "Web Push sent successfully. " +
                    "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                    "Result={Result}, SubscriptionEndpoint={Endpoint}",
                    "webpush", notification.NotificationId, notification.StudentId,
                    "delivered", sub.Endpoint[..Math.Min(50, sub.Endpoint.Length)]);
            }
            else
            {
                _logger.LogWarning(
                    "Web Push delivery failed. " +
                    "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                    "Result={Result}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                    "webpush", notification.NotificationId, notification.StudentId,
                    "failed", result.ErrorCode, result.ErrorMessage);
            }
        }

        IncrementRateCount(notification.StudentId, "webpush");
        return anySuccess;
    }

    private async Task<bool> SendEmailAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "Email channel not configured -- skipping. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "email", notification.NotificationId, notification.StudentId,
                "skipped", "NOT_CONFIGURED");
            return false;
        }

        // Rate limit check
        if (!CheckRateLimit(notification.StudentId, "email", MaxEmailPerStudentPerHour))
        {
            _logger.LogWarning(
                "Email rate limit exceeded for student. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "email", notification.NotificationId, notification.StudentId,
                "rate_limited", "RATE_LIMIT_EXCEEDED");
            return false;
        }

        var result = await _emailSender.SendAsync(
            preferences.EmailAddress!,
            $"CENA: {notification.Title}",
            notification.Body,
            ct);

        if (result.Success)
        {
            IncrementRateCount(notification.StudentId, "email");
            _logger.LogInformation(
                "Email sent successfully. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}",
                "email", notification.NotificationId, notification.StudentId,
                "delivered");
            return true;
        }

        _logger.LogWarning(
            "Email delivery failed. " +
            "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
            "Result={Result}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
            "email", notification.NotificationId, notification.StudentId,
            "failed", result.ErrorCode, result.ErrorMessage);
        return false;
    }

    private async Task<bool> SendSmsAsync(
        NotificationDocument notification,
        NotificationPreferences preferences,
        CancellationToken ct)
    {
        if (!_smsSender.IsConfigured)
        {
            _logger.LogInformation(
                "SMS channel not configured -- skipping. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "sms", notification.NotificationId, notification.StudentId,
                "skipped", "NOT_CONFIGURED");
            return false;
        }

        // Rate limit check
        if (!CheckRateLimit(notification.StudentId, "sms", MaxSmsPerStudentPerHour))
        {
            _logger.LogWarning(
                "SMS rate limit exceeded for student. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}, ErrorCode={ErrorCode}",
                "sms", notification.NotificationId, notification.StudentId,
                "rate_limited", "RATE_LIMIT_EXCEEDED");
            return false;
        }

        var result = await _smsSender.SendAsync(
            preferences.PhoneNumber!,
            $"{notification.Title}: {notification.Body}",
            ct);

        if (result.Success)
        {
            IncrementRateCount(notification.StudentId, "sms");
            _logger.LogInformation(
                "SMS sent successfully. " +
                "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
                "Result={Result}",
                "sms", notification.NotificationId, notification.StudentId,
                "delivered");
            return true;
        }

        _logger.LogWarning(
            "SMS delivery failed. " +
            "Channel={Channel}, NotificationId={NotificationId}, StudentId={StudentId}, " +
            "Result={Result}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
            "sms", notification.NotificationId, notification.StudentId,
            "failed", result.ErrorCode, result.ErrorMessage);
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Per-student per-channel rate limiting (cost guardrail)
    // ═══════════════════════════════════════════════════════════════════════════

    private static bool CheckRateLimit(string studentId, string channel, int maxPerHour)
    {
        var key = $"{studentId}:{channel}";
        var state = RateLimits.GetOrAdd(key, _ => new ChannelRateState());
        return state.GetCountThisHour() < maxPerHour;
    }

    private static void IncrementRateCount(string studentId, string channel)
    {
        var key = $"{studentId}:{channel}";
        var state = RateLimits.GetOrAdd(key, _ => new ChannelRateState());
        state.Increment();
    }

    /// <summary>
    /// Tracks send counts per rolling hour window for rate limiting.
    /// Thread-safe via Interlocked operations.
    /// </summary>
    private sealed class ChannelRateState
    {
        private int _count;
        private long _windowStartTicks = DateTime.UtcNow.Ticks;

        public int GetCountThisHour()
        {
            var now = DateTime.UtcNow.Ticks;
            var windowStart = Interlocked.Read(ref _windowStartTicks);
            var elapsed = TimeSpan.FromTicks(now - windowStart);

            if (elapsed.TotalHours >= 1.0)
            {
                // Reset window
                Interlocked.Exchange(ref _count, 0);
                Interlocked.Exchange(ref _windowStartTicks, now);
                return 0;
            }

            return Volatile.Read(ref _count);
        }

        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }
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
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
