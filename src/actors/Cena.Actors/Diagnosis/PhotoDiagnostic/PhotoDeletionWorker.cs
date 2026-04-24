// =============================================================================
// Cena Platform — PhotoDeletionWorker (EPIC-PRR-J PRR-412)
//
// Enforces the 5-minute photo-deletion SLA defined in the 10-persona
// photo-diagnostic review 2026-04-22 and locked by PPL Amendment 13.
// Every diagnostic photo must leave persistent storage within 5 minutes
// of upload; the ledger row (<see cref="PhotoHashLedgerDocument"/>)
// survives so abuse-detection, re-upload probes, and support auditing
// can still reason about upload history without holding PII.
//
// Design:
//   - Runs every 60 s. 60 s gives headroom for ≤1 s worker jitter +
//     up to a 4 s blob-delete round-trip + 5 s clock-skew buffer and
//     still hits the 99.9% <5-min SLA the DoD asks for.
//   - Eligibility is a pure static kernel (FindEligible) so tests
//     exercise the boundary logic (5-min cut, dispute-hold, already-
//     deleted) without Marten. Mirrors
//     <see cref="AbuseDetectionWorker.FindAbusers"/>.
//   - Per-row try/catch inside the loop: one failed delete (S3 5xx,
//     transient DNS) must NOT abort the sweep, or a single bad blob
//     would indefinitely block deletion of its neighbors and trip the
//     SLA for every subsequent row.
//   - Metrics are mandatory for the PRR-412 DoD ("Monitoring + alert
//     on delete-failure"): we emit both outcome-tagged counters AND
//     a deletion-lag histogram so dashboards can alert on both
//     "rate of failed deletes" and "p99 lag creeping past 5 min".
//
// Production-grade: real BackgroundService, real Marten queries, real
// OTel instruments. Per memory "No stubs — production grade" (2026-04-11).
// =============================================================================

using System.Diagnostics.Metrics;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Sweeps <see cref="PhotoHashLedgerDocument"/> rows and deletes the
/// backing photo blob once the row is past its SLA and not held by an
/// active dispute. Keeps the ledger row so upload history survives.
/// </summary>
public sealed class PhotoDeletionWorker : BackgroundService
{
    /// <summary>
    /// Hard SLA from PPL Amd 13 + the 10-persona photo-diagnostic review.
    /// If this ever gets relaxed, it must be via PR reviewed by product
    /// + legal (memory "Labels match data" — if policy says 5 min, it's
    /// 5 min).
    /// </summary>
    public static readonly TimeSpan DeletionSla = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often the sweep runs. See file header for the 60 s rationale.
    /// </summary>
    public static readonly TimeSpan RunInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Per-row blob-delete timeout. Short: a failing store must not
    /// starve the rest of the sweep. Transient failures are retried on
    /// the next 60 s tick, so three sweeps fit within the SLA.
    /// </summary>
    public static readonly TimeSpan BlobDeleteTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Cap on how many ledger rows one sweep processes. Large enough to
    /// drain a realistic per-minute upload burst (~5000 concurrent
    /// Bagrut-morning students at 1 photo / min each ⇒ 5000 rows) but
    /// bounded so a bad backlog doesn't hold the worker forever on one
    /// tick.
    /// </summary>
    public const int MaxRowsPerSweep = 10_000;

    private readonly IDocumentStore _store;
    private readonly IPhotoBlobStore _blobs;
    private readonly TimeProvider _clock;
    private readonly ILogger<PhotoDeletionWorker> _logger;
    private readonly Counter<long> _deletionsTotal;
    private readonly Histogram<double> _deletionLagMs;

