// =============================================================================
// Cena Platform — CAS Conformance Suite Runner (RDY-036 §7 / RDY-037)
//
// Executes the 500-pair (MathNet ↔ SymPy) golden corpus defined in
// CasConformanceSuite.cs and computes agreement rate. Calls each engine
// directly (IMathNetVerifier + ISymPySidecarClient) — that's the point of
// the suite, measuring cross-engine agreement, not router behaviour.
//
// Exit gate:
//   - ≥ 99% agreement → pass (ADR-0032 §7 threshold)
//   - <  99%          → fail; baseline must be re-measured + doc updated
//
// Placeholder pairs (category == "placeholder") are filtered out — they
// ship in CasConformanceSuite.cs as a 500-slot skeleton that is still
// being populated from the Bagrut corpus. Running them through CAS
// produces meaningless results.
// =============================================================================

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

/// <summary>
/// RDY-036 §7: runs the conformance corpus and computes aggregate metrics.
/// </summary>
public interface ICasConformanceSuiteRunner
{
    /// <summary>
    /// Run every non-placeholder pair through both engines and compute
    /// agreement.
    /// </summary>
    Task<ConformanceSuiteResult> RunAsync(CancellationToken ct = default);

    /// <summary>
    /// Run a subset of pairs filtered by category (e.g. "trig"). Useful
    /// for iterative debugging. Placeholder pairs are still excluded.
    /// </summary>
    Task<ConformanceSuiteResult> RunCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// RDY-043: Run each pair through <see cref="ICasRouterService.VerifyAsync"/>
    /// (instead of hitting MathNet + SymPy directly). Measures router-level
    /// correctness — fallback ordering, circuit-breaker interaction, cost
    /// breaker — which the engine-direct mode bypasses. Nightly publishes
    /// both numbers; ADR-0032 §Enforcement names which one is the CI gate.
    /// </summary>
    Task<RouterConformanceSuiteResult> RunThroughRouterAsync(CancellationToken ct = default);
}

/// <summary>
/// RDY-043: Aggregate for the router-mode run. The router returns a single
/// verified/not-verified verdict per pair, so we compare against the
/// <c>ExpectedEquivalent</c> label.
/// </summary>
public sealed record RouterConformanceSuiteResult
{
    public int TotalPairs { get; init; }
    public int CorrectCount { get; init; }
    public double PassRate => TotalPairs > 0 ? (double)CorrectCount / TotalPairs : 0;
    public bool PassesThreshold => PassRate >= 0.99;
    public IReadOnlyList<RouterConformanceResult> Mismatches { get; init; } =
        Array.Empty<RouterConformanceResult>();
    public TimeSpan TotalDuration { get; init; }
    public DateTimeOffset RunAt { get; init; }
}

public sealed record RouterConformanceResult
{
    public int PairId { get; init; }
    public bool RouterVerified { get; init; }
    public bool ExpectedEquivalent { get; init; }
    public bool Correct => RouterVerified == ExpectedEquivalent;
    public string? EngineUsed { get; init; }
    public string? Error { get; init; }
    public TimeSpan Latency { get; init; }
}

/// <inheritdoc />
public sealed class CasConformanceSuiteRunner : ICasConformanceSuiteRunner
{
    private readonly IMathNetVerifier _mathNet;
    private readonly ISymPySidecarClient _sympy;
    private readonly ICasRouterService? _router;
    private readonly ILogger<CasConformanceSuiteRunner> _logger;

    public CasConformanceSuiteRunner(
        IMathNetVerifier mathNet,
        ISymPySidecarClient sympy,
        ILogger<CasConformanceSuiteRunner> logger,
        ICasRouterService? router = null)
    {
        _mathNet = mathNet;
        _sympy = sympy;
        _logger = logger;
        _router = router;
    }

    public Task<ConformanceSuiteResult> RunAsync(CancellationToken ct = default) =>
        RunCoreAsync(pair => pair.Category != "placeholder", ct);

    public Task<ConformanceSuiteResult> RunCategoryAsync(string category, CancellationToken ct = default) =>
        RunCoreAsync(pair => pair.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
                             && pair.Category != "placeholder", ct);

