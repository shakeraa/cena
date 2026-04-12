// =============================================================================
// Cena Platform — Web Push Client (FIND-arch-018)
// VAPID-based Web Push using Lib.Net.Http.WebPush (RFC 8291 / RFC 8030).
// =============================================================================

using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications;

/// <summary>
/// Production Web Push client that uses VAPID keys to sign push requests.
/// Wraps Lib.Net.Http.WebPush for payload encryption and delivery.
/// </summary>
public sealed class WebPushClient : IWebPushClient, IDisposable
{
    private readonly PushServiceClient _client;
    private readonly ILogger<WebPushClient> _logger;
    private readonly bool _isConfigured;

    public WebPushClient(IConfiguration configuration, ILogger<WebPushClient> logger)
    {
        _logger = logger;

        var section = configuration.GetSection("WebPush");
        var publicKey = section["VapidPublicKey"];
        var privateKey = section["VapidPrivateKey"];
        var subject = section["VapidSubject"];

        if (!string.IsNullOrEmpty(publicKey) &&
            !string.IsNullOrEmpty(privateKey) &&
            !string.IsNullOrEmpty(subject))
        {
            var authentication = new VapidAuthentication(publicKey, privateKey)
            {
                Subject = subject
            };
            _client = new PushServiceClient { DefaultAuthentication = authentication };
            _isConfigured = true;
            _logger.LogInformation("WebPush client initialized with VAPID subject {Subject}", subject);
        }
        else
        {
            _client = new PushServiceClient();
            _isConfigured = false;
            _logger.LogWarning(
                "WebPush VAPID keys not configured (WebPush:VapidPublicKey, WebPush:VapidPrivateKey, WebPush:VapidSubject) " +
                "-- web push notifications are disabled");
        }
    }

    public bool IsConfigured => _isConfigured;

    public async Task<WebPushSendResult> SendAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payload,
        CancellationToken ct = default)
    {
        if (!_isConfigured)
        {
            return new WebPushSendResult(false, "NOT_CONFIGURED", "VAPID keys not configured");
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            return new WebPushSendResult(false, "INVALID_ENDPOINT", "Push subscription endpoint is empty");
        }

        var subscription = new PushSubscription
        {
            Endpoint = endpoint,
            Keys = new Dictionary<string, string>
            {
                ["p256dh"] = p256dh,
                ["auth"] = auth
            }
        };

        var message = new PushMessage(payload)
        {
            Urgency = PushMessageUrgency.Normal
        };

        try
        {
            await _client.RequestPushMessageDeliveryAsync(subscription, message, ct);
            return new WebPushSendResult(true);
        }
        catch (PushServiceClientException ex) when (
            ex.Message.Contains("410") || ex.Message.Contains("Gone"))
        {
            return new WebPushSendResult(false, "SUBSCRIPTION_GONE",
                $"Push subscription expired (410 Gone): {endpoint}");
        }
        catch (PushServiceClientException ex) when (
            ex.Message.Contains("429") || ex.Message.Contains("Too Many"))
        {
            return new WebPushSendResult(false, "RATE_LIMITED",
                $"Push service rate limited (429): {endpoint}");
        }
        catch (PushServiceClientException ex)
        {
            return new WebPushSendResult(false, "PUSH_FAILED",
                $"Web Push failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WebPushSendResult(false, "SEND_ERROR",
                $"Unexpected error sending web push: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
