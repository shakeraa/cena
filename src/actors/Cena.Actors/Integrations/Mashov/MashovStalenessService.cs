// =============================================================================
// Cena Platform — Mashov staleness service (prr-039)
//
// Thin read-side over the circuit breaker's LastSuccessfulSyncAtUtc
// field. Returns a client-shaped DTO the student SPA consumes to render
// the "Mashov data may be outdated" banner.
//
// Staleness threshold: 5 minutes. Below threshold → banner hidden.
// Above threshold → banner visible with the rounded "X minutes ago"
// text (no countdown, no pressure — ADR-0048 framing rules).
//
// Tenant scoping: endpoint callers pass their tenant context (from the
// authenticated principal's EnrollmentId claim); this service is a pure
// query over the circuit breaker's per-tenant state and never returns
// data for a tenant the caller does not own.
// =============================================================================

namespace Cena.Actors.Integrations.Mashov;

public interface IMashovStalenessService
{
    /// <summary>
    /// Staleness snapshot for a single tenant. Never null — unknown
    /// tenants return an IsStale=false, LastSuccessfulSyncAtUtc=null,
    /// IsConfigured=false dto.
    /// </summary>
    MashovStalenessDto ForTenant(string tenantId);
}

public sealed record MashovStalenessDto(
    string TenantId,
    bool IsConfigured,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    bool IsStale,
    int? MinutesSinceLastSync,
    string CircuitState);

public sealed class MashovStalenessService : IMashovStalenessService
{
    public static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(5);

    private readonly IMashovSyncCircuitBreaker _circuit;
    private readonly IMashovProbeTenantSource _tenants;
    private readonly TimeProvider _time;

    public MashovStalenessService(
        IMashovSyncCircuitBreaker circuit,
        IMashovProbeTenantSource tenants,
        TimeProvider? time = null)
    {
        _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _time = time ?? TimeProvider.System;
    }

    public MashovStalenessDto ForTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required", nameof(tenantId));

        var configured = _tenants.ConfiguredTenants()
            .Any(t => string.Equals(t, tenantId, StringComparison.Ordinal));

        if (!configured)
        {
            // Unknown / un-configured tenants: no banner, no data.
            return new MashovStalenessDto(
                TenantId: tenantId,
                IsConfigured: false,
                LastSuccessfulSyncAtUtc: null,
                IsStale: false,
                MinutesSinceLastSync: null,
                CircuitState: MashovCircuitState.Closed.ToString().ToLowerInvariant());
        }

        var status = _circuit.Status(tenantId);
        var now = _time.GetUtcNow();
        var age = status.LastSuccessfulSyncAtUtc is null
            ? (TimeSpan?)null
            : now - status.LastSuccessfulSyncAtUtc.Value;

        var isStale = age is { } a && a > StalenessThreshold;
        // If we have never synced successfully AND the tenant is configured,
        // staleness is true — the banner warns the user even before the
        // first success, because the SIS data is by definition absent.
        if (status.LastSuccessfulSyncAtUtc is null) isStale = true;

        return new MashovStalenessDto(
            TenantId: tenantId,
            IsConfigured: true,
            LastSuccessfulSyncAtUtc: status.LastSuccessfulSyncAtUtc,
            IsStale: isStale,
            MinutesSinceLastSync: age is { } span ? (int)span.TotalMinutes : null,
            CircuitState: status.State.ToString().ToLowerInvariant());
    }
}
