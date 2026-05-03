// =============================================================================
// Cena Platform — SymPy Sidecar Client (CAS-001, Tier 2)
//
// Calls the SymPy sidecar container via NATS request/reply.
// Handles: calculus, trig identities, equation solving, ODE, complex
// simplification. Circuit breaker with MathNet fallback.
//
// NATS subjects:
//   cena.cas.verify.sympy — all operations
//   cena.cas.health.sympy — health check
// =============================================================================

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Cas;

public interface ISymPySidecarClient
{
    Task<CasVerifyResult> VerifyAsync(CasVerifyRequest request, CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

/// <summary>
/// Tier 2 CAS client that sends verification requests to the SymPy sidecar via NATS.
/// Includes circuit breaker: after N consecutive failures, falls back to MathNet.
///
/// prr-010 hardening (2026-04-20): every request is screened by an
/// <see cref="ISymPyTemplateGuard"/> before it is marshalled to NATS.
/// Rejected requests never touch the sidecar process — the banned-token list
/// catches dunder chains, SSRF via <c>printing.preview</c>, and injected
/// <c>import</c>/<c>exec</c> payloads. The Python sidecar re-applies a
/// matching whitelist as defense-in-depth (see
/// <c>docker/sympy-sidecar/sympy_worker.py</c>).
/// </summary>
public sealed class SymPySidecarClient : ISymPySidecarClient
{
    private readonly INatsConnection _nats;
    private readonly ISymPyTemplateGuard _guard;
    private readonly ILogger<SymPySidecarClient> _logger;
    private const string VerifySubject = "cena.cas.verify.sympy";
    private const string HealthSubject = "cena.cas.health.sympy";
    private const string EngineName = "SymPy";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // Circuit breaker state
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenUntil = DateTimeOffset.MinValue;
    private const int FailureThreshold = 3;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SymPySidecarClient(
        INatsConnection nats,
        ILogger<SymPySidecarClient> logger,
        ISymPyTemplateGuard? guard = null)
    {
        _nats = nats;
        _logger = logger;
        _guard = guard ?? new SymPyTemplateGuard();
    }

    public async Task<CasVerifyResult> VerifyAsync(CasVerifyRequest request, CancellationToken ct = default)
    {
        // prr-010: Parse-side guard runs BEFORE NATS marshalling. A rejected
        // template never reaches the sidecar — this is the first wall of the
        // sandbox. The sidecar re-applies a matching whitelist as defense-
        // in-depth, but we short-circuit here for fail-fast behaviour and to
        // pin a test-friendly seam (SymPySandbox.CanarySuiteTests).
        var guard = _guard.Screen(request);
        if (!guard.Allowed)
        {
            _logger.LogWarning(
                "SymPy template rejected at guard (pre-sidecar): {Reason} [token={Token}]",
                guard.Reason, guard.BannedToken ?? "n/a");
            return CasVerifyResult.Error(
                request.Operation, EngineName, 0,
                $"SymPy template rejected: {guard.Reason}",
                CasVerifyStatus.Error);
        }

        // Circuit breaker: if open, fail fast
        if (IsCircuitOpen())
        {
            _logger.LogWarning("SymPy circuit breaker is open, failing fast");
            return CasVerifyResult.Error(request.Operation, EngineName, 0,
                "Circuit breaker open — SymPy sidecar unavailable",
                CasVerifyStatus.CircuitBreakerOpen);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOpts);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            var reply = await _nats.RequestAsync<byte[], byte[]>(
                VerifySubject, payload, cancellationToken: cts.Token);

            if (reply.Data is null || reply.Data.Length == 0)
            {
                RecordFailure();
                return CasVerifyResult.Error(request.Operation, EngineName,
                    sw.Elapsed.TotalMilliseconds, "Empty response from SymPy sidecar");
            }

            var result = JsonSerializer.Deserialize<SymPyResponse>(reply.Data, JsonOpts);
            RecordSuccess();

            if (result is null)
                return CasVerifyResult.Error(request.Operation, EngineName,
                    sw.Elapsed.TotalMilliseconds, "Failed to deserialize SymPy response");

            return result.Success
                ? CasVerifyResult.Success(request.Operation, EngineName,
                    sw.Elapsed.TotalMilliseconds, result.SimplifiedA, result.SimplifiedB)
                : CasVerifyResult.Failure(request.Operation, EngineName,
                    sw.Elapsed.TotalMilliseconds, result.Error ?? "Verification failed");
        }
        catch (OperationCanceledException)
        {
            RecordFailure();
            _logger.LogWarning("SymPy request timed out after {Timeout}ms", RequestTimeout.TotalMilliseconds);
            return CasVerifyResult.Error(request.Operation, EngineName,
                sw.Elapsed.TotalMilliseconds, "SymPy sidecar timeout",
                CasVerifyStatus.Timeout);
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogError(ex, "SymPy sidecar communication error");
            return CasVerifyResult.Error(request.Operation, EngineName,
                sw.Elapsed.TotalMilliseconds, $"Communication error: {ex.Message}");
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var reply = await _nats.RequestAsync<byte[], byte[]>(
                HealthSubject, "ping"u8.ToArray(), cancellationToken: cts.Token);

            return reply.Data is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool IsCircuitOpen() =>
        _consecutiveFailures >= FailureThreshold
        && DateTimeOffset.UtcNow < _circuitOpenUntil;

    private void RecordFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        if (failures >= FailureThreshold)
        {
            _circuitOpenUntil = DateTimeOffset.UtcNow + CircuitOpenDuration;
            _logger.LogWarning("SymPy circuit breaker opened for {Duration}s after {Failures} failures",
                CircuitOpenDuration.TotalSeconds, failures);
        }
    }

    private void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    private record SymPyResponse(
        bool Success,
        string? SimplifiedA,
        string? SimplifiedB,
        string? Error
    );
}
