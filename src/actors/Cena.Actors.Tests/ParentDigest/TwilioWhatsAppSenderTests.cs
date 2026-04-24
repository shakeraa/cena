// =============================================================================
// RDY-069 Phase 1B — TwilioWhatsAppSender tests.
// =============================================================================

using System.Net;
using System.Net.Http;
using Cena.Actors.ParentDigest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.ParentDigest;

public class TwilioWhatsAppOptionsTests
{
    [Fact]
    public void IsComplete_requires_all_three_credentials()
    {
        var full = new TwilioWhatsAppOptions
        {
            AccountSid = "sid", AuthToken = "token", WhatsAppFromNumber = "+15551234567"
        };
        Assert.True(full.IsComplete);
    }

    [Theory]
    [InlineData(null, "token", "+1")]
    [InlineData("sid", null, "+1")]
    [InlineData("sid", "token", null)]
    [InlineData("", "token", "+1")]
    [InlineData("sid", "  ", "+1")]
    public void IsComplete_false_when_any_credential_blank(
        string? sid, string? token, string? from)
    {
        var opts = new TwilioWhatsAppOptions
        {
            AccountSid = sid, AuthToken = token, WhatsAppFromNumber = from,
        };
        Assert.False(opts.IsComplete);
    }
}

public class TwilioWhatsAppSender_MappingTests
{
    [Theory]
    [InlineData(HttpStatusCode.Created, WhatsAppDeliveryOutcome.Accepted)]
    [InlineData(HttpStatusCode.OK, WhatsAppDeliveryOutcome.Accepted)]
    [InlineData(HttpStatusCode.Accepted, WhatsAppDeliveryOutcome.Accepted)]
    [InlineData(HttpStatusCode.TooManyRequests, WhatsAppDeliveryOutcome.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, WhatsAppDeliveryOutcome.InvalidRecipient)]
    [InlineData(HttpStatusCode.BadRequest, WhatsAppDeliveryOutcome.InvalidRecipient)]
    [InlineData(HttpStatusCode.Unauthorized, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.Forbidden, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.InternalServerError, WhatsAppDeliveryOutcome.VendorError)]
    [InlineData(HttpStatusCode.BadGateway, WhatsAppDeliveryOutcome.VendorError)]
    public void Maps_twilio_status_to_vendor_neutral_outcome(
        HttpStatusCode status, WhatsAppDeliveryOutcome expected)
    {
        Assert.Equal(
            expected,
            TwilioWhatsAppSender.MapTwilioStatus(status, "corr-1"));
    }
}

public class TwilioWhatsAppSender_UnconfiguredTests
{
    [Fact]
    public async Task Returns_vendor_error_when_credentials_missing()
    {
        var options = Options.Create(new TwilioWhatsAppOptions());
        var http = new HttpClient();
        var sender = new TwilioWhatsAppSender(
            options, http, new NullWhatsAppRecipientLookup(), NullLogger<TwilioWhatsAppSender>.Instance);

        Assert.False(sender.IsConfigured);
        Assert.Equal("twilio", sender.VendorId);

        var outcome = await sender.SendAsync(new WhatsAppDeliveryAttempt(
            CorrelationId: "corr-1",
            ParentAnonId: "parent-1",
            MinorAnonId: "minor-1",
            TemplateId: "tpl-1",
            Locale: "en",
            AttemptNumber: 1,
            AttemptedAtUtc: DateTimeOffset.UtcNow));
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, outcome);
    }

    [Fact]
    public async Task Returns_invalid_recipient_when_lookup_returns_null()
    {
        var options = Options.Create(new TwilioWhatsAppOptions
        {
            AccountSid = "sid",
            AuthToken = "token",
            WhatsAppFromNumber = "+15551234567",
        });
        var http = new HttpClient(new NoOpHandler());
        var sender = new TwilioWhatsAppSender(
            options, http, new NullWhatsAppRecipientLookup(), NullLogger<TwilioWhatsAppSender>.Instance);

        Assert.True(sender.IsConfigured);

        var outcome = await sender.SendAsync(new WhatsAppDeliveryAttempt(
            CorrelationId: "corr-1",
            ParentAnonId: "parent-1",
            MinorAnonId: "minor-1",
            TemplateId: "tpl-1",
            Locale: "en",
            AttemptNumber: 1,
            AttemptedAtUtc: DateTimeOffset.UtcNow));
        Assert.Equal(WhatsAppDeliveryOutcome.InvalidRecipient, outcome);
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
    }
}
