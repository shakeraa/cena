// =============================================================================
// Cena Platform — TrialKind enum (EPIC-PRR-I, trial-then-paywall §4.0, §11.6)
//
// Discriminator captured at trial-start so funnel analytics, lifecycle email
// targeting, and consent-replay can distinguish the three legitimate
// trial-origination paths. Values persisted on TrialStarted_V1 — never
// renumber.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Origin of a trial. Captured on <see cref="Events.TrialStarted_V1"/> and
/// surfaced in funnel analytics + lifecycle-email decisions.
/// </summary>
public enum TrialKind
{
    /// <summary>
    /// Self-pay path (consent tier ≥ Teen16to17). The student supplied
    /// a payment method on their own device. Consent-tier authorisation
    /// is re-read at trial-start from the consent aggregate (design §4.0.2).
    /// </summary>
    SelfPay = 0,

    /// <summary>
    /// Parent-pay path. Parent confirmed SetupIntent on their own device
    /// after parent-child binding (design §4.0). Required for child and
    /// teen-13-15 consent tiers.
    /// </summary>
    ParentPay = 1,

    /// <summary>
    /// Institute-issued trial code redeemed via the redeem-code flow
    /// (design §5.4). No payment method involved; the institute pays out
    /// of band.
    /// </summary>
    InstituteCode = 2,
}
