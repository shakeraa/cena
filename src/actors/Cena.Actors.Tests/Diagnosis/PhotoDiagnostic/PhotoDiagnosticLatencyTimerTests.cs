// =============================================================================
// Cena Platform — PhotoDiagnosticLatencyTimer tests (EPIC-PRR-J PRR-422)
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class PhotoDiagnosticLatencyTimerTests
{
    private static PhotoDiagnosticMetrics NewMetrics() =>
        new(new DummyMeterFactory());

    [Fact]
    public void ScopeRecordsSucceededWhenMarkSucceededCalled()
    {
        using var metrics = NewMetrics();
        var factory = new PhotoDiagnosticLatencyTimer(metrics);

        using var listener = new MeterListener();
        var tagValues = new List<string>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PhotoDiagnosticMetrics.MeterName &&
                instrument.Name == "cena.photo_diagnostic.end_to_end_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == "outcome" && t.Value is string s) tagValues.Add(s);
            }
        });
        listener.Start();

        using (var scope = factory.Start())
        {
            scope.MarkSucceeded();
        }
        listener.Dispose();

        Assert.Contains("succeeded", tagValues);
    }

    [Fact]
    public void ScopeRecordsFailedWhenMarkSucceededNotCalled()
    {
        using var metrics = NewMetrics();
        var factory = new PhotoDiagnosticLatencyTimer(metrics);

        using var listener = new MeterListener();
        var tagValues = new List<string>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PhotoDiagnosticMetrics.MeterName &&
                instrument.Name == "cena.photo_diagnostic.end_to_end_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == "outcome" && t.Value is string s) tagValues.Add(s);
            }
        });
        listener.Start();

        using (var _ = factory.Start()) { /* no MarkSucceeded */ }
        listener.Dispose();

        Assert.Contains("failed", tagValues);
    }

    [Fact]
    public void ElapsedMsIsMonotonicallyNonDecreasing()
    {
        using var metrics = NewMetrics();
        var factory = new PhotoDiagnosticLatencyTimer(metrics);
        using var scope = factory.Start();
        var e1 = scope.ElapsedMs;
        Thread.SpinWait(1_000_000);
        var e2 = scope.ElapsedMs;
        Assert.True(e2 >= e1);
    }

    [Fact]
    public void DoubleDisposeIsSafe()
    {
        using var metrics = NewMetrics();
        var factory = new PhotoDiagnosticLatencyTimer(metrics);
        var scope = factory.Start();
        scope.MarkSucceeded();
        scope.Dispose();
        scope.Dispose(); // must not throw or double-record
    }

    [Fact]
    public void NullMetricsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new PhotoDiagnosticLatencyTimer(null!));
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
