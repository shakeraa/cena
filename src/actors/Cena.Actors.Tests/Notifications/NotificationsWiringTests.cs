// =============================================================================
// PRR-428: Notifications DI wiring tests.
//
// Proves that AddCenaNotifications resolves a concrete IEmailSender /
// ISmsSender / IWhatsAppSender for each supported backend, and falls back
// to the Null* variants for null / unknown / missing config without
// throwing at startup.
// =============================================================================

using Cena.Actors.Notifications;
using Cena.Actors.ParentDigest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cena.Actors.Tests.Notifications;

public class NotificationsWiringTests
{
    private static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        // SmtpEmailSender + TwilioSmsSender take IConfiguration in their
        // constructors. Real hosts register this automatically via the web
        // builder; the test fixture wires it explicitly.
        services.AddSingleton<IConfiguration>(cfg);
        services.AddCenaNotifications(cfg);
        return services.BuildServiceProvider();
    }

    // ------------------------------------------------------------------
    // Email backend selection
    // ------------------------------------------------------------------

    [Fact]
    public void Default_email_backend_is_smtp()
    {
        using var sp = Build(new Dictionary<string, string?>());
        var sender = sp.GetRequiredService<IEmailSender>();
        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void Email_backend_null_resolves_NullEmailSender()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Email:Backend"] = "null"
        });
        var sender = sp.GetRequiredService<IEmailSender>();
        Assert.IsType<NullEmailSender>(sender);
        Assert.False(sender.IsConfigured);
    }

    [Fact]
    public void Email_backend_unknown_falls_back_to_Null()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Email:Backend"] = "bogus-provider"
        });
        var sender = sp.GetRequiredService<IEmailSender>();
        Assert.IsType<NullEmailSender>(sender);
    }

    [Fact]
    public async Task NullEmailSender_returns_NOT_CONFIGURED_without_throwing()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Email:Backend"] = "null"
        });
        var sender = sp.GetRequiredService<IEmailSender>();
        var result = await sender.SendAsync("parent@example.com", "s", "b");
        Assert.False(result.Success);
        Assert.Equal("NOT_CONFIGURED", result.ErrorCode);
    }

    // ------------------------------------------------------------------
    // SMS backend selection
    // ------------------------------------------------------------------

    [Fact]
    public void Default_sms_backend_is_twilio()
    {
        using var sp = Build(new Dictionary<string, string?>());
        var sender = sp.GetRequiredService<ISmsSender>();
        Assert.IsType<TwilioSmsSender>(sender);
    }

    [Fact]
    public void Sms_backend_null_resolves_NullSmsSender()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Sms:Backend"] = "null"
        });
        var sender = sp.GetRequiredService<ISmsSender>();
        Assert.IsType<NullSmsSender>(sender);
    }

    [Fact]
    public void Sms_backend_unknown_falls_back_to_Null()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Sms:Backend"] = "carrier-pigeon"
        });
        var sender = sp.GetRequiredService<ISmsSender>();
        Assert.IsType<NullSmsSender>(sender);
    }

    [Fact]
    public async Task NullSmsSender_returns_NOT_CONFIGURED()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Sms:Backend"] = "null"
        });
        var sender = sp.GetRequiredService<ISmsSender>();
        var result = await sender.SendAsync("+972501234567", "hi");
        Assert.False(result.Success);
        Assert.Equal("NOT_CONFIGURED", result.ErrorCode);
    }

    // ------------------------------------------------------------------
    // WhatsApp backend selection
    // ------------------------------------------------------------------

    [Fact]
    public void Default_whatsapp_backend_is_twilio()
    {
        using var sp = Build(new Dictionary<string, string?>());
        var sender = sp.GetRequiredService<IWhatsAppSender>();
        Assert.IsType<TwilioWhatsAppSender>(sender);
        Assert.Equal("twilio", sender.VendorId);
    }

    [Fact]
    public void WhatsApp_backend_null_resolves_NullWhatsAppSender()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Backend"] = "null"
        });
        var sender = sp.GetRequiredService<IWhatsAppSender>();
        Assert.IsType<NullWhatsAppSender>(sender);
        Assert.False(sender.IsConfigured);
    }

    [Fact]
    public void WhatsApp_backend_meta_resolves_MetaCloudWhatsAppSender()
    {
        // PRR-429: "meta" backend selects MetaCloudWhatsAppSender. The
        // MetaCloud section is bound here so IsConfigured flips true and
        // the selector wires through the named HttpClient factory.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Backend"] = "meta",
            ["MetaCloud:PhoneNumberId"] = "1234567890",
            ["MetaCloud:AccessToken"] = "dummy-token",
            ["MetaCloud:BusinessAccountId"] = "987654321"
        });
        var sender = sp.GetRequiredService<IWhatsAppSender>();
        Assert.IsType<MetaCloudWhatsAppSender>(sender);
        Assert.Equal("meta", sender.VendorId);
        Assert.True(sender.IsConfigured);
    }

    [Fact]
    public void WhatsApp_backend_unknown_falls_back_to_Null()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Backend"] = "some-vendor"
        });
        var sender = sp.GetRequiredService<IWhatsAppSender>();
        Assert.IsType<NullWhatsAppSender>(sender);
    }

    [Fact]
    public void WhatsApp_recipient_lookup_is_always_registered()
    {
        // Even with WhatsApp disabled, the lookup must resolve — parent
        // digest code takes a nullable dependency and we want explicit
        // registration (Null impl) rather than DI-missing at runtime.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Backend"] = "null"
        });
        var lookup = sp.GetRequiredService<IWhatsAppRecipientLookup>();
        Assert.NotNull(lookup);
        Assert.IsType<NullWhatsAppRecipientLookup>(lookup);
    }

    [Fact]
    public async Task NullWhatsAppSender_returns_VendorError_without_throwing()
    {
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Backend"] = "null"
        });
        var sender = sp.GetRequiredService<IWhatsAppSender>();
        var attempt = new WhatsAppDeliveryAttempt(
            CorrelationId: "corr-1",
            ParentAnonId: "p-1",
            MinorAnonId: "m-1",
            TemplateId: "tpl-1",
            Locale: "en",
            AttemptNumber: 1,
            AttemptedAtUtc: DateTimeOffset.UtcNow);
        var outcome = await sender.SendAsync(attempt);
        Assert.Equal(WhatsAppDeliveryOutcome.VendorError, outcome);
    }

    // ------------------------------------------------------------------
    // Cross-cutting
    // ------------------------------------------------------------------

    [Fact]
    public void Notification_channel_service_is_registered()
    {
        // INotificationChannelService has upstream deps (IDocumentStore,
        // IAnalyticsRollupService, IClock) that the host Program.cs wires
        // up. AddCenaNotifications only owns the service *descriptor*;
        // verify the registration at the IServiceCollection level (the
        // resolve-time dependency graph is a host integration concern).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        services.AddCenaNotifications(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>()).Build());
        Assert.Contains(services, d =>
            d.ServiceType == typeof(INotificationChannelService)
            && d.ImplementationType == typeof(NotificationChannelService));
    }

    [Fact]
    public void Mixed_backends_resolve_independently()
    {
        // Email=smtp, Sms=null, WhatsApp=twilio — each channel picks its
        // own backend without cross-contamination.
        using var sp = Build(new Dictionary<string, string?>
        {
            ["Notifications:Email:Backend"] = "smtp",
            ["Notifications:Sms:Backend"] = "null",
            ["Notifications:WhatsApp:Backend"] = "twilio"
        });
        Assert.IsType<SmtpEmailSender>(sp.GetRequiredService<IEmailSender>());
        Assert.IsType<NullSmsSender>(sp.GetRequiredService<ISmsSender>());
        Assert.IsType<TwilioWhatsAppSender>(sp.GetRequiredService<IWhatsAppSender>());
    }
}
