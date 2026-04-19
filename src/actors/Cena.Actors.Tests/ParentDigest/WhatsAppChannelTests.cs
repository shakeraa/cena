// =============================================================================
// RDY-069 Phase 1A — WhatsApp channel tests.
// =============================================================================

using Cena.Actors.ParentDigest;
using Xunit;

namespace Cena.Actors.Tests.ParentDigest;

public class ParentChannelPreferenceTests
{
    [Fact]
    public void Both_opt_in_both_channels()
    {
        var p = new ParentChannelPreference(
            "parent-1", "minor-1", true, true, "phone-hmac", DateTimeOffset.UtcNow);
        Assert.True(p.HasAnyOptIn);
        Assert.True(p.CanDeliverWhatsApp);
    }

    [Fact]
    public void Email_only_still_has_opt_in()
    {
        var p = new ParentChannelPreference(
            "parent-1", "minor-1", true, false, null, DateTimeOffset.UtcNow);
        Assert.True(p.HasAnyOptIn);
        Assert.False(p.CanDeliverWhatsApp);
    }

    [Fact]
    public void WhatsApp_opt_in_without_phone_cannot_deliver()
    {
        var p = new ParentChannelPreference(
            "parent-1", "minor-1", false, true, null, DateTimeOffset.UtcNow);
        Assert.True(p.HasAnyOptIn);
        Assert.False(p.CanDeliverWhatsApp);
    }

    [Fact]
    public void Neither_channel_opted_in()
    {
        var p = new ParentChannelPreference(
            "parent-1", "minor-1", false, false, null, DateTimeOffset.UtcNow);
        Assert.False(p.HasAnyOptIn);
    }
}

public class WhatsAppTemplateTests
{
    [Theory]
    [InlineData(PreApprovalStatus.Pending, false)]
    [InlineData(PreApprovalStatus.Approved, true)]
    [InlineData(PreApprovalStatus.Rejected, false)]
    [InlineData(PreApprovalStatus.Paused, false)]
    public void CanSend_only_when_approved(PreApprovalStatus status, bool canSend)
    {
        var t = new WhatsAppTemplate("tpl-1", "en", status, "hash");
        Assert.Equal(canSend, t.CanSend);
    }
}

public class NullWhatsAppSenderTests
{
    [Fact]
    public async Task Always_returns_vendor_error_and_not_configured()
    {
        var s = new NullWhatsAppSender();
        Assert.False(s.IsConfigured);
        Assert.Equal("null", s.VendorId);
        var result = await s.SendAsync(new WhatsAppDeliveryAttempt(
            CorrelationId: "corr-1",
            ParentAnonId: "parent-1",
            MinorAnonId: "minor-1",
            TemplateId: "tpl-1",
            Locale: "en",
            AttemptNumber: 1,
            AttemptedAtUtc: DateTimeOffset.UtcNow));
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, result);
    }
}

public class WhatsAppDispatcherTests
{
    private static ParentChannelPreference Prefs(bool email, bool wa, string? phoneHmac)
        => new("parent-1", "minor-1", email, wa, phoneHmac, DateTimeOffset.UtcNow);

    private static WhatsAppTemplate ApprovedTemplate()
        => new("tpl-1", "en", PreApprovalStatus.Approved, "hash");

    [Fact]
    public void Skips_when_parent_opted_out_of_all_channels()
    {
        var d = WhatsAppDispatcher.Decide(
            Prefs(false, false, null),
            ApprovedTemplate(),
            WhatsAppSenderQuality.Green,
            emailAvailable: true);
        Assert.Equal(WhatsAppDispatcher.Decision.SkipAllChannels, d);
    }

    [Fact]
    public void Attempts_whatsapp_when_everything_green()
    {
        var d = WhatsAppDispatcher.Decide(
            Prefs(true, true, "phone"),
            ApprovedTemplate(),
            WhatsAppSenderQuality.Green,
            emailAvailable: true);
        Assert.Equal(WhatsAppDispatcher.Decision.AttemptWhatsApp, d);
    }

    [Fact]
    public void Falls_back_to_email_when_template_unapproved()
    {
        var d = WhatsAppDispatcher.Decide(
            Prefs(true, true, "phone"),
            new WhatsAppTemplate("tpl-1", "en", PreApprovalStatus.Pending, "hash"),
            WhatsAppSenderQuality.Green,
            emailAvailable: true);
        Assert.Equal(WhatsAppDispatcher.Decision.FallBackToEmail, d);
    }

    [Fact]
    public void Falls_back_to_email_when_sender_red()
    {
        var d = WhatsAppDispatcher.Decide(
            Prefs(true, true, "phone"),
            ApprovedTemplate(),
            WhatsAppSenderQuality.Red,
            emailAvailable: true);
        Assert.Equal(WhatsAppDispatcher.Decision.FallBackToEmail, d);
    }

    [Fact]
    public void Skips_when_whatsapp_fails_and_no_email()
    {
        var d = WhatsAppDispatcher.Decide(
            Prefs(false, true, "phone"),
            new WhatsAppTemplate("tpl-1", "en", PreApprovalStatus.Rejected, "hash"),
            WhatsAppSenderQuality.Green,
            emailAvailable: true);
        Assert.Equal(WhatsAppDispatcher.Decision.SkipAllChannels, d);
    }

    [Fact]
    public void Attempts_whatsapp_even_with_yellow_sender_quality()
    {
        // Yellow is a warning, not a circuit-break. Only Red stops us.
        var d = WhatsAppDispatcher.Decide(
            Prefs(true, true, "phone"),
            ApprovedTemplate(),
            WhatsAppSenderQuality.Yellow,
            emailAvailable: true);
        Assert.Equal(WhatsAppDispatcher.Decision.AttemptWhatsApp, d);
    }
}
