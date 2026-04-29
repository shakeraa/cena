// =============================================================================
// Cena Platform — Default Variant Rate Limit Policy (PRR-265, ADR-0059 §15.5)
//
// Static-defaults implementation. Reads from compile-time constants for the
// per-tier caps, and the per-day/per-source window durations. When PRR-253
// lands the IInstitutePricingResolver extension fields, swap the constructor
// to take that resolver and prefer override values; the consumer surface
// (IVariantRateLimitPolicy) stays unchanged.
//
// Per-tier matrix is cogsci-validated (ADR-0059 §14.3.1) and finops-bounded
// (ADR-0059 §14.2 item 1). Editing any value here must land via a PR
// reviewed against the design memo.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Infrastructure.RateLimiting;

namespace Cena.Actors.Variants;

/// <summary>
/// Compile-time default policy. Single source of truth for ADR-0059 §15.5
/// caps.
/// </summary>
public sealed class VariantRateLimitPolicy : IVariantRateLimitPolicy
{
    /// <inheritdoc/>
    public TimeSpan PerDayWindow => TimeSpan.FromHours(24);

    /// <inheritdoc/>
    public TimeSpan PerSourceWindow => TimeSpan.FromDays(30);

    // ── Per-tier per-day structural caps (ADR-0059 §15.5 row 1) ──
    /// <summary>Free + payment-method-verified, structural variants per day.</summary>
    public const int FreeWithPaymentStructuralPerDay = 6;

    /// <summary>Paid tiers, structural variants per day.</summary>
    public const int PaidStructuralPerDay = 25;

    /// <summary>
    /// Free tier without payment-method verification: zero. Per ADR-0059
    /// §15.5 redteam M-1 (defeats account-rotation cache exfiltration).
    /// </summary>
    public const int FreeNoPaymentStructuralPerDay = 0;

    // ── Per-tier per-day parametric caps (ADR-0059 §15.5 row 2) ──
    /// <summary>
    /// Free tier (any payment status): zero. Parametric is paid-only per
    /// Q-A (Ministry §16 derivative-works distance).
    /// </summary>
    public const int FreeParametricPerDay = 0;

    /// <summary>Paid tiers, parametric variants per day.</summary>
    public const int PaidParametricPerDay = 50;

    // ── Per-source caps (ADR-0059 §15.5 columns) — uniform across paid tiers ──
    /// <summary>Per-source structural cap (paid). Free + payment respects this floor too.</summary>
    public const int StructuralPerSource = 2;

    /// <summary>Per-source parametric cap (paid).</summary>
    public const int ParametricPerSource = 5;

    // ── Per-institute aggregate ceilings (ADR-0059 §14.2 item 1 cost ceiling) ──
    // The per-(institute, day) ceiling is a finops invariant: even if every
    // student in a 30-seat classroom hits the per-student cap, the institute
    // cumulative spend is bounded. 30 students × 25 paid-day cap = 750 — we
    // match that as the high-water mark for paid contracts; free institutes
    // get a lower ceiling so a free institute cannot accidentally generate
    // paid-tier-equivalent spend.
    /// <summary>Per-institute per-day structural ceiling for paid contracts.</summary>
    public const int InstitutePaidStructuralPerDay = 750;

    /// <summary>Per-institute per-day parametric ceiling for paid contracts.</summary>
    public const int InstitutePaidParametricPerDay = 1500;

    /// <summary>Per-institute per-day ceiling for free / unsubscribed institutes.</summary>
    public const int InstituteFreeStructuralPerDay = 60;

    /// <summary>Per-institute per-source per-day ceiling.</summary>
    public const int InstitutePerSourcePerDay = 30;

    /// <inheritdoc/>
    public Task<VariantRateLimitCaps> ResolveAsync(
        SubscriptionTier tier,
        VariantKind kind,
        bool paymentVerified,
        string? instituteId,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        var caps = ResolveSync(tier, kind, paymentVerified);
        return Task.FromResult(caps);
    }

    /// <summary>
    /// Synchronous resolver. Exposed for unit tests + the architecture
    /// invariant test that verifies the matrix has not drifted from
    /// ADR-0059 §15.5.
    /// </summary>
    public static VariantRateLimitCaps ResolveSync(
        SubscriptionTier tier,
        VariantKind kind,
        bool paymentVerified)
    {
        var (perDayLimit, perSourceLimit, requiresPayment) =
            ResolvePerStudentCaps(tier, kind, paymentVerified);
        var (institutePerDay, institutePerSourcePerDay) =
            ResolvePerInstituteCaps(tier, kind);
        return new VariantRateLimitCaps(
            PerDayLimit: perDayLimit,
            PerSourceLimit: perSourceLimit,
            InstitutePerDayLimit: institutePerDay,
            InstitutePerSourceLimit: institutePerSourcePerDay,
            RequiresPaymentVerification: requiresPayment);
    }

    private static (int PerDay, int PerSource, bool RequiresPayment) ResolvePerStudentCaps(
        SubscriptionTier tier, VariantKind kind, bool paymentVerified)
    {
        // Unsubscribed (free): structural-only and only with payment-method.
        if (tier == SubscriptionTier.Unsubscribed)
        {
            if (kind == VariantKind.Parametric)
                return (0, 0, true);
            return paymentVerified
                ? (FreeWithPaymentStructuralPerDay, StructuralPerSource, false)
                : (FreeNoPaymentStructuralPerDay, 0, true);
        }

        // Trial inherits Plus/Premium caps for variant generation parity.
        // Paid tiers (Basic / Plus / Premium / SchoolSku / TrialPlus) all
        // share the same ADR-0059 §15.5 row 2 caps for variants — the tier
        // distinction is on price + non-variant features, not on variant cadence.
        return kind switch
        {
            VariantKind.Structural =>
                (PaidStructuralPerDay, StructuralPerSource, false),
            VariantKind.Parametric =>
                (PaidParametricPerDay, ParametricPerSource, false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown variant kind."),
        };
    }

    private static (int PerDay, int PerSourcePerDay) ResolvePerInstituteCaps(
        SubscriptionTier tier, VariantKind kind)
    {
        // Self-pay students don't have an institute; the gate is expected
        // to skip per-institute scopes when instituteId is null. We still
        // compute defaults so the policy is total.
        if (tier == SubscriptionTier.Unsubscribed)
        {
            return kind == VariantKind.Structural
                ? (InstituteFreeStructuralPerDay, InstitutePerSourcePerDay)
                : (0, 0);   // free institutes: no parametric
        }

        return kind switch
        {
            VariantKind.Structural =>
                (InstitutePaidStructuralPerDay, InstitutePerSourcePerDay),
            VariantKind.Parametric =>
                (InstitutePaidParametricPerDay, InstitutePerSourcePerDay),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown variant kind."),
        };
    }
}
