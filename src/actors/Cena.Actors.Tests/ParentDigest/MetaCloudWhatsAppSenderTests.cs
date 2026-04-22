// =============================================================================
// PRR-429 — MetaCloudWhatsAppSender tests.
//
// Mirrors TwilioWhatsAppSenderTests structure:
//   - Options gating
//   - Status/body → WhatsAppDeliveryOutcome mapping
//   - Unconfigured / invalid-recipient guards
//   - Happy-path request shape (URL, body, headers incl. Idempotency-Key)
// =============================================================================

using System.Net;
using System.Net.Http;
using System.Text.Json;
using Cena.Actors.ParentDigest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.ParentDigest;

public class MetaCloudWhatsAppOptionsTests
{
    [Fact]
    public void IsComplete_requires_all_three_credentials()
    {
        var full = new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "1234567890",
            AccessToken = "dummy-token",
            BusinessAccountId = "987654321",
        };
        Assert.True(full.IsComplete);
    }

    [Theory]
    [InlineData(null, "tok", "waba")]
    [InlineData("pid", null, "waba")]
    [InlineData("pid", "tok", null)]
    [InlineData("", "tok", "waba")]
    [InlineData("pid", "  ", "waba")]
    [InlineData("pid", "tok", "")]
    public void IsComplete_false_when_any_credential_blank(
        string? phoneNumberId, string? accessToken, string? businessAccountId)
    {
        var opts = new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = phoneNumberId,
            AccessToken = accessToken,
            BusinessAccountId = businessAccountId,
        };
        Assert.False(opts.IsComplete);
    }

    [Fact]
    public void Default_graph_api_version_is_v21()
    {
        var opts = new MetaCloudWhatsAppOptions();
        Assert.Equal("v21.0", opts.GraphApiVersion);
        Assert.Equal("https://graph.facebook.com", opts.BaseUrl);
    }
}

