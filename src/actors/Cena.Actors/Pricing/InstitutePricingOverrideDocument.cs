// =============================================================================
// Cena Platform — InstitutePricingOverrideDocument (prr-244)
//
// Marten document holding the current per-institute pricing override, if
// any. One doc per institute (Id = "pricing-override-{instituteId}"). The
// document is a PROJECTION of the append-only event stream — writes emit
// an InstitutePricingOverridden_V1, and the projection applies the latest
// event to produce the current row. Reads go through
// IInstitutePricingResolver which caches the projection in Redis 5min.
//
// Tenancy: the document is tenant-scoped via InstituteId. ADR-0001 holds —
// an institute admin never sees another institute's override (the
// resolver scopes by InstituteId on every lookup).
// =============================================================================

namespace Cena.Actors.Pricing;

/// <summary>
/// One row per institute with an active pricing override. Absence of a
/// row means "no override — resolver returns YAML defaults".
/// </summary>
public sealed class InstitutePricingOverrideDocument
{
    /// <summary>Marten document id. Format: <c>pricing-override-{instituteId}</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Domain identity. Equal to <c>InstituteId</c> portion of <see cref="Id"/>.</summary>
    public string InstituteId { get; set; } = "";

    /// <summary>Overridden per-student monthly price (USD).</summary>
    public decimal StudentMonthlyPriceUsd { get; set; }

    /// <summary>Overridden per-seat institutional price (USD).</summary>
    public decimal InstitutionalPerSeatPriceUsd { get; set; }

    /// <summary>Overridden seat-count breakpoint.</summary>
    public int MinSeatsForInstitutional { get; set; }

    /// <summary>Overridden free-tier monthly session cap.</summary>
    public int FreeTierSessionCap { get; set; }

    /// <summary>When this override started applying.</summary>
    public DateTimeOffset EffectiveFromUtc { get; set; }

    /// <summary>
    /// Optional explicit end. Null = open-ended (until replaced). When
    /// set and in the past, the resolver ignores this override and falls
    /// back to defaults.
    /// </summary>
    public DateTimeOffset? EffectiveUntilUtc { get; set; }

    /// <summary>Super-admin user id who applied the override. Never elided — audit trail.</summary>
    public string OverriddenBySuperAdminId { get; set; } = "";

    /// <summary>
    /// Business-sensitive justification text (required, ≥20 chars per the
    /// admin UI form). Visible only to SUPER_ADMIN + finance roles per
    /// PRR-244 privacy non-negotiable.
    /// </summary>
    public string JustificationText { get; set; } = "";

    /// <summary>When the document was first created (write timestamp).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Derive the canonical Marten id from an institute id.
    /// </summary>
    public static string IdFor(string instituteId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteId);
        return $"pricing-override-{instituteId}";
    }
}
