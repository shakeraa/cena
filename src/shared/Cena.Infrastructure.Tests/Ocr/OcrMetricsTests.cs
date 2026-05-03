// =============================================================================
// Cena Platform — OcrMetrics tests (RDY-OCR-OBSERVABILITY / Phase 4)
//
// Uses the System.Diagnostics.Metrics MeterListener to capture every
// measurement emitted by OcrMetrics and assert the shape + tag set.
// Runs on the real OcrMetrics (no test doubles) so the production code
// path is exercised exactly.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Observability;

namespace Cena.Infrastructure.Tests.Ocr;

public sealed class OcrMetricsTests
{
    private sealed record Measurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);

    private static (OcrMetrics metrics, List<Measurement> sink, IDisposable listener) Setup()
    {
        var metrics = new OcrMetrics();
        var sink = new List<Measurement>();

        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OcrMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            sink.Add(new Measurement(inst.Name, value, TagsToDict(tags)));
        });
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            sink.Add(new Measurement(inst.Name, value, TagsToDict(tags)));
        });
        listener.Start();
        return (metrics, sink, listener);
    }

    private static Dictionary<string, object?> TagsToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var d = new Dictionary<string, object?>(tags.Length);
        for (var i = 0; i < tags.Length; i++) d[tags[i].Key] = tags[i].Value;
        return d;
    }

    [Fact]
    public void RecordRequest_Fires_Counter_With_Expected_Tags()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordRequest(CascadeSurface.StudentInteractive, "image/png", "ok");

            var m = Assert.Single(sink);
            Assert.Equal("ocr.cascade.requests", m.InstrumentName);
            Assert.Equal(1, m.Value);
            Assert.Equal("A",          m.Tags["surface"]);
            Assert.Equal("image/png",  m.Tags["content_type"]);
            Assert.Equal("ok",         m.Tags["outcome"]);
        }
    }

    [Fact]
    public void RecordRequest_Maps_AdminBatch_To_B()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordRequest(CascadeSurface.AdminBatch, "application/pdf", "low_conf");

            var m = Assert.Single(sink);
            Assert.Equal("B", m.Tags["surface"]);
            Assert.Equal("low_conf", m.Tags["outcome"]);
        }
    }

    [Fact]
    public void RecordTotalLatency_Fires_Histogram_With_Surface_And_Outcome()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordTotalLatency(CascadeSurface.StudentInteractive, "ok", latencyMs: 850.25);

            var m = Assert.Single(sink);
            Assert.Equal("ocr.cascade.total_latency", m.InstrumentName);
            Assert.Equal(850.25, m.Value);
            Assert.Equal("A", m.Tags["surface"]);
            Assert.Equal("ok", m.Tags["outcome"]);
        }
    }

    [Fact]
    public void RecordLayerLatency_Fires_With_Layer_Tag()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordLayerLatency("layer_4_gate", CascadeSurface.AdminBatch, latencyMs: 120.5);

            var m = Assert.Single(sink);
            Assert.Equal("ocr.layer.latency", m.InstrumentName);
            Assert.Equal("layer_4_gate", m.Tags["layer"]);
            Assert.Equal("B", m.Tags["surface"]);
        }
    }

    [Fact]
    public void RecordFallbackFired_Carries_Fallback_And_Reason()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordFallbackFired("mathpix", "low_conf");

            var m = Assert.Single(sink);
            Assert.Equal("ocr.cascade.fallbacks_fired", m.InstrumentName);
            Assert.Equal("mathpix",  m.Tags["fallback"]);
            Assert.Equal("low_conf", m.Tags["reason"]);
        }
    }

    [Fact]
    public void RecordCasVerdict_Carries_Verdict_Tag()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordCasVerdict("verified");
            metrics.RecordCasVerdict("failed");
            metrics.RecordCasVerdict("unverifiable");

            Assert.Equal(3, sink.Count);
            Assert.Contains(sink, m => (string?)m.Tags["verdict"] == "verified");
            Assert.Contains(sink, m => (string?)m.Tags["verdict"] == "failed");
            Assert.Contains(sink, m => (string?)m.Tags["verdict"] == "unverifiable");
        }
    }

    [Fact]
    public void RecordHumanReviewFlagged_Carries_Surface_And_Reason()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            metrics.RecordHumanReviewFlagged(CascadeSurface.AdminBatch, "majority_math_failed_cas");

            var m = Assert.Single(sink);
            Assert.Equal("ocr.human_review.flagged", m.InstrumentName);
            Assert.Equal("B", m.Tags["surface"]);
            Assert.Equal("majority_math_failed_cas", m.Tags["reason"]);
        }
    }

    [Fact]
    public async Task MeasureLayerAsync_Records_Elapsed_Time()
    {
        var (metrics, sink, listener) = Setup();
        using (listener) using (metrics)
        {
            var result = await metrics.MeasureLayerAsync(
                "layer_0_preprocess",
                CascadeSurface.StudentInteractive,
                async () =>
                {
                    await Task.Delay(20);
                    return 42;
                });

            Assert.Equal(42, result);
            var m = Assert.Single(sink);
            Assert.Equal("ocr.layer.latency", m.InstrumentName);
            Assert.Equal("layer_0_preprocess", m.Tags["layer"]);
            Assert.True(m.Value >= 20, $"expected ≥20ms, got {m.Value}");
        }
    }

    [Fact]
    public void Meter_Is_Named_Cena_Infrastructure_Ocr()
    {
        Assert.Equal("Cena.Infrastructure.Ocr", OcrMetrics.MeterName);
        Assert.Equal("1.0", OcrMetrics.MeterVersion);
    }
}