public class MetaCloudWhatsAppSender_MappingTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, null, WhatsAppDeliveryOutcome.Accepted)]
    [InlineData(HttpStatusCode.Created, null, WhatsAppDeliveryOutcome.Accepted)]
    [InlineData(HttpStatusCode.Accepted, null, WhatsAppDeliveryOutcome.Accepted)]
    [InlineData(HttpStatusCode.TooManyRequests, null, WhatsAppDeliveryOutcome.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, null, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.Forbidden, null, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.InternalServerError, null, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.BadGateway, null, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.NotFound, null, WhatsAppDeliveryOutcome.VendorError)]
    public void Maps_status_without_error_body_to_expected_outcome(
        HttpStatusCode status, string? body, WhatsAppDeliveryOutcome expected)
    {
        Assert.Equal(
            expected,
            MetaCloudWhatsAppSender.MapMetaResponse(status, body, "corr-1"));
    }

    [Fact]
    public void Maps_400_with_131047_to_InvalidRecipient()
    {
        var body = """{"error":{"code":131047,"message":"re-engagement window closed"}}""";
        Assert.Equal(
            WhatsAppDeliveryOutcome.InvalidRecipient,
            MetaCloudWhatsAppSender.MapMetaResponse(HttpStatusCode.BadRequest, body, "c"));
    }

    [Theory]
    [InlineData(132000)]
    [InlineData(132001)]
    [InlineData(132500)]
    [InlineData(132999)]
    public void Maps_400_with_132xxx_to_TemplateNotApproved(int code)
    {
        var body = $$"""{"error":{"code":{{code}},"message":"template mismatch"}}""";
        Assert.Equal(
            WhatsAppDeliveryOutcome.TemplateNotApproved,
            MetaCloudWhatsAppSender.MapMetaResponse(HttpStatusCode.BadRequest, body, "c"));
    }

    [Theory]
    [InlineData(131999)] // below the 132xxx band
    [InlineData(133000)] // above
    [InlineData(0)]
    public void Maps_400_with_unmapped_code_to_VendorError(int code)
    {
        var body = $$"""{"error":{"code":{{code}},"message":"other"}}""";
        Assert.Equal(
            WhatsAppDeliveryOutcome.VendorError,
            MetaCloudWhatsAppSender.MapMetaResponse(HttpStatusCode.BadRequest, body, "c"));
    }

    [Fact]
    public void Maps_400_with_unparseable_body_to_VendorError()
    {
        Assert.Equal(
            WhatsAppDeliveryOutcome.VendorError,
            MetaCloudWhatsAppSender.MapMetaResponse(HttpStatusCode.BadRequest, "not-json", "c"));
    }

    [Fact]
    public void Maps_400_with_missing_error_envelope_to_VendorError()
    {
        Assert.Equal(
            WhatsAppDeliveryOutcome.VendorError,
            MetaCloudWhatsAppSender.MapMetaResponse(HttpStatusCode.BadRequest, "{\"ok\":true}", "c"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseMetaErrorCode_returns_null_on_blank_body(string? body)
    {
        Assert.Null(MetaCloudWhatsAppSender.TryParseMetaErrorCode(body));
    }

    [Fact]
    public void TryParseMetaErrorCode_handles_string_code()
    {
        // Meta occasionally returns error.code as a quoted numeric
        // string — parser should still extract the int.
        var body = """{"error":{"code":"131047","message":"x"}}""";
        Assert.Equal(131047, MetaCloudWhatsAppSender.TryParseMetaErrorCode(body));
    }
}

public class MetaCloudWhatsAppSender_UnconfiguredTests
{
    [Fact]
    public async Task Returns_vendor_error_when_credentials_missing()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions());
        var http = new HttpClient();
        var sender = new MetaCloudWhatsAppSender(
            options, http, new NullWhatsAppRecipientLookup(), NullLogger<MetaCloudWhatsAppSender>.Instance);

        Assert.False(sender.IsConfigured);
        Assert.Equal("meta", sender.VendorId);

        var outcome = await sender.SendAsync(MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, outcome);
    }

    [Fact]
    public async Task Returns_vendor_error_when_only_phone_number_id_missing()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            AccessToken = "tok",
            BusinessAccountId = "waba",
        });
        var http = new HttpClient();
        var sender = new MetaCloudWhatsAppSender(
            options, http, new NullWhatsAppRecipientLookup(), NullLogger<MetaCloudWhatsAppSender>.Instance);

        Assert.False(sender.IsConfigured);
        var outcome = await sender.SendAsync(MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, outcome);
    }

    [Fact]
    public async Task Returns_invalid_recipient_when_lookup_returns_null()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "pid",
            AccessToken = "tok",
            BusinessAccountId = "waba",
        });
        var http = new HttpClient(new NoOpHandler());
        var sender = new MetaCloudWhatsAppSender(
            options, http, new NullWhatsAppRecipientLookup(), NullLogger<MetaCloudWhatsAppSender>.Instance);

        Assert.True(sender.IsConfigured);

        var outcome = await sender.SendAsync(MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.InvalidRecipient, outcome);
    }

    internal static WhatsAppDeliveryAttempt MakeAttempt(string correlationId = "corr-1")
        => new(
            CorrelationId: correlationId,
            ParentAnonId: "parent-1",
            MinorAnonId: "minor-1",
            TemplateId: "weekly_digest_v1",
            Locale: "en",
            AttemptNumber: 1,
            AttemptedAtUtc: DateTimeOffset.UtcNow);

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

