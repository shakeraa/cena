// =============================================================================
// Cena Platform — Marten-backed Offline Sync Ledger (RDY-075 Phase 1B)
//
// Production implementation of IOfflineSyncLedger. Persists seen
// idempotency keys as Marten documents with a 60-day TTL so a
// re-submission beyond that window is indistinguishable from a fresh
// answer (intentional — a student who returns three months later
// should not have their cached answer suddenly dropped as a duplicate).
// =============================================================================

using Marten;
using Marten.Schema;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Sessions;

/// <summary>
/// Marten document backing the ledger. Document id IS the idempotency
/// key so HasSeen() is a point lookup (no scan) and MarkSeen() is a
/// single upsert. SeenAtUtc is informational; the TTL is enforced by
/// the retention worker (ADR-0003 retention schedule) not by Marten
/// directly — we record SeenAtUtc so the worker can age rows out.
/// </summary>
public sealed class OfflineSyncLedgerDocument
{
    [Identity]
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset SeenAtUtc { get; set; }

    public string? StudentAnonId { get; set; }
    public string? SessionId { get; set; }
}

/// <summary>
/// Marten-backed production ledger. Thread-safe via Marten session.
/// Each HasSeen()/MarkSeen() call opens a light-weight query session
/// and disposes it — cheap relative to an HTTP round trip.
/// </summary>
public sealed class MartenOfflineSyncLedger : IOfflineSyncLedger
{
    private readonly IDocumentStore _store;
    private readonly ILogger<MartenOfflineSyncLedger> _logger;

    public MartenOfflineSyncLedger(
        IDocumentStore store,
        ILogger<MartenOfflineSyncLedger> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    public bool HasSeen(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        using var session = _store.QuerySession();
        // Marten ≥ 7 moved synchronous Load<T> off IQuerySession; call
        // the async counterpart and block. See note on SaveChangesAsync
        // in MarkSeen for why the sync blocking is acceptable here.
        return session.LoadAsync<OfflineSyncLedgerDocument>(idempotencyKey)
            .GetAwaiter()
            .GetResult() is not null;
    }

    public void MarkSeen(string idempotencyKey, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        using var session = _store.LightweightSession();
        session.Store(new OfflineSyncLedgerDocument
        {
            Id = idempotencyKey,
            SeenAtUtc = atUtc,
        });
        try
        {
            // Marten ≥ 7 dropped the sync SaveChanges on IDocumentSession
            // in favour of SaveChangesAsync; call it synchronously here
            // because the ILedger contract is sync (the caller is the
            // HTTP endpoint, which already has a fully-async path above
            // and isn't blocked by this one-doc write).
            session.SaveChangesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // MarkSeen MUST NOT throw on the hot path — a write failure
            // here is better than breaking the sync. The consequence of
            // a missed mark is that the caller might re-accept the same
            // idempotency key; downstream projections are idempotent on
            // the event id so the worst case is a duplicate event that
            // the projection replay dedupes.
            _logger.LogWarning(
                ex,
                "[OFFLINE_SYNC] MarkSeen failed for key={Key}; next replay may re-accept",
                idempotencyKey);
        }
    }

    /// <summary>
    /// Test + diagnostic helper. Returns the count of seen keys. NOT
    /// for production use — it runs a full scan.
    /// </summary>
    public async Task<long> CountSeenAsync(CancellationToken ct = default)
    {
        using var session = _store.QuerySession();
        return await session.Query<OfflineSyncLedgerDocument>().CountAsync(ct);
    }
}
