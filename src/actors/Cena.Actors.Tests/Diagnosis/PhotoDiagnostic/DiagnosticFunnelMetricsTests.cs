// =============================================================================
// Cena Platform — Diagnostic funnel metrics tests (EPIC-PRR-J PRR-426)
//
// Locks the funnel counter's tag shape so the dashboard rollup queries
// (stage-to-stage conversion; per-stage abandonment) remain stable
// across refactors. Uses MeterListener to observe the emitted tag set
// on every RecordFunnelEvent call — no OTel collector round-trip needed.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticFunnelMetricsTests
{
    [Fact]
    public void RecordFunnelEvent_emits_stage_and_outcome_tags()
    {
        using var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        var observed = new List<Dictionary<string, object?>>();
        using var listener = SubscribeToFunnel(observed);

        metrics.RecordFunnelEvent(DiagnosticFunnelStage.WrongAnswer, "seen");
        metrics.RecordFunnelEvent(DiagnosticFunnelStage.PreviewConfirmed, "confirmed");
        metrics.RecordFunnelEvent(DiagnosticFunnelStage.DisputeFiled, "filed");

        Assert.Equal(3, observed.Count);
        Assert.Equal("WrongAnswer", (string?)observed[0]["stage"]);
        Assert.Equal("seen", (string?)observed[0]["outcome"]);
        Assert.Equal("PreviewConfirmed", (string?)observed[1]["stage"]);
        Assert.Equal("DisputeFiled", (string?)observed[2]["stage"]);
    }

    [Fact]
    public void Every_canonical_funnel_stage_is_emittable()
    {
        // Guards against a future refactor silently dropping a stage
        // enum value — the dashboard rollup depends on all 14 stages
        // existing (the 12 nominal + HintRequested branch + DisputeFiled).
        using var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        var observed = new List<Dictionary<string, object?>>();
        using var listener = SubscribeToFunnel(observed);

        foreach (var stage in Enum.GetValues<DiagnosticFunnelStage>())
        {
            metrics.RecordFunnelEvent(stage, "ok");
        }

        Assert.Equal(Enum.GetValues<DiagnosticFunnelStage>().Length, observed.Count);
        var seenStages = observed.Select(o => (string?)o["stage"]).ToHashSet();
        foreach (var stage in Enum.GetNames<DiagnosticFunnelStage>())
        {
            Assert.Contains(stage, seenStages);
        }
    }

    [Fact]
    public void Outcome_tag_is_preserved_verbatim_so_dashboards_can_split()
    {
        // Outcome semantics are caller-chosen; we assert the counter does
        // not normalize or rewrite the string. Dashboards rely on that
        // freedom for per-stage custom conversion rates (e.g.,
        // AnalysisComplete.{retry|dispute} vs ReflectionGateShown.
        // {confirmed|abandoned}).
        using var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        var observed = new List<Dictionary<string, object?>>();
        using var listener = SubscribeToFunnel(observed);

        metrics.RecordFunnelEvent(DiagnosticFunnelStage.AnalysisComplete, "retry");
        metrics.RecordFunnelEvent(DiagnosticFunnelStage.AnalysisComplete, "dispute");
        metrics.RecordFunnelEvent(DiagnosticFunnelStage.ReflectionGateShown, "abandoned");

        var outcomes = observed.Select(o => (string?)o["outcome"]).ToList();
        Assert.Contains("retry", outcomes);
        Assert.Contains("dispute", outcomes);
        Assert.Contains("abandoned", outcomes);
    }

    private static MeterListener SubscribeToFunnel(
        List<Dictionary<string, object?>> observed)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PhotoDiagnosticMetrics.MeterName
                    && instrument.Name == "cena.photo_diagnostic.funnel_events_total")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>();
            for (var i = 0; i < tags.Length; i++)
            {
                dict[tags[i].Key] = tags[i].Value;
            }
            observed.Add(dict);
        });
        listener.Start();
        return listener;
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
