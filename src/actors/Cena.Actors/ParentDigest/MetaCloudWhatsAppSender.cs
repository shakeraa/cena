// =============================================================================
// Cena Platform — Meta Cloud WhatsApp Sender (PRR-429)
//
// Direct adapter for Meta's WhatsApp Cloud API (Graph v21.0+). Drops in
// behind the existing IWhatsAppSender interface so operators swap from
// Twilio by flipping Notifications:WhatsApp:Backend = "meta".
//
// Cost motivation: at 1k-parent scale (~4k utility convos/mo, 1k free tier)
// this path is ~$42/mo vs ~$57/mo via Twilio — see
// docs/ops/peripheral-costs.md §3.
//
// Endpoint: POST {BaseUrl}/{GraphApiVersion}/{PhoneNumberId}/messages
// Auth:     Authorization: Bearer {AccessToken}
// Dedup:    Idempotency-Key: {CorrelationId}   (Meta v21+ honours this)
// Body:
//   { "messaging_product": "whatsapp",
//     "to": "{e164_no_plus}",
//     "type": "template",
//     "template": { "name": "{TemplateId}", "language": { "code": "{locale}" } } }
//
// Error-code mapping (from Meta's {"error":{"code":N,...}} envelope):
//   200                                  -> Accepted
//   400 + 131047                         -> InvalidRecipient  (re-engagement window)
//   400 + 132000..132999                 -> TemplateNotApproved
//   429                                  -> RateLimited
//   401 / 403                            -> VendorError
//   anything else (incl. parse failure)  -> VendorError
//
// Out of scope: inbound delivery-status webhooks. A separate task wires
// that once Meta callback signing + dead-letter plumbing are ready.
// =============================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Options bound from the <c>MetaCloud</c> configuration section. Any
/// blank credential field leaves the sender <see cref="MetaCloudWhatsAppSender.IsConfigured"/>
/// false; all sends return <see cref="WhatsAppDeliveryOutcome.VendorError"/>
/// (graceful-disabled, same posture as <see cref="TwilioWhatsAppOptions"/>).
/// </summary>
public sealed class MetaCloudWhatsAppOptions
{
    public const string SectionName = "MetaCloud";

    /// <summary>
    /// Business phone-number identifier from WhatsApp Business Manager —
    /// NOT the phone number itself. Path-parameter on the messages API.
    /// </summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Long-lived system-user access token. Bearer-auth'd.</summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// WhatsApp Business Account identifier — not used on the hot path
    /// (message send) but surfaced to ops for health-check wiring and to
    /// prevent accidental deploys with only half the creds in place.
    /// </summary>
    public string? BusinessAccountId { get; set; }

