// =============================================================================
// Cena Platform — LLM cost metric (prr-046 + prr-233)
//
// Emits the canonical per-feature cost counter:
//
//   cena_llm_call_cost_usd_total{feature, tier, institute_id, task, model_id, exam_target_code}
//
// Every class carrying [TaskRouting] is expected to call
// ILlmCostMetric.Record(...) exactly once on the success path of its LLM
// call. Failure paths DO NOT emit (a failed call has no billable cost; the
// error budget lives in a separate counter governed by ADR-0026 §7).
//
// Label conventions (per task DoD + ADR-0026 §7 + prr-233):
//   - feature           [FeatureTag] — lowercase kebab-case, bounded vocabulary
//   - tier              "tier1" | "tier2" | "tier3" from [TaskRouting]
//   - task              [TaskRouting] task-name — matches routing-config.yaml row
//   - institute_id      tenant scope. "unknown" when the caller doesn't have it;
//                       intentional — see WHY note in the Record method body.
//   - model_id          resolved model (after fallback chain) — useful for
//                       tracking Sonnet→Haiku degradation cost.
//   - exam_target_code  prr-233: Ministry / catalog exam-target code (e.g.
//                       "BAGRUT_MATH_5U"). Optional; degrades to "unknown"
//                       using the same null-signifier as institute_id so
//                       the cost + cache dashboards align on the same
//                       missing-label bucket. Operational (catalog) code,
//                       not PII.
//
// No PII in labels:
//   The institute_id label is the finest tenant grain allowed. Student IDs,
//   thread IDs, and session IDs are intentionally NOT labels — they would
//   blow up Prometheus cardinality and, more importantly, leak PII into an
//   observability store that is not engineered for PII retention.
//
// See docs/adr/0026-llm-three-tier-routing.md §7 (Cost alerts).
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Emits the canonical per-feature LLM cost counter for every LLM call.
/// </summary>
public interface ILlmCostMetric
{
    /// <summary>
    /// Record the USD cost of a single LLM call on its success path. Called
    /// exactly once per call (do not double-count on multi-attempt retry
    /// loops — record the final winning attempt's tokens only).
    /// </summary>
    /// <param name="feature">
    /// Finops cost-center from the caller's <see cref="FeatureTagAttribute"/>.
    /// </param>
    /// <param name="tier">
    /// Routing tier from the caller's <see cref="TaskRoutingAttribute"/>.
    /// </param>
    /// <param name="task">
    /// Task name from the caller's <see cref="TaskRoutingAttribute"/> —
    /// matches a row in <c>contracts/llm/routing-config.yaml</c>.
    /// </param>
    /// <param name="modelId">
    /// Resolved model_id the call actually landed on (post-fallback).
    /// </param>
    /// <param name="inputTokens">Input tokens consumed by the call.</param>
    /// <param name="outputTokens">Output tokens produced by the call.</param>
    /// <param name="instituteId">
    /// Optional tenant-scope label. Pass null/empty when the call site does
    /// not yet thread institute_id through — "unknown" will be emitted so
    /// the cost is still counted toward the per-feature projection.
    /// </param>
    /// <param name="examTargetCode">
    /// prr-233: optional Ministry / catalog exam-target code. Pass null/empty
    /// when the call site has no active target (e.g. content-ingestion,
    /// system-prompt warm-ups); emits "unknown" in the label. Must not
    /// contain ':' (Prometheus label-value constraint on structured forms).
    /// </param>
    void Record(
        string feature,
        string tier,
        string task,
        string modelId,
        long inputTokens,
        long outputTokens,
        string? instituteId = null,
        string? examTargetCode = null);
}

/// <summary>
/// Default <see cref="ILlmCostMetric"/> implementation backed by
/// <see cref="System.Diagnostics.Metrics.Meter"/> (scraped by OpenTelemetry
/// → Prometheus).
/// </summary>
public sealed class LlmCostMetric : ILlmCostMetric
{
    /// <summary>Meter name the OTLP collector looks for.</summary>
    public const string MeterName = "Cena.Llm.Cost";

