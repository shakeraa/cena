// =============================================================================
// Cena Platform — InMemoryDiagnosticDisputeRepository (EPIC-PRR-J PRR-385)
//
// Process-local implementation for tests + dev. Production binds the
// Marten-backed impl so disputes survive host restarts.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class InMemoryDiagnosticDisputeRepository : IDiagnosticDisputeRepository
{
    private readonly ConcurrentDictionary<string, DiagnosticDisputeDocument> _byId = new();

    public Task InsertAsync(DiagnosticDisputeDocument dispute, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dispute);
        if (string.IsNullOrWhiteSpace(dispute.Id))
            throw new ArgumentException("Dispute Id is required.", nameof(dispute));
        if (!_byId.TryAdd(dispute.Id, dispute))
            throw new InvalidOperationException($"Duplicate dispute id: {dispute.Id}");
        return Task.CompletedTask;
    }

    public Task<DiagnosticDisputeDocument?> GetAsync(string disputeId, CancellationToken ct)
    {
        _byId.TryGetValue(disputeId, out var doc);
        return Task.FromResult<DiagnosticDisputeDocument?>(doc);
    }

    public Task<IReadOnlyList<DiagnosticDisputeDocument>> ListRecentAsync(
        DisputeStatus? status, int take, CancellationToken ct)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var q = _byId.Values.AsEnumerable();
        if (status is { } s) q = q.Where(d => d.Status == s);
        var list = q.OrderByDescending(d => d.SubmittedAt).Take(take).ToList();
        return Task.FromResult<IReadOnlyList<DiagnosticDisputeDocument>>(list);
    }

    public Task<IReadOnlyList<DiagnosticDisputeDocument>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var list = _byId.Values
            .Where(d => d.StudentSubjectIdHash == studentSubjectIdHash)
            .OrderByDescending(d => d.SubmittedAt)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<DiagnosticDisputeDocument>>(list);
    }

    public Task UpdateStatusAsync(
        string disputeId,
        DisputeStatus newStatus,
        DateTimeOffset reviewedAt,
        string? reviewerNote,
        CancellationToken ct)
    {
        if (!_byId.TryGetValue(disputeId, out var existing))
            throw new InvalidOperationException($"Dispute {disputeId} not found.");
        var updated = existing with
        {
            Status = newStatus,
            ReviewedAt = reviewedAt,
            ReviewerNote = reviewerNote,
        };
        _byId[disputeId] = updated;
        return Task.CompletedTask;
    }

    public Task<int> DeleteSubmittedBeforeAsync(DateTimeOffset threshold, CancellationToken ct)
    {
        var toRemove = _byId.Values
            .Where(d => d.SubmittedAt < threshold)
            .Select(d => d.Id)
            .ToList();
        var removed = 0;
        foreach (var id in toRemove)
        {
            if (_byId.TryRemove(id, out _)) removed++;
        }
        return Task.FromResult(removed);
    }

    public Task<int> DeleteByStudentAsync(string studentSubjectIdHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var toRemove = _byId.Values
            .Where(d => d.StudentSubjectIdHash == studentSubjectIdHash)
            .Select(d => d.Id)
            .ToList();
        var removed = 0;
        foreach (var id in toRemove)
        {
            if (_byId.TryRemove(id, out _)) removed++;
        }
        return Task.FromResult(removed);
    }
}
