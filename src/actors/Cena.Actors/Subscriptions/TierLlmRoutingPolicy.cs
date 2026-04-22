// =============================================================================
// Cena Platform — TierLlmRoutingPolicy (EPIC-PRR-I PRR-311, ADR-0026)
//
// Per-tier LLM routing decision. Inputs:
//   - Current tier (from entitlement)
//   - Complexity estimate of the call (0..1; existing ADR-0026 pipeline)
//   - Current weekly escalation usage
//
// Output: which model tier to route to + whether a soft-cap upsell fired.
// The LLM router (EPIC-PRR-B) reads this decision on every call.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>3-tier LLM target per ADR-0026.</summary>
public enum LlmModelTier
{
    AgentBoosterWasm = 0,
    Haiku = 1,
    Sonnet = 2,
}

/// <summary>Routing decision returned to the LLM router.</summary>
/// <param name="Target">Which model to hit.</param>
/// <param name="SoftCapReached">True when the student is at/above weekly Sonnet cap.</param>
/// <param name="DegradeReason">Optional reason when the decision is a degrade (e.g., Basic-cap-exhausted).</param>
public sealed record LlmRoutingDecision(
    LlmModelTier Target,
    bool SoftCapReached,
    string? DegradeReason);

/// <summary>Seam for tier-aware LLM routing.</summary>
public interface ITierLlmRoutingPolicy
{
    /// <summary>
    /// Pick a model tier given entitlement + complexity + usage. Complexity
    /// is the ADR-0026 estimate (0..1); 0.30+ is "complex".
    /// </summary>
    LlmRoutingDecision Decide(
        StudentEntitlementView entitlement,
        double complexityEstimate,
        int weeklySonnetEscalationCount);
}

/// <summary>Default ADR-0026 policy: Haiku-first for Basic, Sonnet for Plus/Premium on complex.</summary>
public sealed class TierLlmRoutingPolicy : ITierLlmRoutingPolicy
{
    /// <summary>Complexity threshold above which a call is "complex".</summary>
    public const double ComplexityThreshold = 0.30;

    /// <inheritdoc/>
    public LlmRoutingDecision Decide(
        StudentEntitlementView entitlement,
        double complexityEstimate,
        int weeklySonnetEscalationCount)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        if (weeklySonnetEscalationCount < 0)
        {
            throw new ArgumentException("Usage count must be non-negative.", nameof(weeklySonnetEscalationCount));
        }

        var isComplex = complexityEstimate >= ComplexityThreshold;
        var caps = entitlement.Caps;

        // Simple calls always go to Haiku (or below) regardless of tier.
        if (!isComplex)
        {
            return new LlmRoutingDecision(LlmModelTier.Haiku, false, null);
        }

        switch (entitlement.EffectiveTier)
        {
            case SubscriptionTier.Unsubscribed:
                return new LlmRoutingDecision(
                    LlmModelTier.Haiku, false,
                    DegradeReason: "no-subscription");

            case SubscriptionTier.Basic:
                // Weekly Sonnet cap for Basic tier
                if (caps.SonnetEscalationsPerWeek != UsageCaps.Unlimited &&
                    weeklySonnetEscalationCount >= caps.SonnetEscalationsPerWeek)
                {
                    return new LlmRoutingDecision(
                        LlmModelTier.Haiku, false,
                        DegradeReason: "basic-weekly-sonnet-cap");
                }
                return new LlmRoutingDecision(LlmModelTier.Sonnet, false, null);

            case SubscriptionTier.Plus:
            case SubscriptionTier.Premium:
            case SubscriptionTier.SchoolSku:
                // Plus/Premium/SchoolSku get Sonnet on complex; Premium has
                // a 100-softcap per month elsewhere (see PerTierCapEnforcer).
                var sonnetSoftCap =
                    entitlement.EffectiveTier == SubscriptionTier.Premium &&
                    caps.SonnetEscalationsPerWeek != UsageCaps.Unlimited &&
                    weeklySonnetEscalationCount >= caps.SonnetEscalationsPerWeek;
                return new LlmRoutingDecision(LlmModelTier.Sonnet, sonnetSoftCap, null);

            default:
                return new LlmRoutingDecision(LlmModelTier.Haiku, false, "unknown-tier");
        }
    }
}
