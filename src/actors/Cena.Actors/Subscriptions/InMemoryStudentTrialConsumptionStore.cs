// =============================================================================
// Cena Platform — InMemoryStudentTrialConsumptionStore (Phase 1D)
//
// Production-grade single-host implementation. Per-student state is wrapped
// behind a per-key lock so concurrent IncrementAsync calls cannot race.
// Distinct UTC-date set tracked on the bucket so DaysActive is exact.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Thread-safe in-memory implementation of
/// <see cref="IStudentTrialConsumptionStore"/>. Suitable for single-host
/// deployments and the canonical default per the ADR-0042 "InMemory first,
/// Marten second" migration pattern.
/// </summary>
public sealed class InMemoryStudentTrialConsumptionStore : IStudentTrialConsumptionStore
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<StudentTrialConsumption> GetAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            return Task.FromResult(StudentTrialConsumption.Empty);
        }
        if (!_buckets.TryGetValue(studentSubjectIdEncrypted, out var bucket))
        {
            return Task.FromResult(StudentTrialConsumption.Empty);
        }
        lock (bucket.Lock)
        {
            return Task.FromResult(bucket.Snapshot());
        }
    }

    /// <inheritdoc/>
    public Task<StudentTrialConsumption> IncrementAsync(
        string studentSubjectIdEncrypted,
        EntitlementFeature feature,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Student subject id must be non-empty.", nameof(studentSubjectIdEncrypted));
        }
        var bucket = _buckets.GetOrAdd(studentSubjectIdEncrypted, _ => new Bucket());
        lock (bucket.Lock)
        {
            switch (feature)
            {
                case EntitlementFeature.TutorTurn: bucket.TutorTurns++; break;
                case EntitlementFeature.PhotoDiagnostic: bucket.PhotoDiagnostics++; break;
                case EntitlementFeature.PracticeSession: bucket.Sessions++; break;
                case EntitlementFeature.Generic:
                    // Generic does not increment any counter — it only marks
                    // the day as active for funnel analytics.
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(feature), feature,
                        "Unknown EntitlementFeature value.");
            }
            bucket.ActiveDates.Add(DateOnly.FromDateTime(now.UtcDateTime));
            return Task.FromResult(bucket.Snapshot());
        }
    }

    /// <inheritdoc/>
    public Task<CapIncrementResult> IncrementIfUnderCapAsync(
        string studentSubjectIdEncrypted,
        EntitlementFeature feature,
        int cap,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Student subject id must be non-empty.", nameof(studentSubjectIdEncrypted));
        }
        var bucket = _buckets.GetOrAdd(studentSubjectIdEncrypted, _ => new Bucket());
        lock (bucket.Lock)
        {
            // Atomic check-and-increment under the per-key lock. The same
            // lock IncrementAsync / GetAsync take, so concurrent callers
            // cannot slip past the cap during the gap between this method's
            // check and its mutation.
            if (cap > 0)
            {
                var current = feature switch
                {
                    EntitlementFeature.TutorTurn => bucket.TutorTurns,
                    EntitlementFeature.PhotoDiagnostic => bucket.PhotoDiagnostics,
                    EntitlementFeature.PracticeSession => bucket.Sessions,
                    EntitlementFeature.Generic => 0,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(feature), feature, "Unknown EntitlementFeature value."),
                };
                if (current >= cap)
                {
                    return Task.FromResult(new CapIncrementResult(
                        Allowed: false, Snapshot: bucket.Snapshot()));
                }
            }
            switch (feature)
            {
                case EntitlementFeature.TutorTurn: bucket.TutorTurns++; break;
                case EntitlementFeature.PhotoDiagnostic: bucket.PhotoDiagnostics++; break;
                case EntitlementFeature.PracticeSession: bucket.Sessions++; break;
                case EntitlementFeature.Generic: break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(feature), feature, "Unknown EntitlementFeature value.");
            }
            bucket.ActiveDates.Add(DateOnly.FromDateTime(now.UtcDateTime));
            return Task.FromResult(new CapIncrementResult(
                Allowed: true, Snapshot: bucket.Snapshot()));
        }
    }

    /// <inheritdoc/>
    public Task ResetAsync(string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            return Task.CompletedTask;
        }
        if (_buckets.TryGetValue(studentSubjectIdEncrypted, out var bucket))
        {
            lock (bucket.Lock)
            {
                bucket.TutorTurns = 0;
                bucket.PhotoDiagnostics = 0;
                bucket.Sessions = 0;
                bucket.ActiveDates.Clear();
            }
        }
        return Task.CompletedTask;
    }

    private sealed class Bucket
    {
        public object Lock { get; } = new();
        public int TutorTurns;
        public int PhotoDiagnostics;
        public int Sessions;
        public HashSet<DateOnly> ActiveDates { get; } = new();

        public StudentTrialConsumption Snapshot() => new(
            TutorTurnsUsed: TutorTurns,
            PhotoDiagnosticsUsed: PhotoDiagnostics,
            SessionsStarted: Sessions,
            DaysActive: ActiveDates.Count);
    }
}
