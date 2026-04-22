// =============================================================================
// Cena Platform — MartenDiagnosticDisputeRepository (EPIC-PRR-J PRR-385)
//
// Production Marten-backed implementation. Mirrors
// MartenPhotoDiagnosticMonthlyUsage (PRR-400) in style.
// =============================================================================

using Marten;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class MartenDiagnosticDisputeRepository : IDiagnosticDisputeRepository
{
    private readonly IDocumentStore _store;

    public MartenDiagnosticDisputeRepository(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task InsertAsync(DiagnosticDisputeDocument dispute, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dispute);
        if (string.IsNullOrWhiteSpace(dispute.Id))
            throw new ArgumentException("Dispute Id is required.", nameof(dispute));
        await using var session = _store.LightweightSession();
        session.Insert(dispute);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<DiagnosticDisputeDocument?> GetAsync(string disputeId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<DiagnosticDisputeDocument>(disputeId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DiagnosticDisputeDocument>> ListRecentAsync(
        DisputeStatus? status, int take, CancellationToken ct)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        await using var session = _store.QuerySession();
        var q = session.Query<DiagnosticDisputeDocument>();
        IQueryable<DiagnosticDisputeDocument> filtered = status is { } s
            ? q.Where(d => d.Status == s)
            : q;
        var list = await filtered
            .OrderByDescending(d => d.SubmittedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list.ToList();
    }

    public async Task<IReadOnlyList<DiagnosticDisputeDocument>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        await using var session = _store.QuerySession();
        var list = await session.Query<DiagnosticDisputeDocument>()
            .Where(d => d.StudentSubjectIdHash == studentSubjectIdHash)
            .OrderByDescending(d => d.SubmittedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list.ToList();
    }

    public async Task UpdateStatusAsync(
        string disputeId,
        DisputeStatus newStatus,
        DateTimeOffset reviewedAt,
        string? reviewerNote,
        CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var existing = await session.LoadAsync<DiagnosticDisputeDocument>(disputeId, ct).ConfigureAwait(false);
        if (existing is null)
            throw new InvalidOperationException($"Dispute {disputeId} not found.");
        var updated = existing with
        {
            Status = newStatus,
            ReviewedAt = reviewedAt,
            ReviewerNote = reviewerNote,
        };
        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
