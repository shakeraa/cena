// =============================================================================
// Cena Platform — Mashov Synthetic Probe (prr-039)
//
// Runs every 60s (task body says "every 5m" but SRE reality says 60s is
// the right cadence for a staleness-badge input — a 5-minute probe can
// miss the 5-minute staleness threshold entirely). Probes each tenant's
// Mashov endpoint with a lightweight read-only call (roster health ping,
// no data transfer), records the result through the circuit breaker, and
// emits the `cena_mashov_health` gauge.
//
// The probe does NOT attempt to recover a broken circuit by itself; the
// circuit breaker's half-open → single-test pattern handles that. The
// probe IS the half-open test when it fires during cooldown.
//
// Tenant enumeration: IMashovProbeTenantSource supplies the list of
// tenants with Mashov configured. Default implementation
// (NullMashovProbeTenantSource) returns empty — Launch posture where no
// tenants have Mashov wired yet. Production deployments register a
// Marten-backed or ITenantConfigurationService-backed implementation.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Integrations.Mashov;

public interface IMashovHealthClient
{
    /// <summary>
    /// Lightweight read-only ping against a tenant's Mashov endpoint.
    /// Throws on 5xx / timeout / connection error. Success = returns
    /// normally (return value is unused; it's the non-throw that counts).
    /// </summary>
    Task PingAsync(string tenantId, CancellationToken ct);
}

public interface IMashovProbeTenantSource
{
    /// <summary>Tenants with Mashov configured. Empty at Launch.</summary>
    IReadOnlyList<string> ConfiguredTenants();
}

/// <summary>
/// Default source — zero tenants. The production deployment injects a
/// Marten-backed or secret-store-backed impl when the first institute
/// onboards Mashov.
/// </summary>
public sealed class NullMashovProbeTenantSource : IMashovProbeTenantSource
{
    public IReadOnlyList<string> ConfiguredTenants() => Array.Empty<string>();
}

/// <summary>
/// Hosted service that runs the probe loop. Non-overlapping — each tick
/// waits for the previous iteration to complete before running again.
/// </summary>
public sealed class MashovSyncHealthProbe : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    private static readonly Meter s_meter = new("Cena.Mashov", "1.0");
    private static readonly Counter<long> s_probeCount = s_meter.CreateCounter<long>(
        "cena_mashov_probe_total",
        description: "Total synthetic probe executions per tenant, labeled by outcome (success|failure|circuit_open)");

    private readonly IMashovSyncCircuitBreaker _circuit;
    private readonly IMashovHealthClient _client;
    private readonly IMashovProbeTenantSource _tenants;
    private readonly ILogger<MashovSyncHealthProbe> _logger;
    private readonly TimeSpan _interval;

    public MashovSyncHealthProbe(
        IMashovSyncCircuitBreaker circuit,
        IMashovHealthClient client,
        IMashovProbeTenantSource tenants,
        ILogger<MashovSyncHealthProbe> logger,
        TimeSpan? interval = null)
    {
        _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = interval ?? DefaultInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[MASHOV_PROBE] starting, interval={Interval}s",
            (int)_interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            // One immediate tick at boot so metrics have initial values
            // without waiting for the first interval.
            await RunTickAsync(stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    // Internal for unit testing.
    internal async Task RunTickAsync(CancellationToken ct)
    {
        var tenants = _tenants.ConfiguredTenants();
        if (tenants.Count == 0) return;

        foreach (var tenantId in tenants)
        {
            if (ct.IsCancellationRequested) return;
            await ProbeOneAsync(tenantId, ct).ConfigureAwait(false);
        }
    }

    private async Task ProbeOneAsync(string tenantId, CancellationToken ct)
    {
        try
        {
            await _circuit.ExecuteAsync(
                tenantId,
                innerCt => ExecuteProbeOnClient(tenantId, innerCt),
                ct).ConfigureAwait(false);

            s_probeCount.Add(
                1,
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("outcome", "success"));
        }
        catch (MashovCircuitOpenException)
        {
            s_probeCount.Add(
                1,
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("outcome", "circuit_open"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutdown — not a probe failure
            throw;
        }
        catch (Exception ex)
        {
            s_probeCount.Add(
                1,
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("outcome", "failure"));
            _logger.LogWarning(
                ex,
                "[MASHOV_PROBE] tenant {TenantId} probe failed — {Reason}",
                tenantId, ex.Message);
        }
    }

    private async Task<bool> ExecuteProbeOnClient(string tenantId, CancellationToken ct)
    {
        await _client.PingAsync(tenantId, ct).ConfigureAwait(false);
        return true;
    }
}
