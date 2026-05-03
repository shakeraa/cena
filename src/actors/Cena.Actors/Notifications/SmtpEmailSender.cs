// =============================================================================
// Cena Platform — SMTP Email Sender (FIND-arch-018)
// Real email delivery via MailKit/SMTP. Falls back gracefully when
// SMTP is not configured (dev/test environments).
// =============================================================================

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Cena.Actors.Notifications;

/// <summary>
/// Result of an email send attempt.
/// </summary>
public record EmailSendResult(bool Success, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// Abstraction over email sending for testability.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Whether SMTP is configured and the sender is operational.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Send an email notification.
    /// </summary>
    Task<EmailSendResult> SendAsync(
        string toAddress,
        string subject,
        string bodyText,
        CancellationToken ct = default);
}

/// <summary>
/// Production SMTP email sender using MailKit.
/// Reads SMTP configuration from appsettings (Smtp section).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly string? _host;
    private readonly int _port;
    private readonly string? _username;
    private readonly string? _password;
    private readonly string _fromAddress;
    private readonly string _fromName;
    private readonly bool _useSsl;
    private readonly bool _isConfigured;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _logger = logger;

        var section = configuration.GetSection("Smtp");
        _host = section["Host"];
        _port = section.GetValue("Port", 587);
        _username = section["Username"];
        _password = section["Password"];
        _fromAddress = section["FromAddress"] ?? "noreply@cena.edu";
        _fromName = section["FromName"] ?? "CENA Platform";
        _useSsl = section.GetValue("UseSsl", true);

        if (!string.IsNullOrEmpty(_host))
        {
            _isConfigured = true;
            _logger.LogInformation(
                "SMTP email sender initialized: {Host}:{Port}, from={From}",
                _host, _port, _fromAddress);
        }
        else
        {
            _isConfigured = false;
            _logger.LogWarning(
                "SMTP not configured (Smtp:Host is empty) -- email notifications are disabled");
        }
    }

    public bool IsConfigured => _isConfigured;

    public async Task<EmailSendResult> SendAsync(
        string toAddress,
        string subject,
        string bodyText,
        CancellationToken ct = default)
    {
        if (!_isConfigured)
        {
            return new EmailSendResult(false, "NOT_CONFIGURED", "SMTP host not configured");
        }

        if (string.IsNullOrEmpty(toAddress))
        {
            return new EmailSendResult(false, "INVALID_ADDRESS", "Recipient email address is empty");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = bodyText };

        try
        {
            using var client = new SmtpClient();

            var secureSocketOption = _useSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_host, _port, secureSocketOption, ct);

            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
            {
                await client.AuthenticateAsync(_username, _password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            return new EmailSendResult(true);
        }
        catch (SmtpCommandException ex)
        {
            return new EmailSendResult(false, $"SMTP_{(int)ex.StatusCode}",
                $"SMTP command failed ({ex.StatusCode}): {ex.Message}");
        }
        catch (SmtpProtocolException ex)
        {
            return new EmailSendResult(false, "SMTP_PROTOCOL",
                $"SMTP protocol error: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new EmailSendResult(false, "EMAIL_ERROR",
                $"Unexpected error sending email: {ex.Message}");
        }
    }
}
