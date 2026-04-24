// =============================================================================
// Cena Platform — ImageSimilarityGate (EPIC-PRR-J PRR-404)
//
// Gate that rejects near-identical photo re-uploads within a tight window.
// The anti-gaming pattern this blocks is concrete: a student photographs
// their solution, gets a diagnosis they dislike, then re-uploads the same
// page (same handwriting, same ink, same page) hoping the Vision+CAS chain
// will return a kinder answer. Unchecked, this burns the monthly photo
// quota, inflates vendor costs, and teaches students that the system is
// gameable.
//
// Policy (tunable via ImageSimilarityOptions, defaults documented here):
//   * Window = 5 minutes. Shorter than the monthly quota reset but long
//     enough to catch intentional rapid re-uploads. A student who genuinely
//     does a second attempt with new work will almost always be > 5 min
//     because producing new handwritten working takes longer than that.
//   * Hamming distance threshold = 10 (out of 64). Empirically, aHash
//     distances for re-uploads of the same page (rotated slightly, cropped
//     differently, re-JPEGed) sit at 0–8, while genuinely different pages
//     sit at 16+. Ten is the conservative midpoint — tight enough that a
//     "new page of working on the same problem" is accepted, loose enough
//     that a recompressed/rotated re-shoot is caught.
//
// What this gate does NOT do:
//   * It does not parse the image (intake pipeline already decoded it).
//   * It does not persist anything by itself — persistence is via the
//     IRecentPhotoHashStore port. Production will bind the Marten-backed
//     implementation that shares the hash ledger with PRR-412 (photo
//     deletion SLA); this task ships the algorithm + gate + in-memory
//     fixture. The Marten store is deferred to PRR-412 coordination so we
//     don't double-author the storage schema — PRR-412 already introduces
//     the hash ledger, and this gate will read from it.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Tunable thresholds for the similarity gate.</summary>
/// <param name="Window">Time window to consider "recent" (default 5 min).</param>
/// <param name="MaxHammingDistance">
/// Inclusive Hamming-distance threshold below which an upload is treated as
/// a near-duplicate of a prior upload. Default 10 out of 64 bits.
/// </param>
public sealed record ImageSimilarityOptions(TimeSpan Window, int MaxHammingDistance)
{
    /// <summary>Defaults: 5 minutes, Hamming ≤ 10.</summary>
    public static ImageSimilarityOptions Default { get; } =
        new(TimeSpan.FromMinutes(5), MaxHammingDistance: 10);
}

/// <summary>Three-state outcome mirroring the quota-gate decision shape.</summary>
public enum ImageSimilarityOutcome
{
    /// <summary>No recent near-duplicate — upload proceeds.</summary>
    Accept = 0,

    /// <summary>A recent upload is visually near-identical — reject.</summary>
    RejectNearDuplicate = 1,
}

/// <summary>Details of the prior upload that triggered a rejection.</summary>
public sealed record RecentPhotoHashMatch(DateTimeOffset PriorUploadedAt, int HammingDistance);

/// <summary>
/// Decision returned to the caller. RecentMatch is non-null iff
/// Outcome == RejectNearDuplicate.
/// </summary>
public sealed record ImageSimilarityDecision(
    ImageSimilarityOutcome Outcome,
    RecentPhotoHashMatch? RecentMatch);

/// <summary>
/// Persistence port for the recent-photo-hash ledger. Implementations must
/// be safe under concurrent access.
/// </summary>
public interface IRecentPhotoHashStore
{
    /// <summary>Record a freshly-accepted upload's hash.</summary>
    Task RecordAsync(string studentSubjectIdHash, ulong pHash, DateTimeOffset uploadedAt, CancellationToken ct);

    /// <summary>
    /// Enumerate hashes recorded for this student at or after
    /// <paramref name="since"/>. Implementations are free to evict older
    /// rows; callers must not rely on anything pre-<paramref name="since"/>.
    /// </summary>
    Task<IReadOnlyList<(ulong PHash, DateTimeOffset UploadedAt)>> RecentHashesAsync(
        string studentSubjectIdHash, DateTimeOffset since, CancellationToken ct);
}

/// <summary>Port for the gate itself.</summary>
public interface IImageSimilarityGate
{
    /// <summary>
    /// Check the pHash against this student's recent uploads. Emits
    /// <see cref="ImageSimilarityOutcome.Accept"/> for first uploads and
    /// for uploads that are far enough from every recent sibling.
    /// On Accept, the gate records the new hash before returning, so
    /// the very next call within the window sees it.
    /// </summary>
    Task<ImageSimilarityDecision> EvaluateAsync(
        string studentSubjectIdHash, ulong pHash, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Default implementation.</summary>
public sealed class ImageSimilarityGate : IImageSimilarityGate
{
    private readonly IRecentPhotoHashStore _store;
    private readonly ImageSimilarityOptions _options;

    public ImageSimilarityGate(IRecentPhotoHashStore store, ImageSimilarityOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? ImageSimilarityOptions.Default;
        if (_options.MaxHammingDistance < 0 || _options.MaxHammingDistance > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MaxHammingDistance must be in [0, 64]; got {_options.MaxHammingDistance}.");
        }
        if (_options.Window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), $"Window must be positive; got {_options.Window}.");
        }
    }