    /// <inheritdoc />
    public async Task<RouterConformanceSuiteResult> RunThroughRouterAsync(CancellationToken ct = default)
    {
        if (_router is null)
        {
            throw new InvalidOperationException(
                "RunThroughRouterAsync requires ICasRouterService — pass it to the constructor.");
        }

        var pairs = ConformancePairs.All.Where(p => p.Category != "placeholder").ToArray();
        var mismatches = new List<RouterConformanceResult>();
        var sw = Stopwatch.StartNew();
        int correct = 0;

        _logger.LogInformation(
            "[CAS_CONFORMANCE_ROUTER_START] total_pairs={Count}", pairs.Length);

        foreach (var pair in pairs)
        {
            ct.ThrowIfCancellationRequested();
            var pairSw = Stopwatch.StartNew();
            CasVerifyResult res;
            try
            {
                res = await _router.VerifyAsync(new CasVerifyRequest(
                    Operation: CasOperation.Equivalence,
                    ExpressionA: pair.ExpressionA,
                    ExpressionB: pair.ExpressionB,
                    Variable: null,
                    Tolerance: 1e-9), ct);
            }
            catch (Exception ex)
            {
                pairSw.Stop();
                mismatches.Add(new RouterConformanceResult
                {
                    PairId = pair.Id,
                    RouterVerified = false,
                    ExpectedEquivalent = pair.ExpectedEquivalent,
                    EngineUsed = "none",
                    Error = ex.Message,
                    Latency = pairSw.Elapsed
                });
                continue;
            }
            pairSw.Stop();

            var verified = res.Verified && res.Status == CasVerifyStatus.Ok;
            if (verified == pair.ExpectedEquivalent) correct++;
            else
            {
                mismatches.Add(new RouterConformanceResult
                {
                    PairId = pair.Id,
                    RouterVerified = verified,
                    ExpectedEquivalent = pair.ExpectedEquivalent,
                    EngineUsed = res.Engine,
                    Error = res.ErrorMessage,
                    Latency = pairSw.Elapsed
                });
            }
        }

        sw.Stop();
        var agg = new RouterConformanceSuiteResult
        {
            TotalPairs = pairs.Length,
            CorrectCount = correct,
            Mismatches = mismatches,
            TotalDuration = sw.Elapsed,
            RunAt = DateTimeOffset.UtcNow
        };

        _logger.LogInformation(
            "[CAS_CONFORMANCE_ROUTER_DONE] pairs={Total} correct={Correct} rate={Rate:P2} threshold_pass={Pass}",
            agg.TotalPairs, agg.CorrectCount, agg.PassRate, agg.PassesThreshold);

        return agg;
    }

    private async Task<ConformanceSuiteResult> RunCoreAsync(
        Func<ConformancePair, bool> selector,
        CancellationToken ct)
    {
        var pairs = ConformancePairs.All.Where(selector).ToArray();
        var results = new List<ConformanceResult>(pairs.Length);
        var disagreements = new List<ConformanceResult>();
        var totalSw = Stopwatch.StartNew();
        int agreements = 0;

        _logger.LogInformation(
            "[CAS_CONFORMANCE_START] total_pairs={Count}", pairs.Length);

        foreach (var pair in pairs)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RunPairAsync(pair, ct);
            results.Add(result);
            if (result.Agree) agreements++;
            else disagreements.Add(result);
        }

        totalSw.Stop();

        var agg = new ConformanceSuiteResult
        {
            TotalPairs = pairs.Length,
            AgreementCount = agreements,
            Disagreements = disagreements,
            TotalDuration = totalSw.Elapsed,
            RunAt = DateTimeOffset.UtcNow
        };

        _logger.LogInformation(
            "[CAS_CONFORMANCE_DONE] pairs={Total} agree={Agree} rate={Rate:P2} duration_ms={Ms} threshold_pass={Pass}",
            agg.TotalPairs, agg.AgreementCount, agg.AgreementRate,
            agg.TotalDuration.TotalMilliseconds, agg.PassesThreshold);

        return agg;
    }

    private async Task<ConformanceResult> RunPairAsync(ConformancePair pair, CancellationToken ct)
    {
        var request = new CasVerifyRequest(
            Operation: CasOperation.Equivalence,
            ExpressionA: pair.ExpressionA,
            ExpressionB: pair.ExpressionB,
            Variable: null,
            Tolerance: 1e-9);

        // MathNet runs synchronously in-proc.
        var mathnetSw = Stopwatch.StartNew();
        CasVerifyResult mathnetResult;
        try
        {
            mathnetResult = _mathNet.Verify(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[CAS_CONFORMANCE_PAIR_ERROR] pairId={Id} engine=MathNet", pair.Id);
            mathnetResult = CasVerifyResult.Error(
                CasOperation.Equivalence, "MathNet", 0, ex.Message, CasVerifyStatus.Error);
        }
        mathnetSw.Stop();

        // SymPy sidecar (HTTP). Errors become non-agreement, not crashes.
        var sympySw = Stopwatch.StartNew();
        CasVerifyResult sympyResult;
        try
        {
            sympyResult = await _sympy.VerifyAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[CAS_CONFORMANCE_PAIR_ERROR] pairId={Id} engine=SymPy", pair.Id);
            sympyResult = CasVerifyResult.Error(
                CasOperation.Equivalence, "SymPy", 0, ex.Message, CasVerifyStatus.Error);
        }
        sympySw.Stop();

        return new ConformanceResult
        {
            PairId = pair.Id,
            SymPyResult = sympyResult.Verified,
            MathNetResult = mathnetResult.Verified,
            ExpectedEquivalent = pair.ExpectedEquivalent,
            SymPyLatency = sympySw.Elapsed,
            MathNetLatency = mathnetSw.Elapsed,
            SymPyError = sympyResult.ErrorMessage,
            MathNetError = mathnetResult.ErrorMessage
        };
    }
}
