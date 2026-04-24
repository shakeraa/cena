// =============================================================================
// Cena Platform — Coverage Waterfall Metrics (prr-201)
//
// Single source of truth for the prr-201 metric set:
//   - cena_coverage_rung_filled_total{stage,result}
//   - cena_coverage_rung_gap_total{cell}
//   - cena_coverage_waterfall_duration_seconds{stage}
//   - cena_coverage_drop_total{stage,kind}
//   - cena_coverage_curator_queue_depth{cell}  (observable gauge)
//   - cena_coverage_llm_cost_usd_total{institute}
// =============================================================================

using System.Diagnostics.Metrics;

namespace Cena.Actors.QuestionBank.Coverage;

internal static class CoverageWaterfallMetrics
{
    public static readonly Meter Meter = new("Cena.Coverage", "1.0");

    public static readonly Counter<long> RungFilledTotal = Meter.CreateCounter<long>(
        "cena_coverage_rung_filled_total",
        description: "Waterfall invocations bucketed by final outcome");

    public static readonly Counter<long> RungGapTotal = Meter.CreateCounter<long>(
        "cena_coverage_rung_gap_total",
        description: "Cumulative gap (target-filled) across waterfall calls");

    public static readonly Histogram<double> WaterfallDuration = Meter.CreateHistogram<double>(
        "cena_coverage_waterfall_duration_seconds",
        unit: "s",
        description: "Wall-clock duration per waterfall stage");

    public static readonly Counter<long> DropTotal = Meter.CreateCounter<long>(
        "cena_coverage_drop_total",
        description: "Stage-2/3 candidate drops bucketed by kind");

    public static readonly Counter<double> LlmCostUsdTotal = Meter.CreateCounter<double>(
        "cena_coverage_llm_cost_usd_total",
        description: "Stage-2 LLM spend accumulated per institute");

    public static readonly Counter<long> CuratorEnqueuedTotal = Meter.CreateCounter<long>(
        "cena_coverage_curator_queue_depth",
        description: "Stage-3 curator-queue enqueues; cumulative proxy for queue depth");
}
