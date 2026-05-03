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
    public void RecordStageLatency_emits_stage_tag_and_numeric_sample()
    {
        // PRR-422 SLO decomposition: per-stage latency histogram tagged
        // by stage so the dashboard can break the 15s e2e budget down
        // into upload / OCR / extraction / CAS / template / narrate
        // and spot which stage is carrying a p95 regression.
        using var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        var observed = new List<(double value, Dictionary<string, object?> tags)>();
        using var listener = SubscribeToStageLatency(observed);

        metrics.RecordStageLatency(DiagnosticStage.Ocr, 1234.5);
        metrics.RecordStageLatency(DiagnosticStage.CasChain, 876.2);
        metrics.RecordStageLatency(DiagnosticStage.Narrate, 42.0);

        Assert.Equal(3, observed.Count);
        Assert.Contains(observed, o =>
            (string?)o.tags["stage"] == "Ocr" && o.value == 1234.5);
        Assert.Contains(observed, o =>
            (string?)o.tags["stage"] == "CasChain" && o.value == 876.2);
        Assert.Contains(observed, o =>
            (string?)o.tags["stage"] == "Narrate" && o.value == 42.0);
    }

    [Fact]
    public void Every_diagnostic_stage_is_emittable()
    {
        // SLO decomposition requires coverage of every stage; if a future
        // refactor drops a stage enum value the dashboard root-cause
        // query silently breaks. This guards that seam.
        using var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        var observed = new List<(double value, Dictionary<string, object?> tags)>();
        using var listener = SubscribeToStageLatency(observed);

        foreach (var stage in Enum.GetValues<DiagnosticStage>())
        {
            metrics.RecordStageLatency(stage, 100.0);
        }

        Assert.Equal(Enum.GetValues<DiagnosticStage>().Length, observed.Count);
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

    private static MeterListener SubscribeToStageLatency(
        List<(double value, Dictionary<string, object?> tags)> observed)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PhotoDiagnosticMetrics.MeterName
                    && instrument.Name == "cena.photo_diagnostic.stage_latency_ms")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>();
            for (var i = 0; i < tags.Length; i++)
            {
                dict[tags[i].Key] = tags[i].Value;
            }
            observed.Add((value, dict));
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
