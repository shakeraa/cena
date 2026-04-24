// =============================================================================
// Cena Platform -- Concurrency Limiter (EMU-002.2)
// Enforces a hard cap on the number of students active concurrently during
// emulation: max 30% of total students (e.g. 300 of 1,000).
//
// When at capacity, new arrivals queue and enter as slots become available.
// Backpressure: when queue depth > 50, the caller should slow arrival dispatch.
// =============================================================================

namespace Cena.Emulator.Scheduler;

/// <summary>
/// Snapshot of concurrency metrics at a point in time.
/// </summary>
public sealed record ConcurrencyMetrics(
    int CurrentActive,
    int QueueDepth,
    int PeakConcurrency,
    double AvgSessionDurationMs,
    bool BackpressureActive);

/// <summary>
/// Thread-safe concurrency limiter that enforces a hard cap on concurrent
/// student sessions. Provides async acquire/release with queue-based back-pressure.
/// </summary>
public sealed class ConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _backpressureThreshold;

    private int _currentActive;
    private int _queueDepth;
    private int _peakConcurrency;
    private long _totalCompletedSessions;
    private long _totalSessionDurationMs;

    private bool _disposed;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a concurrency limiter.
    /// </summary>
    /// <param name="maxConcurrency">Hard cap: maximum simultaneous active students.</param>
    /// <param name="backpressureThreshold">Queue depth at which backpressure is signalled.</param>
    public ConcurrencyLimiter(int maxConcurrency, int backpressureThreshold = 50)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Must be positive.");

        _maxConcurrency        = maxConcurrency;
        _backpressureThreshold = backpressureThreshold;
        _semaphore             = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    // ── Public properties ─────────────────────────────────────────────────────

    /// <summary>Number of slots currently occupied.</summary>
    public int CurrentActive => Volatile.Read(ref _currentActive);

    /// <summary>Number of callers waiting for a slot.</summary>
    public int QueueDepth => Volatile.Read(ref _queueDepth);

    /// <summary>Peak concurrent sessions observed since construction.</summary>
    public int PeakConcurrency => Volatile.Read(ref _peakConcurrency);

    /// <summary>True when the queue depth has exceeded the backpressure threshold.</summary>
    public bool BackpressureActive => QueueDepth > _backpressureThreshold;

    /// <summary>Maximum allowed concurrent sessions.</summary>
    public int MaxConcurrency => _maxConcurrency;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Acquire a concurrency slot. Waits if the cap is already reached.
    /// Increments queue depth while waiting, decrements on entry.
    /// </summary>
    public async Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Interlocked.Increment(ref _queueDepth);
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _queueDepth);
        }

        var active = Interlocked.Increment(ref _currentActive);

        // Track peak — spin CAS to avoid race on peak update
        int observed;
        do
        {
            observed = Volatile.Read(ref _peakConcurrency);
            if (active <= observed) break;
        }
        while (Interlocked.CompareExchange(ref _peakConcurrency, active, observed) != observed);
    }

    /// <summary>
    /// Release a concurrency slot and record session duration.
    /// </summary>
    /// <param name="sessionDurationMs">Elapsed milliseconds for the completed session.</param>
    public void Release(long sessionDurationMs = 0)
    {
        if (_disposed) return;

        Interlocked.Decrement(ref _currentActive);

        if (sessionDurationMs > 0)
        {
            Interlocked.Increment(ref _totalCompletedSessions);
            Interlocked.Add(ref _totalSessionDurationMs, sessionDurationMs);
        }

        _semaphore.Release();
    }

    /// <summary>
    /// Returns a point-in-time snapshot of all concurrency metrics.
    /// </summary>
    public ConcurrencyMetrics GetMetrics()
    {
        var completed = Volatile.Read(ref _totalCompletedSessions);
        var totalMs   = Volatile.Read(ref _totalSessionDurationMs);
        var avgMs     = completed > 0 ? (double)totalMs / completed : 0.0;

        return new ConcurrencyMetrics(
            CurrentActive:        CurrentActive,
            QueueDepth:           QueueDepth,
            PeakConcurrency:      PeakConcurrency,
            AvgSessionDurationMs: avgMs,
            BackpressureActive:   BackpressureActive);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }
}
