// =============================================================================
// Cena Platform — Security Notifier (RDY-045, RDY-036 §9)
//
// Fire-and-forget notification sink for high-impact security events (CAS
// overrides, auth bypass, admin role changes). Intentionally minimal
// contract: one method, one payload, no retries beyond a single HTTP call.
//
// Two implementations:
//   - SlackWebhookSecurityNotifier: posts to the webhook at
//     CENA_SECURITY_SLACK_WEBHOOK. Missing env → NullSecurityNotifier is
//     registered instead so dev / CI is not blocked.
//   - NullSecurityNotifier: logs-only. Used when no webhook is configured.
//
// The notifier MUST NOT throw into the caller — a webhook failure logs a
// warning and returns. Audit durability is the responsibility of the SIEM
// log emitted by the endpoint, not this transport.
// =============================================================================

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Services;

public sealed record SecurityNotification(
    string Title,
    string Summary,
    IReadOnlyDictionary<string, string> Fields,
    SecuritySeverity Severity = SecuritySeverity.High);

public enum SecuritySeverity { Info, Warning, High, Critical }

public interface ISecurityNotifier
{
    /// <summary>
    /// Best-effort notification. Never throws. Callers should call it
    /// fire-and-forget (or await without expecting it to block the
    /// primary operation's success).
    /// </summary>
    Task NotifyAsync(SecurityNotification notification, CancellationToken ct = default);
}

/// <summary>
/// RDY-045: Slack webhook notifier. The webhook URL is sourced from
/// <c>CENA_SECURITY_SLACK_WEBHOOK</c>. When the URL is absent, the DI
/// registration should fall back to <see cref="NullSecurityNotifier"/>.
/// </summary>
public sealed class SlackWebhookSecurityNotifier : ISecurityNotifier
{
    public const string WebhookEnvVar = "CENA_SECURITY_SLACK_WEBHOOK";

    private readonly HttpClient _http;
    private readonly string _webhookUrl;
    private readonly ILogger<SlackWebhookSecurityNotifier> _logger;

    public SlackWebhookSecurityNotifier(
        HttpClient http,
        string webhookUrl,
        ILogger<SlackWebhookSecurityNotifier> logger)
    {
        _http = http;
        _webhookUrl = webhookUrl;
        _logger = logger;
    }

    public async Task NotifyAsync(SecurityNotification n, CancellationToken ct = default)
    {
        try
        {
            var fields = n.Fields.Select(kv => new
            {
                title = kv.Key,
                value = kv.Value,
                @short = kv.Value.Length < 40
            }).ToArray();

            var payload = new
            {
                text = $"*[{n.Severity}] {n.Title}*",
                attachments = new[]
                {
                    new
                    {
                        color = n.Severity switch
                        {
                            SecuritySeverity.Critical => "danger",
                            SecuritySeverity.High => "warning",
                            _ => "good"
                        },
                        title = n.Title,
                        text = n.Summary,
                        fields,
                        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }
                }
            };

            using var resp = await _http.PostAsJsonAsync(_webhookUrl, payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[SECURITY_NOTIFIER_FAILED] status={Status} title={Title}",
                    (int)resp.StatusCode, n.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[SECURITY_NOTIFIER_EX] title={Title} — notification dropped (fire-and-forget)", n.Title);
        }
    }
}

/// <summary>
/// RDY-045: Null notifier — structured-log only, used when no webhook URL
/// is configured. The SIEM log is the durable audit record; this keeps
/// behaviour sensible in dev / CI.
/// </summary>
public sealed class NullSecurityNotifier : ISecurityNotifier
{
    private readonly ILogger<NullSecurityNotifier> _logger;
    public NullSecurityNotifier(ILogger<NullSecurityNotifier> logger) => _logger = logger;

    public Task NotifyAsync(SecurityNotification n, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[SECURITY_NOTIFIER_NOOP] severity={Severity} title={Title} summary={Summary}",
            n.Severity, n.Title, n.Summary);
        return Task.CompletedTask;
    }
}
