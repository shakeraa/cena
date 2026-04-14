// =============================================================================
// Cena Platform — CAS Router Service (CAS-001)
//
// Routes verification requests to the correct CAS tier:
//   1. Simple arithmetic/algebra → MathNet (in-process, <1ms)
//   2. Calculus/trig/ODE → SymPy sidecar (NATS, ~50ms)
//   3. SymPy unavailable → MathNet fallback (if capable)
//
// Every verification call is logged as a CAS audit event.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Actors.RateLimit;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

public interface ICasRouterService
{
    /// <summary>
    /// Route a CAS verification request to the appropriate engine.
    /// </summary>
    Task<CasVerifyResult> VerifyAsync(CasVerifyRequest request, CancellationToken ct = default);
}

/// <summary>
/// 3-tier CAS router: MathNet → SymPy → MathNet fallback.
/// </summary>
public sealed class CasRouterService : ICasRouterService
{
    private readonly IMathNetVerifier _mathNet;
    private readonly ISymPySidecarClient _symPy;
    private readonly ICostCircuitBreaker _costBreaker;
    private readonly ILogger<CasRouterService> _logger;

    // Metrics (OpenTelemetry)
    private static readonly Meter Meter = new("Cena.Cas", "1.0");
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "cena.cas.requests.total", description: "Total CAS verification requests");
    private static readonly Counter<long> FallbackTotal = Meter.CreateCounter<long>(
        "cena.cas.fallback.total", description: "MathNet fallback activations");
    private static readonly Histogram<double> LatencyMs = Meter.CreateHistogram<double>(
        "cena.cas.latency.ms", description: "CAS verification latency");

    public CasRouterService(
        IMathNetVerifier mathNet,
        ISymPySidecarClient symPy,
        ICostCircuitBreaker costBreaker,
        ILogger<CasRouterService> logger)
    {
        _mathNet = mathNet;
        _symPy = symPy;
        _costBreaker = costBreaker;
        _logger = logger;
    }

    public async Task<CasVerifyResult> VerifyAsync(CasVerifyRequest request, CancellationToken ct = default)
    {
        if (await _costBreaker.IsOpenAsync(ct))
        {
            _logger.LogWarning("CAS verification blocked — global cost circuit breaker is open");
            return CasVerifyResult.Error(request.Operation, "circuit-breaker", 0,
                "CAS verification temporarily unavailable due to cost limits. Please try again later.",
                CasVerifyStatus.CircuitBreakerOpen);
        }

        var sw = Stopwatch.StartNew();

        // Tier 1: Try MathNet first (in-process, fast)
        if (_mathNet.CanHandle(request))
        {
            var mathNetResult = _mathNet.Verify(request);
            RecordMetrics(mathNetResult, sw);

            // If MathNet succeeded or gave a definitive answer, return it
            if (mathNetResult.Status == CasVerifyStatus.Ok)
            {
                _logger.LogDebug("CAS Tier 1 (MathNet): {Operation} → {Verified} in {Ms:F1}ms",
                    request.Operation, mathNetResult.Verified, mathNetResult.LatencyMs);
                return mathNetResult;
            }
        }

        // Tier 2: Route to SymPy sidecar
        var symPyResult = await _symPy.VerifyAsync(request, ct);
        RecordMetrics(symPyResult, sw);

        if (symPyResult.Status == CasVerifyStatus.Ok)
        {
            _logger.LogDebug("CAS Tier 2 (SymPy): {Operation} → {Verified} in {Ms:F1}ms",
                request.Operation, symPyResult.Verified, symPyResult.LatencyMs);
            return symPyResult;
        }

        // Tier 3: Fallback to MathNet if SymPy is down
        if (_mathNet.CanHandle(request))
        {
            FallbackTotal.Add(1, new KeyValuePair<string, object?>("operation", request.Operation.ToString()));
            _logger.LogWarning("CAS fallback: SymPy unavailable, using MathNet for {Operation}", request.Operation);

            var fallbackResult = _mathNet.Verify(request);
            RecordMetrics(fallbackResult, sw);
            return fallbackResult;
        }

        // No engine can handle this request
        _logger.LogError("CAS: no engine available for {Operation} on '{ExprA}'",
            request.Operation, request.ExpressionA);
        return CasVerifyResult.Error(request.Operation, "none",
            sw.Elapsed.TotalMilliseconds, "No CAS engine available for this operation");
    }

    private static void RecordMetrics(CasVerifyResult result, Stopwatch sw)
    {
        var tags = new TagList
        {
            { "operation", result.Operation.ToString() },
            { "engine", result.Engine },
            { "verified", result.Verified.ToString() }
        };
        RequestsTotal.Add(1, tags);
        LatencyMs.Record(result.LatencyMs, tags);
    }
}
