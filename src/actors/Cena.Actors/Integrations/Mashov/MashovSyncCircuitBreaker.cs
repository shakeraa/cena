// =============================================================================
// Cena Platform — Mashov Sync Circuit Breaker (prr-039)
//
// Wraps every outbound Mashov call (roster, calendar, future grade-passback)
// with a per-tenant circuit breaker:
//
//   closed → open on 5 consecutive failures within 2 minutes
//   open → half-open after 30s cooldown
//   half-open → closed on one success, back to open on failure
//
// State is in-memory, singleton-scoped, and keyed by tenant. Mashov
// credentials are per-tenant (each school has its own Mashov SIS
// account) so the circuit must be per-tenant too — one school's outage
// must not cascade into healthy schools.
//
// Emits:
//   - MashovSyncCircuitBreakerOpened log event id 8100
//   - MashovSyncCircuitBreakerClosed log event id 8101
//   - MashovSyncCircuitBreakerHalfOpen log event id 8102
//   - `cena_mashov_circuit_state` OTel gauge (0=closed, 1=half-open, 2=open)
//   - `cena_mashov_last_successful_sync_seconds_ago` OTel gauge per tenant
//
// The last-successful-sync timestamp is the data point the UI staleness
// badge reads — when > 5 minutes old, the student UI surfaces the
// "Mashov data may be outdated" banner. See MashovSyncHealthProbe for
// the synthetic probe that refreshes it.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Integrations.Mashov;

public enum MashovCircuitState
{
    Closed = 0,
    HalfOpen = 1,
    Open = 2,
}

public sealed record MashovCircuitStatus(
    string TenantId,
    MashovCircuitState State,
    int ConsecutiveFailures,
    DateTimeOffset? LastOpenedAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc);

public interface IMashovSyncCircuitBreaker
{
    MashovCircuitStatus Status(string tenantId);

    IReadOnlyList<MashovCircuitStatus> All();

    /// <summary>
    /// Execute an outbound Mashov call under the circuit. Throws
    /// <see cref="MashovCircuitOpenException"/> immediately when the
    /// circuit is open.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        string tenantId,
        Func<CancellationToken, Task<T>> call,
        CancellationToken ct = default);

    /// <summary>Test hook: manually record a success (used by probes).</summary>
    void RecordSuccess(string tenantId);

    /// <summary>Test hook: manually record a failure (used by probes).</summary>
    void RecordFailure(string tenantId, string reason);
}

public sealed class MashovCircuitOpenException : Exception
{
    public string TenantId { get; }
    public DateTimeOffset OpenedAtUtc { get; }

    public MashovCircuitOpenException(string tenantId, DateTimeOffset openedAtUtc)
        : base($"Mashov circuit breaker is OPEN for tenant '{tenantId}' (opened {openedAtUtc:O})")
    {
        TenantId = tenantId;
        OpenedAtUtc = openedAtUtc;
    }
}

public sealed class MashovSyncCircuitBreaker : IMashovSyncCircuitBreaker
{
    public const int FailureThreshold = 5;
    public static readonly TimeSpan SamplingWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan CooldownDuration = TimeSpan.FromSeconds(30);

    private static readonly Meter s_meter = new("Cena.Mashov", "1.0");

    private readonly Dictionary<string, TenantCircuit> _circuits = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly ILogger<MashovSyncCircuitBreaker> _logger;
    private readonly TimeProvider _time;

    public MashovSyncCircuitBreaker(
        ILogger<MashovSyncCircuitBreaker> logger,
        TimeProvider? time = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;

        s_meter.CreateObservableGauge(
            "cena_mashov_circuit_state",
            ObserveCircuitStates,
            description: "Mashov circuit breaker state per tenant: 0=closed, 1=half-open, 2=open");

        s_meter.CreateObservableGauge(
            "cena_mashov_last_successful_sync_seconds_ago",
            ObserveStaleness,
            description: "Seconds since last successful Mashov sync per tenant (negative = never synced)");
    }

    public MashovCircuitStatus Status(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required", nameof(tenantId));
        lock (_sync)
        {
            var c = GetOrCreate(tenantId);
            return Snapshot(c);
        }
    }

    public IReadOnlyList<MashovCircuitStatus> All()
    {
        lock (_sync)
        {
            return _circuits.Values.Select(Snapshot).ToArray();
        }
    }

    public async Task<T> ExecuteAsync<T>(
        string tenantId,
        Func<CancellationToken, Task<T>> call,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required", nameof(tenantId));
        if (call is null) throw new ArgumentNullException(nameof(call));

        // Pre-call gate: open → throw without dialing.
        TenantCircuit circuit;
        lock (_sync) { circuit = GetOrCreate(tenantId); }
        CheckGate(circuit);

        try
        {
            var result = await call(ct).ConfigureAwait(false);
            RecordSuccessInternal(circuit);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFailureInternal(circuit, ex.Message);
            throw;
        }
    }

    public void RecordSuccess(string tenantId)
    {
        TenantCircuit circuit;
        lock (_sync) { circuit = GetOrCreate(tenantId); }
        RecordSuccessInternal(circuit);
    }