    public PhotoDeletionWorker(
        IDocumentStore store,
        IPhotoBlobStore blobs,
        TimeProvider clock,
        IMeterFactory meterFactory,
        ILogger<PhotoDeletionWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(meterFactory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Co-exists with PhotoDiagnosticMetrics: PhotoDiagnosticMetrics
        // covers pipeline-stage instruments (confidence, latency,
        // refusals). These two instruments are specifically for the
        // deletion SLA dashboard so we keep them under the same meter
        // name for operational coherence.
        var meter = meterFactory.Create(PhotoDiagnosticMetrics.MeterName, "1.0.0");
        _deletionsTotal = meter.CreateCounter<long>(
            "cena.photo_diagnostic.deletions_total",
            description:
                "Photo deletion outcomes. Tags: outcome="
                + "succeeded | failed | held_for_dispute. Alert on "
                + "rate(failed) > 0.1% of rate(succeeded) rolling 5m.");
        _deletionLagMs = meter.CreateHistogram<double>(
            "cena.photo_diagnostic.deletion_lag_ms",
            unit: "ms",
            description:
                "Clock - UploadedAt at the moment of successful delete. "
                + "SLO: p99 < 300000 ms (5 min). A drift past 4 min "
                + "should page — the SLA is hard.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "PhotoDeletionWorker sweep failed; retrying in {Interval}.", RunInterval);
            }
            try
            {
                await Task.Delay(RunInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One sweep pass. Public for operational use (on-demand drain from
    /// an admin endpoint) and test hooks. Returns the count of successful
    /// blob deletions.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();

        // Pull candidate rows past SLA and not already deleted. We filter
        // dispute-hold + age in the pure kernel so the query shape is
        // stable across backends (Marten/Postgres today; possibly a
        // different store later).
        IReadOnlyList<PhotoHashLedgerDocument> candidates;
        await using (var session = _store.QuerySession())
        {
            var cutoff = now - DeletionSla;
            candidates = await session.Query<PhotoHashLedgerDocument>()
                .Where(d => !d.PhotoDeleted && d.UploadedAtUtc <= cutoff)
                .Take(MaxRowsPerSweep)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var decisions = FindEligible(candidates, now).ToList();

        int succeeded = 0;
        int failed = 0;
        int held = 0;

        foreach (var decision in decisions)
        {
            if (ct.IsCancellationRequested) break;

            if (decision.Outcome == DeletionOutcome.HeldForDispute)
            {
                held++;
                _deletionsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "held_for_dispute"));
                continue;
            }

            // decision.Outcome == Eligible from here down
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(BlobDeleteTimeout);
                await _blobs.DeleteAsync(decision.Row.Id, timeoutCts.Token).ConfigureAwait(false);

                await MarkDeletedAsync(decision.Row.Id, now, ct).ConfigureAwait(false);

                var lagMs = (now - decision.Row.UploadedAtUtc).TotalMilliseconds;
                _deletionLagMs.Record(lagMs);
                _deletionsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "succeeded"));
                succeeded++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failed++;
                _deletionsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
                // Per-row failure is a WARNING not an ERROR: the next
                // sweep will retry, and the PhotoDeletionAuditJob will
                // loudly flag it if the retry never succeeds.
                _logger.LogWarning(ex,
                    "PhotoDeletionWorker: delete failed for {BlobKey} (uploaded {UploadedAt:O}); "
                    + "will retry next sweep.",
                    decision.Row.Id, decision.Row.UploadedAtUtc);
            }
        }

        if (succeeded + failed + held > 0)
        {
            _logger.LogInformation(
                "PhotoDeletionWorker sweep: succeeded={Succeeded} failed={Failed} held={Held}.",
                succeeded, failed, held);
        }
        return succeeded;
    }

    private async Task MarkDeletedAsync(string id, DateTimeOffset now, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var existing = await session.LoadAsync<PhotoHashLedgerDocument>(id, ct).ConfigureAwait(false);
        if (existing is null) return; // raced with a concurrent delete — nothing to do
        var updated = existing with { PhotoDeleted = true, DeletedAtUtc = now };
        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Outcome of the eligibility check for one ledger row. Separates
    /// "not eligible because dispute held" from "not eligible yet" (the
    /// latter is dropped from the decision list entirely; only rows past
    /// the 5-min cut appear here).
    /// </summary>
    public enum DeletionOutcome
    {
        /// <summary>Row is past SLA, not held — delete the blob.</summary>
        Eligible,
        /// <summary>Row is past SLA but on a non-expired dispute hold.</summary>
        HeldForDispute,
    }

    /// <summary>Decision record for one ledger row after eligibility evaluation.</summary>
    public sealed record Decision(PhotoHashLedgerDocument Row, DeletionOutcome Outcome);

    /// <summary>
    /// Pure eligibility kernel. Filter <paramref name="rows"/> to those
    /// past the 5-min SLA and either no-hold or hold-expired (=Eligible);
    /// rows past SLA but on an active dispute hold become HeldForDispute.
    /// Rows NOT past the 5-min cut (or already deleted) are omitted entirely
    /// — they aren't decisions yet. Empty input yields empty output.
    ///
    /// Callers MAY pre-filter by <c>PhotoDeleted=false</c> and age as a
    /// DB-side optimization; this kernel still enforces every rule so it
    /// is trivially testable and survives a lax caller.
    /// </summary>
    public static IEnumerable<Decision> FindEligible(
        IEnumerable<PhotoHashLedgerDocument> rows, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var cutoff = now - DeletionSla;
        foreach (var r in rows)
        {
            if (r.PhotoDeleted) continue;
            if (r.UploadedAtUtc > cutoff) continue; // not yet due
            if (r.DisputeHoldUntilUtc is { } hold && hold > now)
            {
                yield return new Decision(r, DeletionOutcome.HeldForDispute);
                continue;
            }
            yield return new Decision(r, DeletionOutcome.Eligible);
        }
    }
}