public class MetaCloudWhatsAppSender_HappyPathTests
{
    [Fact]
    public async Task Sends_well_formed_request_and_returns_accepted()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "1234567890",
            AccessToken = "secret-token",
            BusinessAccountId = "waba-id",
            GraphApiVersion = "v21.0",
            BaseUrl = "https://graph.test.invalid",
        });

        var handler = new CapturingHandler(
            status: HttpStatusCode.OK,
            responseBody: """{"messages":[{"id":"wamid.xyz"}]}""");
        var http = new HttpClient(handler);
        var lookup = new StaticRecipientLookup("+972501234567");
        var sender = new MetaCloudWhatsAppSender(
            options, http, lookup, NullLogger<MetaCloudWhatsAppSender>.Instance);

        var attempt = MetaCloudWhatsAppSender_UnconfiguredTests.MakeAttempt("corr-42");
        var outcome = await sender.SendAsync(attempt);

        Assert.Equal(WhatsAppDeliveryOutcome.Accepted, outcome);

        // --- Request URL -----------------------------------------------
        Assert.NotNull(handler.LastRequest);
        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("graph.test.invalid", uri.Host);
        Assert.Equal("/v21.0/1234567890/messages", uri.AbsolutePath);

        // --- Auth + idempotency headers --------------------------------
        Assert.NotNull(handler.LastRequest.Headers.Authorization);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization.Parameter);

        Assert.True(handler.LastRequest.Headers.TryGetValues("Idempotency-Key", out var idemValues));
        Assert.Equal("corr-42", Assert.Single(idemValues!));

        // --- Request body shape ----------------------------------------
        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("972501234567", root.GetProperty("to").GetString()); // leading '+' stripped
        Assert.Equal("template", root.GetProperty("type").GetString());
        var template = root.GetProperty("template");
        Assert.Equal("weekly_digest_v1", template.GetProperty("name").GetString());
        Assert.Equal("en", template.GetProperty("language").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Maps_meta_400_re_engagement_error_to_invalid_recipient()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "pid",
            AccessToken = "tok",
            BusinessAccountId = "waba",
            BaseUrl = "https://graph.test.invalid",
        });

        var handler = new CapturingHandler(
            status: HttpStatusCode.BadRequest,
            responseBody: """{"error":{"code":131047,"message":"Re-engagement window closed"}}""");
        var http = new HttpClient(handler);
        var sender = new MetaCloudWhatsAppSender(
            options, http, new StaticRecipientLookup("+15551230000"),
            NullLogger<MetaCloudWhatsAppSender>.Instance);

        var outcome = await sender.SendAsync(
            MetaCloudWhatsAppSender_UnconfiguredTests.MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.InvalidRecipient, outcome);
    }

    [Fact]
    public async Task Maps_meta_400_template_error_to_template_not_approved()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "pid",
            AccessToken = "tok",
            BusinessAccountId = "waba",
            BaseUrl = "https://graph.test.invalid",
        });

        var handler = new CapturingHandler(
            status: HttpStatusCode.BadRequest,
            responseBody: """{"error":{"code":132001,"message":"Template name does not exist"}}""");
        var http = new HttpClient(handler);
        var sender = new MetaCloudWhatsAppSender(
            options, http, new StaticRecipientLookup("+15551230000"),
            NullLogger<MetaCloudWhatsAppSender>.Instance);

        var outcome = await sender.SendAsync(
            MetaCloudWhatsAppSender_UnconfiguredTests.MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.TemplateNotApproved, outcome);
    }

    [Fact]
    public async Task Returns_vendor_error_on_http_request_exception()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "pid",
            AccessToken = "tok",
            BusinessAccountId = "waba",
            BaseUrl = "https://graph.test.invalid",
        });

        var handler = new ThrowingHandler(new HttpRequestException("network down"));
        var http = new HttpClient(handler);
        var sender = new MetaCloudWhatsAppSender(
            options, http, new StaticRecipientLookup("+15551230000"),
            NullLogger<MetaCloudWhatsAppSender>.Instance);

        var outcome = await sender.SendAsync(
            MetaCloudWhatsAppSender_UnconfiguredTests.MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, outcome);
    }

    [Fact]
    public async Task Returns_vendor_error_on_cancellation()
    {
        var options = Options.Create(new MetaCloudWhatsAppOptions
        {
            PhoneNumberId = "pid",
            AccessToken = "tok",
            BusinessAccountId = "waba",
            BaseUrl = "https://graph.test.invalid",
        });

        var handler = new ThrowingHandler(new TaskCanceledException("timeout"));
        var http = new HttpClient(handler);
        var sender = new MetaCloudWhatsAppSender(
            options, http, new StaticRecipientLookup("+15551230000"),
            NullLogger<MetaCloudWhatsAppSender>.Instance);

        var outcome = await sender.SendAsync(
            MetaCloudWhatsAppSender_UnconfiguredTests.MakeAttempt());
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, outcome);
    }

    private sealed class StaticRecipientLookup : IWhatsAppRecipientLookup
    {
        private readonly string? _phone;
        public StaticRecipientLookup(string? phone) => _phone = phone;
        public Task<string?> ResolveAsync(string parentAnonId, string minorAnonId, CancellationToken ct)
            => Task.FromResult(_phone);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public CapturingHandler(HttpStatusCode status, string responseBody)
        {
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(_ex);
    }
}
