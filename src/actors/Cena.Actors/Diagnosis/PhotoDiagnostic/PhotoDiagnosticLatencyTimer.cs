// =============================================================================
// Cena Platform — PhotoDiagnosticLatencyTimer (EPIC-PRR-J PRR-422)
//
// Stopwatch-backed IDisposable that records the full upload → narration
// latency on disposal. Launch SLO (docs/research/cena-sexy-game-research-
// 2026-04-11.md §latency-budget): p95 < 10 seconds from "upload received"
// to "narration rendered" — beyond that the student disengages.
//
// Pattern:
//   using (var t = latencyTimer.Start())
//   {
//       ... ingest + OCR + CAS + template + narrate ...
//       t.MarkSucceeded();      // before disposal, if you want the outcome tag
//   }
// The timer records automatically on disposal. If MarkSucceeded was never
// called we tag the sample as "failed" so we can slice the histogram.
// =============================================================================

using System.Diagnostics;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Factory for scoped latency timers. Inject this, not the timer.</summary>
public interface IPhotoDiagnosticLatencyTimer
{
    /// <summary>Start a new scoped timer. Dispose records the sample.</summary>
    PhotoDiagnosticLatencyScope Start();
}

/// <summary>Default implementation — records into PhotoDiagnosticMetrics.</summary>
public sealed class PhotoDiagnosticLatencyTimer : IPhotoDiagnosticLatencyTimer
{
    private readonly PhotoDiagnosticMetrics _metrics;

    public PhotoDiagnosticLatencyTimer(PhotoDiagnosticMetrics metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public PhotoDiagnosticLatencyScope Start() => new(_metrics);
}

/// <summary>
/// One scoped latency measurement. Use within a `using` block.
/// Records the elapsed time on disposal, tagged with success/failure.
/// </summary>
public sealed class PhotoDiagnosticLatencyScope : IDisposable
{
    private readonly PhotoDiagnosticMetrics _metrics;
    private readonly Stopwatch _stopwatch;
    private bool _succeeded;
    private bool _disposed;

    internal PhotoDiagnosticLatencyScope(PhotoDiagnosticMetrics metrics)
    {
        _metrics = metrics;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>Mark the diagnostic as successful before disposal.</summary>
    public void MarkSucceeded() => _succeeded = true;

    /// <summary>Elapsed milliseconds so far (for debug logging).</summary>
    public double ElapsedMs => _stopwatch.Elapsed.TotalMilliseconds;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        _metrics.RecordEndToEndLatency(_stopwatch.Elapsed.TotalMilliseconds, _succeeded);
    }
}
