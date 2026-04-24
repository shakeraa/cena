// =============================================================================
// Cena Platform — Null Email Sender (PRR-428)
//
// Graceful-disabled IEmailSender used when Notifications:Email:Backend is
// null / unknown / missing. Returns a structured NOT_CONFIGURED result on
// every send so callers can observe the absence (and surface it via
// /health) without crashing.
//
// Mirrors the NullErrorAggregator / NullWhatsAppSender pattern.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Notifications;

public sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger)
    {
        _logger = logger;
    }

    public bool IsConfigured => false;

    public Task<EmailSendResult> SendAsync(
        string toAddress,
        string subject,
        string bodyText,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[PRR-428] NullEmailSender swallowed email to={To} subject={Subject}",
            toAddress, subject);
        return Task.FromResult(new EmailSendResult(
            Success: false,
            ErrorCode: "NOT_CONFIGURED",
            ErrorMessage: "Notifications:Email:Backend is null — no email sent"));
    }
}
