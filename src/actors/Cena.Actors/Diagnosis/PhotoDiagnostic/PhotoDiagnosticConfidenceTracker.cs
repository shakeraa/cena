// =============================================================================
// Cena Platform — PhotoDiagnosticConfidenceTracker (EPIC-PRR-J PRR-420/421)
//
// Per-student rolling-window tracker for OCR confidence + template score.
// When a student's last-N photos are persistently low-quality, the caller
// should switch the upsell from "try another photo" to "try typed input"
// — different camera/handwriting/lighting isn't going to help them.
//
// Memory model: in-memory ConcurrentDictionary keyed by student subject id
// with a small bounded window (default 8 samples). Eviction is by age
// (sliding TTL, default 30 min) so idle students don't accumulate state
// forever. Session-scoped per memory "Misconception session scope"
// (ADR-0003): we never persist this off-session.
//
// Production-grade: real concurrent structures, no stubs.
// Bounded memory: N samples × active-student count; a Bagrut-morning
// spike at 5000 concurrent students × 8 samples × ~40 bytes/sample
// ≈ 1.6 MB. Well within budget.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Advice surfaced to the UI when the tracker decides a student's photo
/// pipeline is producing persistently poor signal.
/// </summary>
public enum PhotoDiagnosticAdvice
{
    /// <summary>Normal operation; keep offering photo upload.</summary>
    Continue,
    /// <summary>Two+ low-confidence OCR samples in a row; prompt a retake/lighting hint.</summary>
    SuggestRetake,
    /// <summary>Persistent low-confidence across the window; surface typed-input path.</summary>
    SuggestTypedInput,
}

/// <summary>Single observation added to the rolling window.</summary>
public sealed record DiagnosticObservation(
    double OcrConfidence,
    double TemplateScore,
    DateTimeOffset RecordedAt);

/// <summary>Seam for the tracker; injectable for tests.</summary>
public interface IPhotoDiagnosticConfidenceTracker
{
    /// <summary>Record one diagnostic pass for the given student.</summary>
    void Record(string studentSubjectId, DiagnosticObservation observation);

    /// <summary>Advice to surface next — based on the student's rolling window.</summary>
    PhotoDiagnosticAdvice GetAdvice(string studentSubjectId);

    /// <summary>Evict stale windows; safe to call from a background timer.</summary>
    int EvictStale(DateTimeOffset asOf);
}

/// <summary>Default in-memory, session-scoped tracker.</summary>
public sealed class PhotoDiagnosticConfidenceTracker : IPhotoDiagnosticConfidenceTracker
{
    /// <summary>Rolling window size per student.</summary>
    public const int WindowSize = 8;

    /// <summary>Entries older than this are evicted.</summary>
    public static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    /// <summary>Below this OCR confidence the sample counts as "poor".</summary>
    public const double OcrPoorThreshold = 0.60;

    /// <summary>Below this template score the sample counts as "no confident match".</summary>
    public const double TemplatePoorThreshold = 0.70;

    /// <summary>
    /// Count of consecutive poor samples (from most-recent backwards) at
    /// which we switch from "SuggestRetake" to "SuggestTypedInput".
    /// </summary>
    public const int PersistentPoorThreshold = 3;

    private readonly ConcurrentDictionary<string, StudentWindow> _windows = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Record(string studentSubjectId, DiagnosticObservation observation)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId))
            throw new ArgumentException("studentSubjectId is required.", nameof(studentSubjectId));
        ArgumentNullException.ThrowIfNull(observation);

        var window = _windows.GetOrAdd(studentSubjectId, _ => new StudentWindow());
        window.Add(observation);
    }

    /// <inheritdoc/>
    public PhotoDiagnosticAdvice GetAdvice(string studentSubjectId)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId)) return PhotoDiagnosticAdvice.Continue;
        if (!_windows.TryGetValue(studentSubjectId, out var window)) return PhotoDiagnosticAdvice.Continue;
        return window.ComputeAdvice();
    }

    /// <inheritdoc/>
    public int EvictStale(DateTimeOffset asOf)
    {
        var evicted = 0;
        foreach (var kv in _windows)
        {
            if (kv.Value.IsIdle(asOf, SessionTtl))
            {
                if (_windows.TryRemove(kv.Key, out _))
                    evicted++;
            }
        }
        return evicted;
    }

    /// <summary>Per-student sliding window. Thread-safe via internal lock.</summary>
    private sealed class StudentWindow
    {
        private readonly object _lock = new();
        private readonly Queue<DiagnosticObservation> _samples = new(WindowSize + 1);
        private DateTimeOffset _lastRecordedAt = DateTimeOffset.MinValue;

        public void Add(DiagnosticObservation observation)
        {
            lock (_lock)
            {
                _samples.Enqueue(observation);
                while (_samples.Count > WindowSize) _samples.Dequeue();
                if (observation.RecordedAt > _lastRecordedAt) _lastRecordedAt = observation.RecordedAt;
            }
        }

        public bool IsIdle(DateTimeOffset asOf, TimeSpan ttl)
        {
            lock (_lock)
            {
                if (_samples.Count == 0) return true;
                return asOf - _lastRecordedAt > ttl;
            }
        }

        public PhotoDiagnosticAdvice ComputeAdvice()
        {
            lock (_lock)
            {
                if (_samples.Count == 0) return PhotoDiagnosticAdvice.Continue;

                var arr = _samples.ToArray();
                var trailingPoor = 0;
                for (int i = arr.Length - 1; i >= 0; i--)
                {
                    if (IsPoor(arr[i])) trailingPoor++;
                    else break;
                }

                if (trailingPoor >= PersistentPoorThreshold)
                    return PhotoDiagnosticAdvice.SuggestTypedInput;
                if (trailingPoor >= 2)
                    return PhotoDiagnosticAdvice.SuggestRetake;
                return PhotoDiagnosticAdvice.Continue;
            }
        }

        private static bool IsPoor(DiagnosticObservation o) =>
            o.OcrConfidence < OcrPoorThreshold || o.TemplateScore < TemplatePoorThreshold;
    }
}
