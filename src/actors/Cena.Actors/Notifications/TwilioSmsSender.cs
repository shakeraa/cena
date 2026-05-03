// =============================================================================
// Cena Platform — SMS Sender (FIND-arch-018)
// SMS channel is explicitly disabled until a provider (Twilio, etc.) is
// configured. Returns false with a structured reason -- never pretends to send.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications;

/// <summary>
/// Result of an SMS send attempt.
/// </summary>
public record SmsSendResult(bool Success, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// Abstraction over SMS sending for testability.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Whether an SMS provider is configured and operational.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Send an SMS notification.
    /// </summary>
    Task<SmsSendResult> SendAsync(
        string phoneNumber,
        string messageBody,
        CancellationToken ct = default);
}

/// <summary>
/// SMS sender that is explicitly disabled until a provider is configured.
/// SMS is not a launch requirement for CENA. When Twilio (or another provider)
/// credentials are added to the Sms configuration section, this class should be
/// replaced with a real Twilio implementation.
/// </summary>
public sealed class TwilioSmsSender : ISmsSender
{
    private readonly bool _isConfigured;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(IConfiguration configuration, ILogger<TwilioSmsSender> logger)
    {
        _logger = logger;

        var section = configuration.GetSection("Sms");
        var accountSid = section["AccountSid"];
        var authToken = section["AuthToken"];
        var fromNumber = section["FromNumber"];

        // SMS is only enabled if ALL three Twilio values are present
        _isConfigured = !string.IsNullOrEmpty(accountSid) &&
                        !string.IsNullOrEmpty(authToken) &&
                        !string.IsNullOrEmpty(fromNumber);

        if (!_isConfigured)
        {
            _logger.LogWarning(
                "SMS provider not configured (Sms:AccountSid / Sms:AuthToken / Sms:FromNumber are empty) " +
                "-- SMS notifications are disabled. This is expected for non-production environments");
        }
        else
        {
            _logger.LogInformation("SMS sender initialized with from number {FromNumber}", fromNumber);
        }
    }

    public bool IsConfigured => _isConfigured;

    public Task<SmsSendResult> SendAsync(
        string phoneNumber,
        string messageBody,
        CancellationToken ct = default)
    {
        if (!_isConfigured)
        {
            _logger.LogInformation(
                "SMS channel not configured -- skipping send to {Phone}. " +
                "To enable, set Sms:AccountSid, Sms:AuthToken, and Sms:FromNumber in appsettings",
                MaskPhoneNumber(phoneNumber));

            return Task.FromResult(
                new SmsSendResult(false, "NOT_CONFIGURED", "SMS provider not configured"));
        }

        // When _isConfigured is true, this is where Twilio API calls would go.
        // For now, this path is unreachable because dev/test config never has
        // real Twilio credentials. When we add them, implement:
        //
        // var client = new TwilioRestClient(accountSid, authToken);
        // var message = await MessageResource.CreateAsync(
        //     to: new PhoneNumber(phoneNumber),
        //     from: new PhoneNumber(fromNumber),
        //     body: messageBody);
        //
        // For now, fail explicitly so we never silently claim success.
        _logger.LogError(
            "SMS provider is configured but Twilio SDK integration is not yet implemented. " +
            "Channel={Channel}, Phone={Phone}, ErrorCode={ErrorCode}",
            "sms", MaskPhoneNumber(phoneNumber), "NOT_IMPLEMENTED");

        return Task.FromResult(
            new SmsSendResult(false, "NOT_IMPLEMENTED",
                "SMS provider configured but Twilio SDK integration pending"));
    }

    private static string MaskPhoneNumber(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4)
            return "***";
        return $"***{phone[^4..]}";
    }
}