    /// <summary>
    /// Fully-qualified metric name emitted to Prometheus. Matches the
    /// dashboard and alert expressions in
    /// <c>deploy/observability/dashboards/llm-cost-projection.json</c> and
    /// <c>deploy/observability/alerting-rules.yaml</c>.
    /// </summary>
    public const string CostCounterName = "cena_llm_call_cost_usd_total";

    /// <summary>
    /// Histogram for per-call cost — lets the dashboard show a distribution
    /// and compute percentiles (e.g. p95 cost per feature) in addition to
    /// the rolling sum.
    /// </summary>
    public const string CostHistogramName = "cena_llm_call_cost_usd";

    /// <summary>Placeholder label value for unknown institute scope.</summary>
    public const string UnknownInstituteLabel = "unknown";

    /// <summary>
    /// prr-233: placeholder label value for unknown exam-target scope.
    /// Same spelling as <see cref="UnknownInstituteLabel"/> so dashboards
    /// can group the null-signifier consistently across cost + cache metrics.
    /// </summary>
    public const string UnknownExamTargetCodeLabel = "unknown";

    private readonly LlmPricingTable _pricing;
    private readonly Counter<double> _cost;
    private readonly Histogram<double> _costHistogram;

    public LlmCostMetric(IMeterFactory meterFactory, LlmPricingTable pricing)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(pricing);
        _pricing = pricing;

        var meter = meterFactory.Create(MeterName, "1.0.0");
        _cost = meter.CreateCounter<double>(
            CostCounterName,
            unit: "USD",
            description: "LLM call cost (USD) tagged by feature/tier/task/institute (prr-046)");
        _costHistogram = meter.CreateHistogram<double>(
            CostHistogramName,
            unit: "USD",
            description: "Per-call LLM cost distribution (USD) tagged by feature/tier/task (prr-046)");
    }

    public void Record(
        string feature,
        string tier,
        string task,
        string modelId,
        long inputTokens,
        long outputTokens,
        string? instituteId = null,
        string? examTargetCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(tier);
        ArgumentException.ThrowIfNullOrWhiteSpace(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        // prr-233: target code must be label-safe (no colons) because some
        // exporters flatten TagList entries into structured IDs. Empty/null
        // is fine (degrades to "unknown"); a colon is not.
        if (examTargetCode is not null && examTargetCode.Contains(':'))
        {
            throw new ArgumentException(
                $"exam_target_code must not contain ':' — got '{examTargetCode}'.",
                nameof(examTargetCode));
        }

        var cost = _pricing.ComputeCostUsd(modelId, inputTokens, outputTokens);

        // WHY default institute_id = "unknown" rather than throw:
        //   Threading tenant scope through every LLM service is a broader
        //   refactor than prr-046's scope (tracked under EPIC-PRR-B /
        //   prr-084 per-institute caps). Emitting "unknown" keeps the
        //   per-feature projection honest (the global rollup is still
        //   correct) while making the missing-tenant coverage visible on
        //   the dashboard's top-10-institutes panel — institutes that are
        //   wired through show up; "unknown" is an obvious gap to close.
        var institute = string.IsNullOrWhiteSpace(instituteId)
            ? UnknownInstituteLabel
            : instituteId!;

        // prr-233: exam_target_code degrades the same way. A call site that
        // does not yet thread the active target (e.g. a legacy prr-148 path
        // that has not migrated to prr-218 aggregates) emits "unknown" and
        // is visible on the per-target dashboard as a gap to close.
        var target = string.IsNullOrWhiteSpace(examTargetCode)
            ? UnknownExamTargetCodeLabel
            : examTargetCode!;

        var tags = new TagList
        {
            { "feature",          feature  },
            { "tier",             tier     },
            { "task",             task     },
            { "institute_id",     institute },
            { "model_id",         modelId  },
            { "exam_target_code", target   },
        };

        _cost.Add(cost, tags);
        _costHistogram.Record(cost, tags);
    }
}