    public void RecordFailure(string tenantId, string reason)
    {
        TenantCircuit circuit;
        lock (_sync) { circuit = GetOrCreate(tenantId); }
        RecordFailureInternal(circuit, reason);
    }

    private void CheckGate(TenantCircuit circuit)
    {
        lock (_sync)
        {
            MaybeTransitionToHalfOpen(circuit);
            if (circuit.State == MashovCircuitState.Open)
                throw new MashovCircuitOpenException(
                    circuit.TenantId, circuit.LastOpenedAtUtc ?? _time.GetUtcNow());
        }
    }

    private void RecordSuccessInternal(TenantCircuit circuit)
    {
        lock (_sync)
        {
            var wasOpen = circuit.State != MashovCircuitState.Closed;
            circuit.ConsecutiveFailures = 0;
            circuit.FailuresInWindow.Clear();
            circuit.LastSuccessfulSyncAtUtc = _time.GetUtcNow();
            if (wasOpen)
            {
                circuit.State = MashovCircuitState.Closed;
                circuit.LastOpenedAtUtc = null;
                _logger.LogInformation(
                    "[MASHOV_CB] circuit CLOSED for tenant {TenantId} " +
                    "(EventId=8101). Requests flowing normally.",
                    circuit.TenantId);
            }
        }
    }

    private void RecordFailureInternal(TenantCircuit circuit, string reason)
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            circuit.ConsecutiveFailures++;
            circuit.FailuresInWindow.Add(now);
            PruneWindow(circuit, now);

            if (circuit.State == MashovCircuitState.HalfOpen)
            {
                // A failure during half-open → reopen immediately.
                OpenCircuit(circuit, now, reason);
                return;
            }

            if (circuit.FailuresInWindow.Count >= FailureThreshold
                && circuit.State == MashovCircuitState.Closed)
            {
                OpenCircuit(circuit, now, reason);
            }
        }
    }

    private void OpenCircuit(TenantCircuit circuit, DateTimeOffset now, string reason)
    {
        circuit.State = MashovCircuitState.Open;
        circuit.LastOpenedAtUtc = now;
        _logger.LogError(
            "[MASHOV_CB] circuit OPENED for tenant {TenantId} after {Failures} " +
            "failures in {Window}s. Cooldown={Cooldown}s. Reason={Reason}. (EventId=8100)",
            circuit.TenantId,
            circuit.FailuresInWindow.Count,
            (int)SamplingWindow.TotalSeconds,
            (int)CooldownDuration.TotalSeconds,
            reason);
    }

    private void MaybeTransitionToHalfOpen(TenantCircuit circuit)
    {
        if (circuit.State != MashovCircuitState.Open) return;
        if (circuit.LastOpenedAtUtc is null) return;
        var now = _time.GetUtcNow();
        if (now - circuit.LastOpenedAtUtc.Value < CooldownDuration) return;

        circuit.State = MashovCircuitState.HalfOpen;
        _logger.LogInformation(
            "[MASHOV_CB] circuit HALF-OPEN for tenant {TenantId}. " +
            "Testing next request. (EventId=8102)",
            circuit.TenantId);
    }

    private static void PruneWindow(TenantCircuit circuit, DateTimeOffset now)
    {
        var cutoff = now - SamplingWindow;
        circuit.FailuresInWindow.RemoveAll(t => t < cutoff);
    }

    private TenantCircuit GetOrCreate(string tenantId)
    {
        if (!_circuits.TryGetValue(tenantId, out var c))
        {
            c = new TenantCircuit(tenantId);
            _circuits[tenantId] = c;
        }
        return c;
    }

    private MashovCircuitStatus Snapshot(TenantCircuit c) => new(
        c.TenantId,
        c.State,
        c.ConsecutiveFailures,
        c.LastOpenedAtUtc,
        c.LastSuccessfulSyncAtUtc);

    private IEnumerable<Measurement<int>> ObserveCircuitStates()
    {
        lock (_sync)
        {
            foreach (var c in _circuits.Values)
            {
                yield return new Measurement<int>(
                    (int)c.State,
                    new KeyValuePair<string, object?>("tenant_id", c.TenantId));
            }
        }
    }

    private IEnumerable<Measurement<double>> ObserveStaleness()
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            foreach (var c in _circuits.Values)
            {
                var seconds = c.LastSuccessfulSyncAtUtc is null
                    ? -1.0
                    : (now - c.LastSuccessfulSyncAtUtc.Value).TotalSeconds;
                yield return new Measurement<double>(
                    seconds,
                    new KeyValuePair<string, object?>("tenant_id", c.TenantId));
            }
        }
    }

    // Internal per-tenant circuit state. Never leaked across the API boundary.
    private sealed class TenantCircuit
    {
        public string TenantId { get; }
        public MashovCircuitState State { get; set; } = MashovCircuitState.Closed;
        public int ConsecutiveFailures { get; set; }
        public List<DateTimeOffset> FailuresInWindow { get; } = new();
        public DateTimeOffset? LastOpenedAtUtc { get; set; }
        public DateTimeOffset? LastSuccessfulSyncAtUtc { get; set; }

        public TenantCircuit(string tenantId) { TenantId = tenantId; }
    }
}
