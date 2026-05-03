// =============================================================================
// Cena Platform — DefaultPricingYaml loader (prr-244)
//
// Typed view of `contracts/pricing/default-pricing.yml`. Loaded once at
// service start via YamlDotNet. The loader DOES validate bounds — a
// malformed YAML file fails fast on load so a bad pricing change can't
// silently corrupt billing. Every bound violation throws with the offending
// field name + value in the message.
//
// No runtime mutation — the defaults object is immutable after load.
// Overrides do not mutate the YAML; they're applied separately via the
// resolver's override-lookup path.
// =============================================================================

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cena.Actors.Pricing;

/// <summary>
/// Immutable typed view of the default-pricing YAML file.
/// </summary>
public sealed class DefaultPricingYaml
{
    /// <summary>YAML schema version. Enforced to be <c>1</c>.</summary>
    public int Version { get; init; }

    /// <summary>When these defaults started applying.</summary>
    public DateTimeOffset EffectiveFromUtc { get; init; }

    /// <summary>Provenance tag for logs + reporting.</summary>
    public string Source { get; init; } = "cena-saas-global-default";

    /// <summary>Student monthly price in USD.</summary>
    public decimal StudentMonthlyPriceUsd { get; init; }

    /// <summary>Per-seat institutional price in USD.</summary>
    public decimal InstitutionalPerSeatPriceUsd { get; init; }

    /// <summary>Seat breakpoint for institutional pricing.</summary>
    public int MinSeatsForInstitutional { get; init; }

    /// <summary>Free-tier monthly session cap.</summary>
    public int FreeTierSessionCap { get; init; }

    /// <summary>Validation bounds (enforced on load + on override writes).</summary>
    public PricingBounds Bounds { get; init; } = new();

    /// <summary>
    /// Load + validate from the given file path. Throws
    /// <see cref="InvalidOperationException"/> on bounds violation, and
    /// <see cref="FileNotFoundException"/> if the path doesn't exist.
    /// </summary>
    public static DefaultPricingYaml LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Pricing defaults file not found: {path}", path);
        }
        var text = File.ReadAllText(path);
        return LoadFromYaml(text);
    }

    /// <summary>
    /// Parse + validate a YAML string.
    /// </summary>
    public static DefaultPricingYaml LoadFromYaml(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var parsed = deserializer.Deserialize<DefaultPricingYaml>(yaml)
            ?? throw new InvalidOperationException("Pricing YAML deserialised to null.");

        if (parsed.Version != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported pricing YAML version: {parsed.Version}. Expected 1.");
        }

        parsed.AssertValid();
        return parsed;
    }

    /// <summary>
    /// Throw if any value is outside the declared bounds. Also enforces
    /// the cross-field invariant that the institutional per-seat price
    /// cannot exceed the student monthly price (institutional is always
    /// a discount tier).
    /// </summary>
    public void AssertValid()
    {
        if (StudentMonthlyPriceUsd < Bounds.StudentMonthlyPriceUsdMin
            || StudentMonthlyPriceUsd > Bounds.StudentMonthlyPriceUsdMax)
        {
            throw new InvalidOperationException(
                $"student_monthly_price_usd={StudentMonthlyPriceUsd} outside bounds "
                + $"[{Bounds.StudentMonthlyPriceUsdMin}, {Bounds.StudentMonthlyPriceUsdMax}]");
        }
        if (InstitutionalPerSeatPriceUsd < Bounds.InstitutionalPerSeatPriceUsdMin
            || InstitutionalPerSeatPriceUsd > Bounds.InstitutionalPerSeatPriceUsdMax)
        {
            throw new InvalidOperationException(
                $"institutional_per_seat_price_usd={InstitutionalPerSeatPriceUsd} outside bounds "
                + $"[{Bounds.InstitutionalPerSeatPriceUsdMin}, {Bounds.InstitutionalPerSeatPriceUsdMax}]");
        }
        if (MinSeatsForInstitutional < Bounds.MinSeatsForInstitutionalMin
            || MinSeatsForInstitutional > Bounds.MinSeatsForInstitutionalMax)
        {
            throw new InvalidOperationException(
                $"min_seats_for_institutional={MinSeatsForInstitutional} outside bounds "
                + $"[{Bounds.MinSeatsForInstitutionalMin}, {Bounds.MinSeatsForInstitutionalMax}]");
        }
        if (FreeTierSessionCap < Bounds.FreeTierSessionCapMin
            || FreeTierSessionCap > Bounds.FreeTierSessionCapMax)
        {
            throw new InvalidOperationException(
                $"free_tier_session_cap={FreeTierSessionCap} outside bounds "
                + $"[{Bounds.FreeTierSessionCapMin}, {Bounds.FreeTierSessionCapMax}]");
        }
        if (InstitutionalPerSeatPriceUsd > StudentMonthlyPriceUsd)
        {
            throw new InvalidOperationException(
                "institutional_per_seat_price_usd must not exceed student_monthly_price_usd "
                + $"({InstitutionalPerSeatPriceUsd} > {StudentMonthlyPriceUsd}). "
                + "Institutional pricing is always a discount tier.");
        }
    }

    /// <summary>Project into a <see cref="ResolvedPricing"/> carrying the Default source tag.</summary>
    public ResolvedPricing ToResolvedPricing() => new(
        StudentMonthlyPriceUsd: StudentMonthlyPriceUsd,
        InstitutionalPerSeatPriceUsd: InstitutionalPerSeatPriceUsd,
        MinSeatsForInstitutional: MinSeatsForInstitutional,
        FreeTierSessionCap: FreeTierSessionCap,
        Source: PricingSource.Default,
        EffectiveFromUtc: EffectiveFromUtc);
}

/// <summary>
/// Validation bounds loaded from the YAML <c>bounds:</c> block. Enforced
/// on both the defaults themselves (at load) and every override write
/// (in the admin endpoint).
/// </summary>
public sealed class PricingBounds
{
    public decimal StudentMonthlyPriceUsdMin { get; init; } = 3.30m;
    public decimal StudentMonthlyPriceUsdMax { get; init; } = 99.00m;
    public decimal InstitutionalPerSeatPriceUsdMin { get; init; } = 2.31m;
    public decimal InstitutionalPerSeatPriceUsdMax { get; init; } = 99.00m;
    public int MinSeatsForInstitutionalMin { get; init; } = 1;
    public int MinSeatsForInstitutionalMax { get; init; } = 1000;
    public int FreeTierSessionCapMin { get; init; } = 0;
    public int FreeTierSessionCapMax { get; init; } = 500;
}
