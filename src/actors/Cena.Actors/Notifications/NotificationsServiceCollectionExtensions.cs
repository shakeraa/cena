// =============================================================================
// Cena Platform — Notifications DI wiring (PRR-428)
//
// Single entry point for registering all outbound-notification peripherals
// (Email, SMS, WhatsApp, Web Push) with config-driven backend selection so
// vendor swaps are a config change — not a code change — at every call
// site that takes IEmailSender / ISmsSender / IWhatsAppSender.
//
// Mirrors the AddCenaErrorAggregator pattern at
// src/shared/Cena.Infrastructure/Observability/ErrorAggregator/ServiceCollectionExtensions.cs
//
// Config schema:
//
//   "Notifications": {
//     "Email":    { "Backend": "smtp"   },   // smtp | null
//     "Sms":      { "Backend": "twilio" },   // twilio | null
//     "WhatsApp": { "Backend": "twilio" }    // twilio | null
//   }
//
// Unknown / missing backend → Null variant + warning log. Never throws at
// startup (same graceful-disabled posture as the error aggregator).
// =============================================================================

using Cena.Actors.ParentDigest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers all outbound notification senders + the multi-channel
    /// coordinator behind a config-driven backend selector. Safe to call
    /// from every host — uses TryAdd* for shared deps so a host that
    /// already registered a concrete impl wins.
    /// </summary>
    public static IServiceCollection AddCenaNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var emailBackend = (configuration["Notifications:Email:Backend"]
            ?? "smtp").Trim().ToLowerInvariant();
        var smsBackend = (configuration["Notifications:Sms:Backend"]
            ?? "twilio").Trim().ToLowerInvariant();
        var whatsAppBackend = (configuration["Notifications:WhatsApp:Backend"]
            ?? "twilio").Trim().ToLowerInvariant();

        RegisterEmail(services, emailBackend);
        RegisterSms(services, smsBackend);
        RegisterWhatsApp(services, configuration, whatsAppBackend);

        // Multi-channel coordinator — resolves whichever concrete senders
        // the selector just registered.
        services.TryAddScoped<INotificationChannelService, NotificationChannelService>();

        return services;
    }

    private static void RegisterEmail(IServiceCollection services, string backend)
    {
        switch (backend)
        {
            case "smtp":
                services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
                break;

            case "null":
            case "":
                services.TryAddSingleton<IEmailSender, NullEmailSender>();
                break;

            default:
                services.TryAddSingleton<IEmailSender>(sp =>
                {
                    sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Notifications.Setup")
                        .LogWarning(
                            "Notifications:Email:Backend={Backend} is not recognised. "
                            + "Falling back to NullEmailSender. Expected one of: smtp, null.",
                            backend);
                    return new NullEmailSender(sp.GetRequiredService<ILogger<NullEmailSender>>());
                });
                break;
        }
    }

    private static void RegisterSms(IServiceCollection services, string backend)
    {
        switch (backend)
        {
            case "twilio":
                services.TryAddSingleton<ISmsSender, TwilioSmsSender>();
                break;

            case "null":
            case "":
                services.TryAddSingleton<ISmsSender, NullSmsSender>();
                break;

            default:
                services.TryAddSingleton<ISmsSender>(sp =>
                {
                    sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Notifications.Setup")
                        .LogWarning(
                            "Notifications:Sms:Backend={Backend} is not recognised. "
                            + "Falling back to NullSmsSender. Expected one of: twilio, null.",
                            backend);
                    return new NullSmsSender(sp.GetRequiredService<ILogger<NullSmsSender>>());
                });
                break;
        }
    }

    private static void RegisterWhatsApp(
        IServiceCollection services,
        IConfiguration configuration,
        string backend)
    {
        // Twilio options bound from the shared Twilio section (matches the
        // TwilioWhatsAppSender constructor contract).
        services.Configure<TwilioWhatsAppOptions>(
            configuration.GetSection(TwilioWhatsAppOptions.SectionName));

        // Recipient lookup: the identity-store-backed real implementation
        // lands in a separate task; until then, the Null lookup short-
        // circuits to InvalidRecipient so no ghost messages go out.
        services.TryAddSingleton<IWhatsAppRecipientLookup, NullWhatsAppRecipientLookup>();

        // Named HttpClient for the Twilio adapter so the selector factory
        // can pluck it by name without owning a typed HttpClient registration.
        services.AddHttpClient("TwilioWhatsApp");

        switch (backend)
        {
            case "twilio":
                services.TryAddSingleton<IWhatsAppSender>(sp => new TwilioWhatsAppSender(
                    sp.GetRequiredService<IOptions<TwilioWhatsAppOptions>>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("TwilioWhatsApp"),
                    sp.GetRequiredService<IWhatsAppRecipientLookup>(),
                    sp.GetRequiredService<ILogger<TwilioWhatsAppSender>>()));
                break;

            case "null":
            case "":
                services.TryAddSingleton<IWhatsAppSender, NullWhatsAppSender>();
                break;

            default:
                services.TryAddSingleton<IWhatsAppSender>(sp =>
                {
                    sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Notifications.Setup")
                        .LogWarning(
                            "Notifications:WhatsApp:Backend={Backend} is not recognised. "
                            + "Falling back to NullWhatsAppSender. Expected one of: twilio, null.",
                            backend);
                    return new NullWhatsAppSender();
                });
                break;
        }
    }
}
