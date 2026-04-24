// =============================================================================
// Cena Platform — Twilio WhatsApp Sender (RDY-069 Phase 1B)
//
// Concrete IWhatsAppSender backed by Twilio's WhatsApp messaging
// gateway. Reads `Twilio:AccountSid` + `Twilio:AuthToken` +
// `Twilio:WhatsAppFromNumber` from configuration; falls through to
// graceful-disabled (VendorError) when any of them is missing so a
// dev / staging environment without Twilio credentials doesn't break
// the digest pipeline.
//
// The adapter MUST translate Twilio-specific response codes into our
// vendor-neutral WhatsAppDeliveryOutcome enum so the dispatcher's
// circuit-breaker logic stays portable across vendor swaps.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Options bound from <c>Twilio</c> configuration section. Missing or
/// blank AccountSid / AuthToken / WhatsAppFromNumber → sender is
/// NotConfigured and all sends return VendorError (graceful-disabled).
/// </summary>
public sealed class TwilioWhatsAppOptions
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? WhatsAppFromNumber { get; set; }

    /// <summary>
    /// Override the Twilio API base URL (default
    /// <c>https://api.twilio.com</c>). Tests point this at a mock
    /// server; production leaves it null.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(WhatsAppFromNumber);
}

public sealed class TwilioWhatsAppSender : IWhatsAppSender
{
    private readonly TwilioWhatsAppOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<TwilioWhatsAppSender> _logger;
    private readonly IWhatsAppRecipientLookup _recipientLookup;

    public TwilioWhatsAppSender(
        IOptions<TwilioWhatsAppOptions> options,
        HttpClient http,
        IWhatsAppRecipientLookup recipientLookup,
        ILogger<TwilioWhatsAppSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(recipientLookup);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _http = http;
        _recipientLookup = recipientLookup;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
        else if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://api.twilio.com");
    }

    public string VendorId => "twilio";
    public bool IsConfigured => _options.IsComplete;

    public async Task<WhatsAppDeliveryOutcome> SendAsync(
        WhatsAppDeliveryAttempt attempt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (!IsConfigured)
        {
            _logger.LogWarning(
                "[RDY-069] TwilioWhatsAppSender not configured; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }

        string? phoneNumber;
        try
        {
            phoneNumber = await _recipientLookup.ResolveAsync(
                attempt.ParentAnonId, attempt.MinorAnonId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[RDY-069] Recipient lookup failed; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.InvalidRecipient;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            _logger.LogWarning(
                "[RDY-069] No phone on file; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.InvalidRecipient;
        }

        // Twilio messaging path (simplified — a production adapter
        // uses the Twilio SDK or signs Basic auth explicitly). Phase
        // 1B ships the minimal POST; Phase 1C adds retries + exponential
        // backoff + idempotency-key header.
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid!)}/Messages.json";
        var body = new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{_options.WhatsAppFromNumber}",
            ["To"] = $"whatsapp:{phoneNumber}",
            ["Body"] = $"(template={attempt.TemplateId}; locale={attempt.Locale})",
            ["StatusCallback"] = string.Empty, // Phase 1C wires webhook URL
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(body),
        };
        var basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
        request.Headers.Add("I-Correlation-Id", attempt.CorrelationId);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            return MapTwilioStatus(response.StatusCode, attempt.CorrelationId);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "[RDY-069] Twilio request cancelled; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "[RDY-069] Twilio request failed; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }
    }

    internal static WhatsAppDeliveryOutcome MapTwilioStatus(
        HttpStatusCode status, string correlationId)
    {
        return status switch
        {
            HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Accepted
                => WhatsAppDeliveryOutcome.Accepted,
            HttpStatusCode.TooManyRequests
                => WhatsAppDeliveryOutcome.RateLimited,
            HttpStatusCode.NotFound
                => WhatsAppDeliveryOutcome.InvalidRecipient,
            HttpStatusCode.BadRequest
                => WhatsAppDeliveryOutcome.InvalidRecipient,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => WhatsAppDeliveryOutcome.VendorError,
            _ => WhatsAppDeliveryOutcome.VendorError,
        };
    }
}

/// <summary>
/// Resolves parent/minor anon ids to the parent's live phone number.
/// Concrete implementation reads the parent identity store + enforces
/// the consent gate separately from the channel opt-in gate.
/// </summary>
public interface IWhatsAppRecipientLookup
{
    Task<string?> ResolveAsync(
        string parentAnonId, string minorAnonId, CancellationToken ct);
}

/// <summary>Graceful-disabled lookup — always returns null.</summary>
public sealed class NullWhatsAppRecipientLookup : IWhatsAppRecipientLookup
{
    public Task<string?> ResolveAsync(
        string parentAnonId, string minorAnonId, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
