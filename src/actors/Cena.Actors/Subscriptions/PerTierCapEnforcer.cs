// =============================================================================
// Cena Platform — PerTierCapEnforcer (EPIC-PRR-I PRR-312, EPIC-PRR-J PRR-400)
//
// Central seam for per-tier cap enforcement. Consumed by:
//   - Photo diagnostic intake (EPIC-PRR-J)
//   - LLM router Sonnet-escalation check (EPIC-PRR-B)
//   - Hint ladder (existing HintLadderEndpoint)
//
// Decisions returned:
//   - Allow: within caps; proceed
//   - SoftCapReached: still allow, but frontend should show upsell UX
//   - HardCapReached: block; frontend routes to contact-support or upgrade
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Outcome of a per-tier cap check.</summary>
public enum CapDecision
{
    /// <summary>Within caps; proceed normally.</summary>
    Allow = 0,

    /// <summary>At or above soft cap; proceed but show positive-framing upsell.</summary>
    SoftCapReached = 1,

    /// <summary>Above hard cap; block the operation.</summary>
    HardCapReached = 2,
}

/// <summary>Cap counter a caller is checking against.</summary>
public enum CapCounter
{
    PhotoDiagnosticPerMonth,
    SonnetEscalationPerWeek,
    HintRequestPerMonth,
}

/// <summary>Result of a cap check including next-action hint for UX.</summary>
/// <param name="Decision">Allow / SoftCapReached / HardCapReached.</param>
/// <param name="CurrentUsage">How many times the counter has fired this period.</param>
/// <param name="SoftCap">Soft cap for this tier; -1 = unlimited (in which case Decision is always Allow).</param>
/// <param name="HardCap">Hard cap for this tier; -1 = no hard cap.</param>
public sealed record CapCheckResult(
    CapDecision Decision,
    int CurrentUsage,
    int SoftCap,
    int HardCap);

/// <summary>Seam for enforcing per-tier caps. Pure w.r.t. entitlement + usage counts.</summary>
public interface IPerTierCapEnforcer
{
    /// <summary>
    /// Decide whether the current usage violates the entitlement's caps for
    /// the given counter.
    /// </summary>
    CapCheckResult Check(StudentEntitlementView entitlement, CapCounter counter, int currentUsage);
}

/// <summary>Default implementation — pure function over caps + usage count.</summary>
public sealed class PerTierCapEnforcer : IPerTierCapEnforcer
{
    /// <inheritdoc/>
    public CapCheckResult Check(StudentEntitlementView entitlement, CapCounter counter, int currentUsage)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        if (currentUsage < 0)
        {
            throw new ArgumentException("Usage count must be non-negative.", nameof(currentUsage));
        }

        var caps = entitlement.Caps;
        var (softCap, hardCap) = counter switch
        {
            CapCounter.PhotoDiagnosticPerMonth =>
                (caps.PhotoDiagnosticsPerMonth, caps.PhotoDiagnosticsHardCapPerMonth),
            CapCounter.SonnetEscalationPerWeek =>
                (caps.SonnetEscalationsPerWeek, caps.SonnetEscalationsPerWeek),
            CapCounter.HintRequestPerMonth =>
                (caps.HintRequestsPerMonth, caps.HintRequestsPerMonth),
            _ => throw new ArgumentOutOfRangeException(nameof(counter)),
        };

        // Unlimited sentinel short-circuits to Allow regardless of usage.
        if (softCap == UsageCaps.Unlimited)
        {
            return new CapCheckResult(CapDecision.Allow, currentUsage, softCap, hardCap);
        }

        // Hard-cap: usage >= hard cap → block.
        if (hardCap != UsageCaps.Unlimited && hardCap > 0 && currentUsage >= hardCap)
        {
            return new CapCheckResult(CapDecision.HardCapReached, currentUsage, softCap, hardCap);
        }

        // Zero cap (Unsubscribed or Basic's no-photo-diagnostic): hard-block.
        if (softCap == 0)
        {
            return new CapCheckResult(CapDecision.HardCapReached, currentUsage, softCap, hardCap);
        }

        // Soft cap reached but not hard.
        if (currentUsage >= softCap)
        {
            return new CapCheckResult(CapDecision.SoftCapReached, currentUsage, softCap, hardCap);
        }

        return new CapCheckResult(CapDecision.Allow, currentUsage, softCap, hardCap);
    }
}
