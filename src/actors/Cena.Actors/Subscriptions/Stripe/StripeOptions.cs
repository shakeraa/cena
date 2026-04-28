// =============================================================================
// Cena Platform — StripeOptions (EPIC-PRR-I PRR-301, ADR-0053)
//
// Configuration binding for Stripe integration. Secrets (SecretKey,
// WebhookSigningSecret) MUST come from environment or key-vault providers,
// NEVER from checked-in config files. Price IDs are non-secret but live
// here so tier+cycle → Stripe price id is a configuration concern, not code.
// =============================================================================

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Configuration section <c>Stripe</c>. Bound via
/// <c>IConfiguration.GetSection("Stripe").Get&lt;StripeOptions&gt;()</c>.
/// </summary>
public sealed class StripeOptions
{
    /// <summary>Configuration root key.</summary>
    public const string SectionName = "Stripe";

    /// <summary>
    /// Stripe secret API key (e.g., <c>sk_test_...</c>). MUST come from env
    /// or secret store; a value starting with "sk_live_" in a non-Production
    /// environment is a fatal misconfiguration and composition root logs an
    /// error before refusing to register.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Webhook-endpoint signing secret (<c>whsec_...</c>). Mandatory — unsigned
    /// webhook traffic is rejected at the endpoint boundary.
    /// </summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>Publishable key, optional. Only used if Stripe.js is wired client-side.</summary>
    public string? PublishableKey { get; set; }

    /// <summary>
    /// Mapping of (tier, billing-cycle) → Stripe price id. Every retail tier
    /// MUST have both Monthly and Annual prices configured.
    /// </summary>
    public StripePriceIdMap PriceIds { get; set; } = new();

    /// <summary>
    /// Where Stripe redirects the parent on successful checkout. Should be a
    /// short confirmation page on the Student SPA.
    /// </summary>
    public string SuccessUrl { get; set; } = "https://cena.test/subscription/confirm";

    /// <summary>Where Stripe redirects on cancel/back.</summary>
    public string CancelUrl { get; set; } = "https://cena.test/pricing";

    /// <summary>
    /// Opt-in flag for the Phase 1C SetupIntent (trial-then-paywall §4.0)
    /// adapter. Default <c>false</c>: hosts that wire the seam should fall
    /// back to <c>InMemorySetupIntentProvider</c> until the Stripe Dashboard
    /// toggle "Use Radar on payment methods saved for future use" has been
    /// flipped on for the account (per
    /// <c>docs/design/trial-recycle-defense-001-research.md §1.3</c> — Radar
    /// SetupIntent compatibility is opt-in). Flipping this flag without
    /// also flipping the dashboard toggle means SetupIntents are created
    /// but not Radar-scored — defeats the point of the §5.7 ledger pairing.
    /// </summary>
    public bool EnableSetupIntents { get; set; }

    /// <summary>True if all required fields are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretKey) &&
        !string.IsNullOrWhiteSpace(WebhookSigningSecret) &&
        PriceIds.HasAllRetailPrices();
}

/// <summary>Mapping of (tier, cycle) → Stripe price id.</summary>
public sealed class StripePriceIdMap
{
    public string BasicMonthly { get; set; } = string.Empty;
    public string BasicAnnual { get; set; } = string.Empty;
    public string PlusMonthly { get; set; } = string.Empty;
    public string PlusAnnual { get; set; } = string.Empty;
    public string PremiumMonthly { get; set; } = string.Empty;
    public string PremiumAnnual { get; set; } = string.Empty;

    /// <summary>True iff every retail tier has both monthly + annual prices set.</summary>
    public bool HasAllRetailPrices() =>
        !string.IsNullOrWhiteSpace(BasicMonthly) &&
        !string.IsNullOrWhiteSpace(BasicAnnual) &&
        !string.IsNullOrWhiteSpace(PlusMonthly) &&
        !string.IsNullOrWhiteSpace(PlusAnnual) &&
        !string.IsNullOrWhiteSpace(PremiumMonthly) &&
        !string.IsNullOrWhiteSpace(PremiumAnnual);
}
