// =============================================================================
// Cena Platform — Web Push Client Interface (FIND-arch-018)
// Abstraction over Web Push protocol for testability
// =============================================================================

namespace Cena.Actors.Notifications;

/// <summary>
/// Result of a Web Push send attempt.
/// </summary>
public record WebPushSendResult(bool Success, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// Abstraction over the Web Push protocol (VAPID-based).
/// Implementations wrap the actual HTTP call to the push service endpoint.
/// </summary>
public interface IWebPushClient
{
    /// <summary>
    /// Whether VAPID keys are configured and the client is operational.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Send a push notification to a specific subscription endpoint.
    /// </summary>
    Task<WebPushSendResult> SendAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payload,
        CancellationToken ct = default);
}
