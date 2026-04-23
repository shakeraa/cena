// =============================================================================
// Cena Platform — InMemoryDiagnosticCreditLedger (EPIC-PRR-J PRR-391)
//
// Process-local implementation for tests + dev. Production binds the
// Marten-backed impl so credit history survives host restarts. Thread-
// safe via ConcurrentDictionary, and queries snapshot the values
// collection so listing can never observe a partially-mutated view.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class InMemoryDiagnosticCreditLedger : IDiagnosticCreditLedger
{
    private readonly ConcurrentDictionary<string, DiagnosticCreditLedgerDocument> _byId = new();

    public Task IssueAsync(DiagnosticCreditLedgerDocument credit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credit);
        if (string.IsNullOrWhiteSpace(credit.Id))
            throw new ArgumentException("Credit Id is required.", nameof(credit));
        if (string.IsNullOrWhiteSpace(credit.DisputeId))
            throw new ArgumentException("DisputeId is required.", nameof(credit));
        if (!_byId.TryAdd(credit.Id, credit))
            throw new InvalidOperationException($"Duplicate credit id: {credit.Id}");
        return Task.CompletedTask;
    }

    public Task<DiagnosticCreditLedgerDocument?> GetByDisputeAsync(string disputeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
            throw new ArgumentException("disputeId is required.", nameof(disputeId));
        // Expected cardinality = 0 or 1 (enforced at service layer); but
        // tolerate the general case by returning the first match.
        var hit = _byId.Values.FirstOrDefault(c => c.DisputeId == disputeId);
        return Task.FromResult<DiagnosticCreditLedgerDocument?>(hit);
    }

    public Task<IReadOnlyList<DiagnosticCreditLedgerDocument>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var list = _byId.Values
            .Where(c => c.StudentSubjectIdHash == studentSubjectIdHash)
            .OrderByDescending(c => c.IssuedAtUtc)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<DiagnosticCreditLedgerDocument>>(list);
    }

    public Task<int> CountFreeCreditsAsync(
        string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var month = MonthlyUsageKey.For(asOfUtc);
        var total = _byId.Values
            .Where(c =>
                c.StudentSubjectIdHash == studentSubjectIdHash
                && c.CreditKind == DiagnosticCreditKind.FreeUploadQuota
                && MonthlyUsageKey.For(c.IssuedAtUtc) == month)
            .Sum(c => c.UploadQuotaBumpCount);
        return Task.FromResult(total);
    }
}