    public async Task<ImageSimilarityDecision> EvaluateAsync(
        string studentSubjectIdHash, ulong pHash, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
        {
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        }

        var since = now - _options.Window;
        var recent = await _store.RecentHashesAsync(studentSubjectIdHash, since, ct).ConfigureAwait(false);

        // Find the closest recent hash. We scan all (window is small, N is
        // small) and pick the minimum distance — that gives the caller the
        // most informative rejection metadata when multiple recent uploads
        // are close.
        RecentPhotoHashMatch? closest = null;
        foreach (var (priorHash, priorTime) in recent)
        {
            // Defence in depth: the store contract says only entries newer
            // than `since`, but we re-filter in case a test double returns
            // older rows.
            if (priorTime < since) continue;

            int distance = ImagePerceptualHash.HammingDistance(pHash, priorHash);
            if (distance <= _options.MaxHammingDistance &&
                (closest is null || distance < closest.HammingDistance))
            {
                closest = new RecentPhotoHashMatch(priorTime, distance);
            }
        }

        if (closest is not null)
        {
            // Rejection path: do NOT record the hash — rejected uploads
            // must not poison the ledger (and would be immediately
            // redundant anyway).
            return new ImageSimilarityDecision(ImageSimilarityOutcome.RejectNearDuplicate, closest);
        }

        await _store.RecordAsync(studentSubjectIdHash, pHash, now, ct).ConfigureAwait(false);
        return new ImageSimilarityDecision(ImageSimilarityOutcome.Accept, RecentMatch: null);
    }
}

/// <summary>
/// DEV / TEST ONLY — in-process, TTL-swept recent-hash ledger. Production
/// MUST bind a durable store (planned: Marten-backed shared ledger with
/// PRR-412 photo deletion SLA). This class is deliberately allocated in
/// the main assembly (not a test project) so the InMemory DI composition
/// mode can register it, mirroring the SandboxPaymentGateway idiom.
/// NEVER register this in Production.
/// </summary>
public sealed class InMemoryRecentPhotoHashStore : IRecentPhotoHashStore
{
    private readonly ConcurrentDictionary<string, List<Entry>> _byStudent = new();
    private readonly TimeSpan _retention;

    /// <summary>
    /// Create a new in-memory store. <paramref name="retention"/> bounds how
    /// long entries survive before the lazy sweep evicts them; defaults to
    /// 1 hour (well past the 5-minute similarity window, so evictions never
    /// race with a live decision).
    /// </summary>
    public InMemoryRecentPhotoHashStore(TimeSpan? retention = null)
    {
        _retention = retention ?? TimeSpan.FromHours(1);
        if (_retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention), $"retention must be positive; got {_retention}.");
        }
    }

    private readonly record struct Entry(ulong PHash, DateTimeOffset UploadedAt);

    public Task RecordAsync(string studentSubjectIdHash, ulong pHash, DateTimeOffset uploadedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
        {
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        }

        var list = _byStudent.GetOrAdd(studentSubjectIdHash, static _ => new List<Entry>());
        lock (list)
        {
            SweepLocked(list, uploadedAt);
            list.Add(new Entry(pHash, uploadedAt));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(ulong PHash, DateTimeOffset UploadedAt)>> RecentHashesAsync(
        string studentSubjectIdHash, DateTimeOffset since, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
        {
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        }

        if (!_byStudent.TryGetValue(studentSubjectIdHash, out var list))
        {
            return Task.FromResult<IReadOnlyList<(ulong, DateTimeOffset)>>(Array.Empty<(ulong, DateTimeOffset)>());
        }

        List<(ulong, DateTimeOffset)> snapshot;
        lock (list)
        {
            SweepLocked(list, since);
            snapshot = new List<(ulong, DateTimeOffset)>(list.Count);
            foreach (var entry in list)
            {
                if (entry.UploadedAt >= since)
                {
                    snapshot.Add((entry.PHash, entry.UploadedAt));
                }
            }
        }
        return Task.FromResult<IReadOnlyList<(ulong, DateTimeOffset)>>(snapshot);
    }

    private void SweepLocked(List<Entry> list, DateTimeOffset now)
    {
        var cutoff = now - _retention;
        // list stays small (N = uploads-per-hour per student, O(10)), so
        // we don't need a heap — linear sweep is fine and branch-free.
        int write = 0;
        for (int read = 0; read < list.Count; read++)
        {
            if (list[read].UploadedAt >= cutoff)
            {
                if (write != read) list[write] = list[read];
                write++;
            }
        }
        if (write < list.Count)
        {
            list.RemoveRange(write, list.Count - write);
        }
    }
}
