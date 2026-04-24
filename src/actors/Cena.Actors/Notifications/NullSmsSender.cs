// =============================================================================
// Cena Platform — Null SMS Sender (PRR-428)
//
// Graceful-disabled ISmsSender used when Notifications:Sms:Backend is
// null / unknown / missing. Returns a structured NOT_CONFIGURED result so
// callers observe the gap via /health without the process crashing.
//
// Mirrors the NullErrorAggregator / NullWhatsAppSender pattern.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications;

public sealed class NullSmsSender : ISmsSender
{
    private readonly ILogger<NullSmsSender> _logger;

    public NullSmsSender(ILogger<NullSmsSender> logger)
    {
        _logger = logger;
    }

    public bool IsConfigured => false;

    public Task<SmsSendResult> SendAsync(
        string phoneNumber,
        string messageBody,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[PRR-428] NullSmsSender swallowed sms to={Phone} bodyLen={Len}",
            phoneNumber, messageBody?.Length ?? 0);
        return Task.FromResult(new SmsSendResult(
            Success: false,
            ErrorCode: "NOT_CONFIGURED",
            ErrorMessage: "Notifications:Sms:Backend is null — no SMS sent"));
    }
}
