// =============================================================================
// Cena Platform — InstitutePricingResolver (prr-244)
//
// Default implementation of IInstitutePricingResolver. Looks up the
// institute's override document; if present + currently effective,
// returns those values with Source=Override. Otherwise returns the YAML
// defaults with Source=Default.
//
// Caching: uses the IPricingCache seam so tests can swap in a no-op
// cache. Production wires a Redis-backed 5-minute TTL cache; any cache
// miss falls back to a direct store lookup + YAML defaults.
//
// Clock: injected via Func for deterministic tests. Default is
// DateTimeOffset.UtcNow.
// =============================================================================

namespace Cena.Actors.Pricing;

/// <summary>
/// Cache seam for resolver lookups. Keyed by institute id.
/// </summary>
public interface IPricingCache
{
    /// <summary>Return cached value or null on miss.</summary>
    Task<ResolvedPricing?> GetAsync(string instituteId, CancellationToken ct = default);

    /// <summary>Store value with the configured TTL.</summary>
    Task SetAsync(string instituteId, ResolvedPricing value, CancellationToken ct = default);
}

/// <summary>No-op cache for tests + hosts that have not wired Redis.</summary>
public sealed class NullPricingCache : IPricingCache
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullPricingCache Instance = new();
    private NullPricingCache() { }

    /// <inheritdoc />
    public Task<ResolvedPricing?> GetAsync(string instituteId, CancellationToken ct = default)
        => Task.FromResult<ResolvedPricing?>(null);

    /// <inheritdoc />
    public Task SetAsync(string instituteId, ResolvedPricing value, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// Default resolver. Order of lookups:
///   1. Cache hit → return.
///   2. Override store lookup → if currently effective, cache + return override.
///   3. Fall back to YAML defaults → cache + return.
/// </summary>
public sealed class InstitutePricingResolver : IInstitutePricingResolver
{
    private readonly DefaultPricingYaml _defaults;
    private readonly IInstitutePricingOverrideStore _overrides;
    private readonly IPricingCache _cache;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Wire via DI.</summary>
    public InstitutePricingResolver(
        DefaultPricingYaml defaults,
        IInstitutePricingOverrideStore overrides,
        IPricingCache? cache = null,
        Func<DateTimeOffset>? clock = null)
    {
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _cache = cache ?? NullPricingCache.Instance;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<ResolvedPricing> ResolveAsync(string instituteId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteId);

        var cached = await _cache.GetAsync(instituteId, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var now = _clock();
        var doc = await _overrides.FindAsync(instituteId, ct).ConfigureAwait(false);
        ResolvedPricing resolved;
        if (doc is not null && IsEffective(doc, now))
        {
            resolved = new ResolvedPricing(
                StudentMonthlyPriceUsd: doc.StudentMonthlyPriceUsd,
                InstitutionalPerSeatPriceUsd: doc.InstitutionalPerSeatPriceUsd,
                MinSeatsForInstitutional: doc.MinSeatsForInstitutional,
                FreeTierSessionCap: doc.FreeTierSessionCap,
                Source: PricingSource.Override,
                EffectiveFromUtc: doc.EffectiveFromUtc);
        }
        else
        {
            resolved = _defaults.ToResolvedPricing();
        }

        await _cache.SetAsync(instituteId, resolved, ct).ConfigureAwait(false);
        return resolved;
    }

    private static bool IsEffective(InstitutePricingOverrideDocument doc, DateTimeOffset now)
    {
        if (doc.EffectiveFromUtc > now) return false;
        if (doc.EffectiveUntilUtc is { } until && until <= now) return false;
        return true;
    }
}

/// <summary>
/// In-memory override store for tests + phase-1 pre-Marten wiring.
/// Thread-safe via lock on a dictionary.
/// </summary>
public sealed class InMemoryInstitutePricingOverrideStore : IInstitutePricingOverrideStore
{
    private readonly Dictionary<string, InstitutePricingOverrideDocument> _map = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <inheritdoc />
    public Task<InstitutePricingOverrideDocument?> FindAsync(
        string instituteId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteId);
        lock (_gate)
        {
            _map.TryGetValue(instituteId, out var doc);
            return Task.FromResult<InstitutePricingOverrideDocument?>(doc);
        }
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        InstitutePricingOverrideDocument document,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.InstituteId);
        lock (_gate)
        {
            _map[document.InstituteId] = document;
        }
        return Task.CompletedTask;
    }
}
