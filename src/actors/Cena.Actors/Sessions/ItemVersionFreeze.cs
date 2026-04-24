// =============================================================================
// Cena Platform — Item-Version Freeze (RDY-075 Phase 1A)
//
// Dina's offline-sync rule: when an item is served to the student,
// the (difficulty, CAS verification snapshot, question text, answer
// set) tuple is FROZEN to the version that was served. Later
// re-calibrations or content edits MUST NOT retroactively change
// the grading of an answer the student already submitted.
//
// Without this rule, the following happens on reconnect:
//   1. Student solves item v1 offline, answers "4" (correct vs v1).
//   2. Admin edits item, v2 now grades "5" as correct.
//   3. Student syncs: answer "4" is marked wrong against v2.
//   4. Mastery projection demotes the student for something they got right.
//
// The freeze captures enough of the item state to grade the answer
// exactly as it would have been graded when the item was served.
// Server-side ingest ignores the current item version entirely and
// grades against the frozen snapshot.
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Snapshot of an item at the moment it was served to the student.
/// Travels with every offline answer event and is authoritative for
/// grading on reconnect.
/// </summary>
public sealed record ItemVersionFreeze(
    string ItemId,
    int ItemVersion,
    string QuestionText,
    string CorrectAnswerCanonical,
    double Difficulty,
    double Discrimination,
    string CasSnapshotHash,
    DateTimeOffset FrozenAtUtc)
{
    /// <summary>
    /// True when <paramref name="candidate"/> matches the frozen
    /// canonical answer. Normalises whitespace + trailing dots; case-
    /// insensitive by default. This runs server-side during
    /// idempotent ingest so a stale UI or a re-hydrated service-worker
    /// answer grades the same way regardless of when the replay
    /// happens.
    /// </summary>
    public bool IsAnswerCorrect(string candidate, bool caseSensitive = false)
    {
        if (candidate is null) return false;
        var lhs = Normalise(candidate);
        var rhs = Normalise(CorrectAnswerCanonical);
        return caseSensitive
            ? string.Equals(lhs, rhs, StringComparison.Ordinal)
            : string.Equals(lhs, rhs, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string s)
        => (s ?? string.Empty).Trim().TrimEnd('.').Replace(" ", string.Empty);
}

/// <summary>
/// One answer event recorded offline and queued for sync. Idempotent
/// on <see cref="IdempotencyKey"/> — the server's ingest pipeline
/// ignores any event whose key has already been recorded.
/// </summary>
public sealed record OfflineAnswerEvent(
    string IdempotencyKey,
    string StudentAnonId,
    string SessionId,
    ItemVersionFreeze Freeze,
    string SubmittedAnswer,
    TimeSpan TimeSpent,
    DateTimeOffset AnsweredAtUtc);

/// <summary>
/// Server-side ingest decision: what to do with an offline event on
/// reconnect. <see cref="Duplicate"/> means the idempotency key has
/// been seen before and the event is silently dropped; <see cref="Accept"/>
/// means grade + project; <see cref="Reject"/> indicates the freeze
/// refers to an item the server has never heard of (a corrupted
/// upload or a stale service-worker; event is dead-lettered for
/// investigation).
/// </summary>
public enum OfflineIngestDecision
{
    Accept = 0,
    Duplicate = 1,
    Reject = 2
}

/// <summary>
/// Thin wrapper around a "have we seen this idempotency key?" lookup.
/// Phase 1A ships an in-memory implementation; Phase 1B wires the
/// persistent Marten document that backs it.
/// </summary>
public interface IOfflineSyncLedger
{
    bool HasSeen(string idempotencyKey);
    void MarkSeen(string idempotencyKey, DateTimeOffset atUtc);
}

/// <summary>
/// In-memory ledger for tests + single-host dev. NOT safe to run in
/// a horizontally-scaled API process — use the Marten-backed ledger
/// in production (Phase 1B).
/// </summary>
public sealed class InMemoryOfflineSyncLedger : IOfflineSyncLedger
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new();
    private readonly object _lock = new();

    public bool HasSeen(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_lock) return _seen.ContainsKey(idempotencyKey);
    }

    public void MarkSeen(string idempotencyKey, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_lock) _seen[idempotencyKey] = atUtc;
    }

    /// <summary>Test helper — count of seen keys.</summary>
    public int SeenCount
    {
        get { lock (_lock) return _seen.Count; }
    }
}

/// <summary>
/// Pure ingest decision — looks up the ledger, decides what to do,
/// does NOT mutate anything itself. Callers mark-seen on Accept.
/// </summary>
public static class OfflineSyncIngest
{
    public static OfflineIngestDecision Decide(
        OfflineAnswerEvent ev,
        IOfflineSyncLedger ledger,
        Func<string, bool> itemExistsCheck)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(itemExistsCheck);

        if (ledger.HasSeen(ev.IdempotencyKey))
            return OfflineIngestDecision.Duplicate;

        if (!itemExistsCheck(ev.Freeze.ItemId))
            return OfflineIngestDecision.Reject;

        return OfflineIngestDecision.Accept;
    }
}
