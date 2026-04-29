// =============================================================================
// Cena Platform — Variant Rate Limit Policy (PRR-265, ADR-0059 §15.5 R1)
//
// Source-of-truth for the per-tier per-source caps from ADR-0059 §15.5:
//
//   | Tier              | Structural per-day | Structural per-source | Parametric per-day | Parametric per-source |
//   | Free + payment    |                  6 |                     2 |                  0 |                     0 |
//   | Free no payment   |                  0 |                     0 |                  0 |                     0 |
//   | Basic / Plus      |                 25 |                     2 |                 50 |                     5 |
//   | Premium / School  |                 25 |                     2 |                 50 |                     5 |
//   | Trial             |                 25 |                     2 |                 50 |                     5 |
//
// Per-source caps use a 30-day sliding window (cogsci spacing benefit per
// ADR-0059 §14.3.1 — too generous as lifetime, too short as daily). Per-day
// caps use a 24-hour rolling window (no midnight burst-bypass).
//
// Why a separate policy seam:
//   - PRR-253 will extend IInstitutePricingResolver / ResolvedPricing with
//     VariantStructuralPerDay, VariantStructuralPerSource, etc. Once that
//     lands, the policy implementation can read from ResolvedPricing
//     instead of (or in addition to) these compile-time defaults — without
//     touching any consumer.
//   - Per-institute overrides are commercially relevant: a B2B contract
//     might raise the institute-day cap for a high-volume cohort. Routing
//     through this policy keeps that override path single-source-of-truth.
//
// Why the values are constants (not config flags): ADR-0048 design
// non-negotiables ban dark-pattern engagement. A configurable cap is a
// cap that can drift with promotional pressure. The cogsci-validated
// numbers are constants in code; commercial overrides go through the
// pricing-resolver path which is reviewed against the spec.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Infrastructure.RateLimiting;

namespace Cena.Actors.Variants;

/// <summary>
/// Resolved caps for one (tier, kind) tuple. Per-day is enforced via a
/// 24-hour sliding window; per-source is a 30-day sliding window per the
/// header rationale.
/// </summary>
public sealed record VariantRateLimitCaps(
    int PerDayLimit,
    int PerSourceLimit,
    int InstitutePerDayLimit,
    int InstitutePerSourceLimit,
    bool RequiresPaymentVerification);

/// <summary>
/// Per-tier policy lookup. Pure function; no I/O. The compile-time defaults
/// from ADR-0059 §15.5 are the floor — when PRR-253's IInstitutePricingResolver
/// extension lands, the implementation can override per-institute via the
/// resolver while honoring this contract.
/// </summary>
public interface IVariantRateLimitPolicy
{
    /// <summary>
    /// The 24-hour sliding window for per-day scopes.
    /// </summary>
    TimeSpan PerDayWindow { get; }

    /// <summary>
    /// The 30-day sliding window for per-source scopes (cogsci spacing
    /// benefit per ADR-0059 §14.3.1).
    /// </summary>
    TimeSpan PerSourceWindow { get; }

    /// <summary>
    /// Resolve caps for (tier, kind, paymentVerified) at the given time.
    /// </summary>
    /// <param name="tier">The student's effective tier (or curator-author tier).</param>
    /// <param name="kind">Structural or parametric.</param>
    /// <param name="paymentVerified">
    /// True iff the caller has a verified payment-method or institute-SSO
    /// linkage. Required for free-tier structural per ADR-0059 §15.5
    /// (defeats account-rotation cache exfiltration, redteam M-1).
    /// </param>
    /// <param name="instituteId">Institute owning the variant generation, or null for self-pay.</param>
    /// <param name="asOfUtc">Resolution timestamp (for time-varying overrides).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VariantRateLimitCaps> ResolveAsync(
        SubscriptionTier tier,
        VariantKind kind,
        bool paymentVerified,
        string? instituteId,
        DateTimeOffset asOfUtc,
        CancellationToken ct);
}
