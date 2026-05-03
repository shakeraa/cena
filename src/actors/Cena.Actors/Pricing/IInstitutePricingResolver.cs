// =============================================================================
// Cena Platform — IInstitutePricingResolver (prr-244, ADR-0050 Q5)
//
// The SINGLE seam every pricing-bearing code path must use. Billing jobs,
// per-seat counters, Stripe metadata, finance dashboards all go through
// ResolveAsync — never read the YAML or the override doc directly.
//
// Enforcement: NoHardcodedPricingTest scans src/** for unregistered
// dollar literals under pricing-adjacent namespaces and fails CI on
// violations (allowlist initially empty).
//
// Semantics:
//   - If an active override exists for the institute AND its
//     EffectiveFromUtc ≤ nowUtc AND (EffectiveUntilUtc is null OR > nowUtc),
//     the resolver returns those values with Source=Override.
//   - Otherwise returns the defaults with Source=Default.
//
// Caching: production implementation caches for 5 minutes via Redis so an
// override takes effect within the cache TTL without hammering Marten. In
// tests we use the in-memory store + no cache so behaviour is synchronous.
// =============================================================================

namespace Cena.Actors.Pricing;

/// <summary>
/// Resolves the effective pricing for an institute at a point in time.
/// </summary>
public interface IInstitutePricingResolver
{
    /// <summary>
    /// Return the pricing in effect for <paramref name="instituteId"/> at
    /// <c>DateTimeOffset.UtcNow</c>. Never throws on "no override" — that
    /// is the common case returning defaults. Throws only on malformed
    /// override documents (the projection layer upstream prevents that in
    /// normal flow).
    /// </summary>
    Task<ResolvedPricing> ResolveAsync(string instituteId, CancellationToken ct = default);
}

/// <summary>
/// Read-side store contract for override documents. Implementations back
/// this with Marten (production) or an in-memory dictionary (tests).
/// </summary>
public interface IInstitutePricingOverrideStore
{
    /// <summary>
    /// Returns the current override document for the institute, or null
    /// if none exists. Filtering on EffectiveFromUtc / EffectiveUntilUtc
    /// happens in the resolver, not here — callers get the raw projection.
    /// </summary>
    Task<InstitutePricingOverrideDocument?> FindAsync(
        string instituteId,
        CancellationToken ct = default);

    /// <summary>
    /// Idempotent upsert of the override document. Called from the admin
    /// endpoint after the <see cref="Events.InstitutePricingOverridden_V1"/>
    /// event has been appended to the stream.
    /// </summary>
    Task UpsertAsync(
        InstitutePricingOverrideDocument document,
        CancellationToken ct = default);
}
