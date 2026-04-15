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
}

/// <inheritdoc />
public sealed class CasConformanceSuiteRunner : ICasConformanceSuiteRunner
{
    private readonly IMathNetVerifier _mathNet;
    private readonly ISymPySidecarClient _sympy;
    private readonly ILogger<CasConformanceSuiteRunner> _logger;

    public CasConformanceSuiteRunner(
        IMathNetVerifier mathNet,
        ISymPySidecarClient sympy,
        ILogger<CasConformanceSuiteRunner> logger)
    {
        _mathNet = mathNet;
        _sympy = sympy;
        _logger = logger;
    }

    public Task<ConformanceSuiteResult> RunAsync(CancellationToken ct = default) =>
        RunCoreAsync(pair => pair.Category != "placeholder", ct);

    public Task<ConformanceSuiteResult> RunCategoryAsync(string category, CancellationToken ct = default) =>
        RunCoreAsync(pair => pair.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
                             && pair.Category != "placeholder", ct);

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