    /// <summary>Graph API version (default <c>v21.0</c>).</summary>
    public string GraphApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Override the Meta Graph base URL (default
    /// <c>https://graph.facebook.com</c>). Tests point this at a mock
    /// server; production leaves the default.
    /// </summary>
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>
    /// Meta app secret — used to verify the HMAC-SHA256 signature on every
    /// inbound webhook (PRR-437). Required when the webhook is wired; the
    /// endpoint refuses traffic if this is blank (fail-loud — the right
    /// posture when the signing secret is missing is "reject everything",
    /// not "accept unsigned traffic").
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// Verify-token string Meta presents during the GET handshake at
    /// subscription time (PRR-437). Must match a shared secret configured
    /// in WhatsApp Business Manager when setting up the webhook; otherwise
    /// the handshake returns 403 and Meta considers the webhook invalid.
    /// </summary>
    public string? WebhookVerifyToken { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(PhoneNumberId)
        && !string.IsNullOrWhiteSpace(AccessToken)
        && !string.IsNullOrWhiteSpace(BusinessAccountId);

    /// <summary>
    /// True when BOTH AppSecret + WebhookVerifyToken are populated. The
    /// webhook endpoint refuses inbound traffic when this is false so a
    /// deploy-without-webhook-config doesn't silently accept unverified
    /// POSTs.
    /// </summary>
    public bool IsWebhookReady =>
        !string.IsNullOrWhiteSpace(AppSecret)
        && !string.IsNullOrWhiteSpace(WebhookVerifyToken);
}

public sealed class MetaCloudWhatsAppSender : IWhatsAppSender
{
    private readonly MetaCloudWhatsAppOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<MetaCloudWhatsAppSender> _logger;
    private readonly IWhatsAppRecipientLookup _recipientLookup;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MetaCloudWhatsAppSender(
        IOptions<MetaCloudWhatsAppOptions> options,
        HttpClient http,
        IWhatsAppRecipientLookup recipientLookup,
        ILogger<MetaCloudWhatsAppSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(recipientLookup);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _http = http;
        _recipientLookup = recipientLookup;
        _logger = logger;

        // Honour explicit BaseUrl override; otherwise use the Graph default
        // when the caller-supplied client doesn't already have one set.
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
        else if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://graph.facebook.com");
    }

    public string VendorId => "meta";
    public bool IsConfigured => _options.IsComplete;

    public async Task<WhatsAppDeliveryOutcome> SendAsync(
        WhatsAppDeliveryAttempt attempt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (!IsConfigured)
        {
            _logger.LogWarning(
                "[PRR-429] MetaCloudWhatsAppSender not configured; correlation={Corr}",
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
                "[PRR-429] Recipient lookup failed; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.InvalidRecipient;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            _logger.LogWarning(
                "[PRR-429] No phone on file; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.InvalidRecipient;
        }

        // Meta's "to" field is the E.164 number without the leading '+'.
        var metaTo = phoneNumber.TrimStart('+');

        var path = $"/{_options.GraphApiVersion}/{Uri.EscapeDataString(_options.PhoneNumberId!)}/messages";
        var payload = new MetaMessageRequest
        {
            MessagingProduct = "whatsapp",
            To = metaTo,
            Type = "template",
            Template = new MetaTemplate
            {
                Name = attempt.TemplateId,
                Language = new MetaTemplateLanguage { Code = attempt.Locale },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        // Meta honours Idempotency-Key for retry-safe message creation on v21+.
        request.Headers.TryAddWithoutValidation("Idempotency-Key", attempt.CorrelationId);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var bodyText = string.Empty;
            try
            {
                bodyText = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                // Body read is best-effort; status code alone is enough
                // to make a mapping decision for every non-body path.
                bodyText = string.Empty;
            }
            return MapMetaResponse(response.StatusCode, bodyText, attempt.CorrelationId, _logger);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "[PRR-429] Meta request cancelled/timeout; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "[PRR-429] Meta request failed; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }
        catch (Exception ex)
        {
            // Defensive catch-all: SendAsync is contractually no-throw for
            // the outer caller (dispatch pipeline treats exceptions as
            // crashing bugs, not delivery outcomes).
            _logger.LogWarning(
                ex,
                "[PRR-429] Meta request unexpected failure; correlation={Corr}",
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }
    }

    /// <summary>
    /// Map a Meta Cloud API response (status + body) to a vendor-neutral
    /// <see cref="WhatsAppDeliveryOutcome"/>. Exposed internal so the
    /// mapping tests can exercise the table directly without a real HTTP
    /// round-trip.
    /// </summary>
    internal static WhatsAppDeliveryOutcome MapMetaResponse(
        HttpStatusCode status,
        string? body,
        string correlationId,
        ILogger? logger = null)
    {
        // Fast-path: 2xx is always Accepted for template sends. Meta does
        // not return 201/202 here — it's 200 with the message id in the
        // body — but accept the family defensively.
        if ((int)status is >= 200 and < 300)
            return WhatsAppDeliveryOutcome.Accepted;

        if (status == HttpStatusCode.TooManyRequests)
            return WhatsAppDeliveryOutcome.RateLimited;

        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
            return WhatsAppDeliveryOutcome.VendorError;

        // 400 branches off Meta's error code (131047 / 132xxx). Any
        // other 4xx or 5xx → VendorError.
        if (status == HttpStatusCode.BadRequest)
        {
            var code = TryParseMetaErrorCode(body);
            if (code == 131047)
                return WhatsAppDeliveryOutcome.InvalidRecipient;
            if (code is >= 132000 and <= 132999)
                return WhatsAppDeliveryOutcome.TemplateNotApproved;

            logger?.LogWarning(
                "[PRR-429] Meta 400 with unmapped error code {Code}; correlation={Corr}",
                code, correlationId);
            return WhatsAppDeliveryOutcome.VendorError;
        }

        return WhatsAppDeliveryOutcome.VendorError;
    }

    internal static int? TryParseMetaErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("error", out var err)) return null;
            if (err.ValueKind != JsonValueKind.Object) return null;
            if (!err.TryGetProperty("code", out var codeEl)) return null;

            return codeEl.ValueKind switch
            {
                JsonValueKind.Number when codeEl.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(codeEl.GetString(), out var n) => n,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- JSON DTOs --------------------------------------------------------

    private sealed class MetaMessageRequest
    {
        [JsonPropertyName("messaging_product")]
        public string MessagingProduct { get; set; } = "whatsapp";

        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "template";

        [JsonPropertyName("template")]
        public MetaTemplate Template { get; set; } = new();
    }

    private sealed class MetaTemplate
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public MetaTemplateLanguage Language { get; set; } = new();
    }

    private sealed class MetaTemplateLanguage
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "en";
    }
}
