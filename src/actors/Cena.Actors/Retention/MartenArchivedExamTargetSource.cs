// =============================================================================
// Cena Platform — MartenArchivedExamTargetSource (prr-229 prod binding)
//
// Production Marten-backed implementation of IArchivedExamTargetSource.
// Replaces InMemoryArchivedExamTargetSource as the production DI binding
// per memory "No stubs — production grade" (2026-04-11).
//
// *** CORRECTNESS MOTIVATION ***
// The in-memory source is a ConcurrentDictionary populated only by test
// harnesses (no production Append caller exists — verified by grep). In
// production the dictionary is perpetually empty, so ListArchivedAsync
// yields zero rows, so the retention worker never shreds anything. The
// ADR-0050 §6 24-month post-archive retention window is therefore NOT
// enforced in production today. This commit closes that gap by querying
// the canonical source of truth: the StudentPlan event store itself.
//
// Approach:
//   1. Query Marten for every ExamTargetArchived_V1 event across all
//      streams via QueryRawEventDataOnly<T>(). This scans the event log
//      but only fetches archive events (tiny fraction of total volume).
//   2. Group by StudentAnonId to discover unique affected streams.
//   3. For each affected stream, replay via IStudentPlanAggregateStore to
//      get the current fold (tells us which targets are STILL archived
//      vs completed vs re-activated per §6 edge cases, and maps the
//      StudentPlan ExamTargetId → ExamCode → ExamTargetCode that the
//      mastery + RTBF subsystems key on).
//   4. Filter out rows whose (studentAnonId, examTargetCode) key is
//      already in the shred ledger so each sweep progresses forward
//      instead of re-processing shredded targets.
//
// Monotonic sweep progress: MarkShreddedAsync writes an
// ExamTargetShredLedgerDocument; ListArchivedAsync reads the ledger and
// filters. The ledger survives pod restarts so the retention worker
// never loops on a shredded target even after a deploy.
// =============================================================================

using System.Runtime.CompilerServices;
using Cena.Actors.ExamTargets;
using Cena.Actors.StudentPlan;
using Marten;

namespace Cena.Actors.Retention;

/// <summary>
/// Marten-backed archived-target source. Reads canonical archive state
/// from the StudentPlan event store + filters via the shred ledger.
/// </summary>
public sealed class MartenArchivedExamTargetSource : IArchivedExamTargetSource
{
    private readonly IDocumentStore _store;
    private readonly IStudentPlanAggregateStore _planStore;

    /// <summary>Construct with the document store and the plan-aggregate store.</summary>
    public MartenArchivedExamTargetSource(
        IDocumentStore store,
        IStudentPlanAggregateStore planStore)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ArchivedExamTargetRow> ListArchivedAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Discover every student who ever archived a target. Scans
        //    archive events only (small relative to total event volume);
        //    Distinct() collapses multi-target archivals per student.
        await using var session = _store.QuerySession();
        var archiveEvents = await session.Events
            .QueryRawEventDataOnly<Cena.Actors.StudentPlan.Events.ExamTargetArchived_V1>()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var affectedStudents = archiveEvents
            .Select(e => e.StudentAnonId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (affectedStudents.Count == 0)
        {
            yield break;
        }

        // 2. Load the shred ledger once per sweep. At N archived targets
        //    shredded over the system's lifetime this document table is
        //    small (bounded by total-shreds, which is capped per sweep),
        //    so a single scan is cheap and monotonically progressing.
        var ledger = await session.Query<ExamTargetShredLedgerDocument>()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var shreddedKeys = new HashSet<string>(
            ledger.Select(d => d.Id),
            StringComparer.Ordinal);

        // 3. Replay each affected student's aggregate and yield every
        //    still-archived target. Replay (vs trusting the raw archive
        //    event's ArchivedAt) accounts for the §6 edge case where a
        //    target was archived, then un-archived, then re-archived —
        //    the aggregate fold carries the FINAL ArchivedAt, whereas
        //    a raw event stream would yield stale rows.
        foreach (var studentAnonId in affectedStudents)
        {
            if (ct.IsCancellationRequested) yield break;

            var aggregate = await _planStore
                .LoadAsync(studentAnonId, ct)
                .ConfigureAwait(false);

            foreach (var target in aggregate.State.Targets)
            {
                if (target.ArchivedAt is null) continue;

                // Map StudentPlan's ExamCode (catalog enum, uppercase)
                // to the mastery / RTBF ExamTargetCode (normalised,
                // lowercase-hyphen). Parse normalises so downstream
                // equality holds regardless of call site.
                var code = ExamTargetCode.Parse(target.ExamCode.Value);
                var ledgerKey = LedgerKey(studentAnonId, code);
                if (shreddedKeys.Contains(ledgerKey)) continue;

                yield return new ArchivedExamTargetRow(
                    StudentAnonId: studentAnonId,
                    ExamTargetCode: code,
                    ArchivedAtUtc: target.ArchivedAt.Value,
                    // TenantId not carried on the StudentPlan aggregate
                    // today. The retention worker tolerates null here
                    // and the ADR-0050 §9 cross-tenant invariant is
                    // separately enforced by the erasure cascade.
                    TenantId: null);
            }
        }
    }

    /// <inheritdoc />
    public async Task MarkShreddedAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        DateTimeOffset shreddedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.", nameof(studentAnonId));
        }

        await using var session = _store.LightweightSession();
        session.Store(new ExamTargetShredLedgerDocument
        {
            Id = LedgerKey(studentAnonId, examTargetCode),
            ShreddedAtUtc = shreddedAtUtc,
        });
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string LedgerKey(string studentAnonId, ExamTargetCode code)
        => studentAnonId + "|" + code.Value;
}
