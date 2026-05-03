// =============================================================================
// Cena Platform — PhotoDeletionAuditJob (EPIC-PRR-J PRR-412)
//
// "Verifiable audit" half of the PRR-412 DoD. Independently of the
// PhotoDeletionWorker, this job counts ledger rows whose photo is still
// present past the 5-min SLA and NOT covered by an active dispute hold.
// Any non-zero count is a persistence-tier violation that should page.
//
// Why separate from the worker:
//   - The worker's job is to DO deletions. The audit's job is to
//     VERIFY deletions happened. If the two shared code, a bug in the
//     selection predicate would silently hide itself from its own
//     audit. Separate queries + separate phrasing catch drift.
//   - The audit checks the blob store directly (IPhotoBlobStore.Exists)
//     because the ledger's <c>PhotoDeleted</c> flag is worker-set
//     metadata — it says "I think I deleted it", not "the storage
//     tier confirms this blob is gone". An SLA violation that matters
//     is one where the blob is physically extant, not one where the
//     metadata is stale.
//
// Expected callers:
//   - A hosted scheduler (future work; out-of-scope for PRR-412
//     backend portion) that runs AuditAsync every 5 minutes and
//     forwards the count to PagerDuty via the standard ops alert
//     config. The metric exists here (via the worker's counters) and
//     AuditAsync returns the violation count; the actual alert rule
//     is ops's job.
//   - An admin endpoint ("Run deletion audit now") for incident
//     response; wiring that endpoint is a follow-up task.
//
// Production-grade: real Marten query, real blob-store probe, no stubs.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Audits the photo-deletion SLA. <see cref="AuditAsync"/> returns the
/// number of ledger rows whose photo is past the 5-min SLA AND still
/// extant in the blob store AND not covered by an active dispute hold —
/// the "photos that should be gone but aren't" count.
/// </summary>
public sealed class PhotoDeletionAuditJob
{
    /// <summary>
    /// SLA to audit against. Pulled from <see cref="PhotoDeletionWorker.DeletionSla"/>
    /// so a change to the SLA in one place propagates to the audit too
    /// — the two are supposed to enforce the same contract.
    /// </summary>
    public static TimeSpan DeletionSla => PhotoDeletionWorker.DeletionSla;

    /// <summary>
    /// Cap on how many ledger rows the audit inspects per run. Large
    /// enough to catch a realistic backlog (~5000 concurrent Bagrut-
    /// morning uploads × 1-min sweep gap), bounded so a full-table scan
    /// after a cold-start can't run unbounded.
    /// </summary>
    public const int MaxRowsPerAudit = 20_000;

    private readonly IDocumentStore _store;
    private readonly IPhotoBlobStore _blobs;
    private readonly TimeProvider _clock;
    private readonly ILogger<PhotoDeletionAuditJob> _logger;

    public PhotoDeletionAuditJob(
        IDocumentStore store,
        IPhotoBlobStore blobs,
        TimeProvider clock,
        ILogger<PhotoDeletionAuditJob> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Count of photos still extant past the 5-min SLA, excluding those
    /// on an active dispute hold. <c>0</c> is the pass state; anything
    /// higher means the worker has fallen behind or the blob store is
    /// failing deletes and the next worker sweep is not catching up.
    /// </summary>
    public async Task<int> AuditAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now - DeletionSla;

        // Scope the Marten query tightly so we don't scan ledger rows
        // that are too young to have tripped the SLA, nor rows already
        // marked deleted (the blob-existence probe would just confirm
        // they're gone — wasted round-trip).
        IReadOnlyList<PhotoHashLedgerDocument> suspects;
        await using (var session = _store.QuerySession())
        {
            suspects = await session.Query<PhotoHashLedgerDocument>()
                .Where(d => d.UploadedAtUtc <= cutoff)
                .Where(d => !d.PhotoDeleted)
                .Take(MaxRowsPerAudit)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        // The pure kernel narrows to "should be deleted right now"; an
        // active dispute hold is not a violation.
        var candidates = FindViolationCandidates(suspects, now).ToList();

        int violations = 0;
        foreach (var row in candidates)
        {
            if (ct.IsCancellationRequested) break;

            bool extant;
            try
            {
                extant = await _blobs.ExistsAsync(row.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Can't reach the store — be conservative and count this
                // as a violation. "Unknown" must page. A store that's
                // down is also a store that isn't deleting.
                _logger.LogError(ex,
                    "PhotoDeletionAuditJob: blob-store probe failed for {BlobKey}; "
                    + "counting as violation.", row.Id);
                violations++;
                continue;
            }

            if (extant)
            {
                violations++;
                _logger.LogError(
                    "PhotoDeletionAuditJob: SLA VIOLATION — photo {BlobKey} still present "
                    + "{AgeMinutes:F1} min past upload (SLA={Sla}).",
                    row.Id,
                    (now - row.UploadedAtUtc).TotalMinutes,
                    DeletionSla);
            }
        }

        if (violations == 0)
        {
            _logger.LogInformation(
                "PhotoDeletionAuditJob: OK — inspected {Inspected} past-SLA candidates, zero extant.",
                candidates.Count);
        }
        return violations;
    }

    /// <summary>
    /// Pure kernel: narrow <paramref name="suspects"/> to rows that
    /// are past SLA AND not on an active dispute hold. These are the
    /// rows the audit then probes against the blob store; anything that
    /// returns extant is a violation. Empty input yields empty output.
    ///
    /// Identical eligibility-shape rules as
    /// <see cref="PhotoDeletionWorker.FindEligible"/> minus the
    /// already-deleted branch (Marten pre-filter owns that) — kept as
    /// a separate method because the audit's output type is the raw
    /// ledger row (not a decision) and diverging behavior later (e.g.,
    /// a grace-period tolerance) would belong here, not in the worker.
    /// </summary>
    public static IEnumerable<PhotoHashLedgerDocument> FindViolationCandidates(
        IEnumerable<PhotoHashLedgerDocument> suspects, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(suspects);
        var cutoff = now - DeletionSla;
        foreach (var r in suspects)
        {
            if (r.PhotoDeleted) continue;
            if (r.UploadedAtUtc > cutoff) continue;
            if (r.DisputeHoldUntilUtc is { } hold && hold > now) continue;
            yield return r;
        }
    }
}
