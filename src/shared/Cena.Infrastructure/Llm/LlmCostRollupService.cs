// =============================================================================
// Cena Platform — LLM cost rollup service (prr-112).
//
// The admin cost dashboard needs a per-feature × per-cohort rollup over a
// time window. The authoritative source of these numbers is the Prometheus
// metric cena_llm_call_cost_usd_total{feature, institute_id, task, model_id}
// emitted by LlmCostMetric (prr-046).
//
// This interface is the seam between "endpoint HTTP handler" and "cost
// data source":
//
//   - Production binding: PrometheusLlmCostRollupService queries Prometheus
//     via HTTP range-query (`/api/v1/query_range`) and folds the resulting
//     time-series into CohortCostRollup.
//   - Test / stub binding: provide a deterministic in-memory implementation.
//
// The cohort concept maps to institute_id: Cena has no dedicated "cohort"
// label today, but every institute scopes itself to a tenant, and the
// persona panel uses "cohort" as an informal synonym for the grade-level
// slice within an institute. Phase 1B of prr-112 supports institute_id
// cohorts; Phase 2 (if/when we add a `cohort_id` label to the metric)
// is a pure extension.
//
// WHY an interface and not a concrete class:
//
//   - Endpoint tests must not require Prometheus to stand up. A DI
//     substitution keeps the admin endpoint testable with an in-memory
//     implementation.
//   - The query path is a thin HTTP client; binding it behind an interface
//     keeps the HTTP fan-out contained to ONE file so mocks are trivial.
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Per-feature cost slice inside a cohort rollup.
/// </summary>
/// <param name="Feature">Feature tag (kebab-case, e.g. "socratic").</param>
/// <param name="CostUsd">Sum of USD cost for this feature within the window.</param>
/// <param name="CallCount">Optional: number of calls counted; 0 when unknown.</param>
public sealed record FeatureCostSliceDto(
    string Feature,
    double CostUsd,
    long CallCount);

/// <summary>
/// Per-cohort (institute_id) cost rollup across a time window.
/// </summary>
/// <param name="CohortId">Cohort identifier (today: institute_id).</param>
/// <param name="FromUtc">Window start (ISO-8601).</param>
/// <param name="ToUtc">Window end (ISO-8601).</param>
/// <param name="TotalCostUsd">Sum across all features in the window.</param>
/// <param name="FeatureSlices">Per-feature breakdown; may be empty.</param>
public sealed record CohortCostRollupDto(
    string CohortId,
    string FromUtc,
    string ToUtc,
    double TotalCostUsd,
    IReadOnlyList<FeatureCostSliceDto> FeatureSlices);

/// <summary>
/// Service abstraction returning per-feature cost rollups for a cohort
/// over a time window. Backed by Prometheus in production; pluggable for
/// tests.
/// </summary>
public interface ILlmCostRollupService
{
    /// <summary>
    /// Return the per-feature cost rollup for the given cohort + window.
    /// </summary>
    Task<CohortCostRollupDto> GetCohortRollupAsync(
        string cohortId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Default in-memory implementation. Always returns zero-cost slices. DI
/// binding swaps to the Prometheus-backed implementation in production; the
/// dashboard remains structurally visible in test + local-dev even when
/// the metric pipeline is not wired.
/// </summary>
public sealed class NullLlmCostRollupService : ILlmCostRollupService
{
    public Task<CohortCostRollupDto> GetCohortRollupAsync(
        string cohortId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cohortId);
        return Task.FromResult(new CohortCostRollupDto(
            CohortId: cohortId,
            FromUtc: fromUtc.ToUniversalTime().ToString("O",
                System.Globalization.CultureInfo.InvariantCulture),
            ToUtc: toUtc.ToUniversalTime().ToString("O",
                System.Globalization.CultureInfo.InvariantCulture),
            TotalCostUsd: 0.0,
            FeatureSlices: Array.Empty<FeatureCostSliceDto>()));
    }
}
