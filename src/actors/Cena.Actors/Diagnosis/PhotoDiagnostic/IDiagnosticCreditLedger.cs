// =============================================================================
// Cena Platform — IDiagnosticCreditLedger (EPIC-PRR-J PRR-391)
//
// Port for the credit-ledger aggregate. Two backends mirror the dispute
// repository pattern:
//   - MartenDiagnosticCreditLedger (production)
//   - InMemoryDiagnosticCreditLedger (dev + unit tests)
//
// The ledger is append-only: IssueAsync only inserts new rows; we never
// update or delete an already-issued credit. If a credit is later judged
// erroneous the correct flow is a compensating row (future work), not
// mutating history.
//
// Monthly credit-count is the read path used by PhotoDiagnosticQuotaGate
// to compute effectiveUsage = rawCount - creditsInMonth. That query is
// hot on the intake path, so Marten's Index(x => x.StudentSubjectIdHash)
// is set in MartenDiagnosticCreditLedger.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface IDiagnosticCreditLedger
{
    /// <summary>Insert a new credit row. Throws on duplicate <see cref="DiagnosticCreditLedgerDocument.Id"/>.</summary>
    Task IssueAsync(DiagnosticCreditLedgerDocument credit, CancellationToken ct);

    /// <summary>
    /// Fetch the credit issued for a specific dispute, or null if none.
    /// Uniqueness is enforced at the service layer (one credit per dispute)
    /// so this returns a single row, not a list.
    /// </summary>
    Task<DiagnosticCreditLedgerDocument?> GetByDisputeAsync(string disputeId, CancellationToken ct);

    /// <summary>Most-recent first. Used by the admin dashboard's per-student audit view.</summary>
    Task<IReadOnlyList<DiagnosticCreditLedgerDocument>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct);

    /// <summary>
    /// Sum of <see cref="DiagnosticCreditLedgerDocument.UploadQuotaBumpCount"/> across
    /// every ledger row for the student whose <see cref="DiagnosticCreditLedgerDocument.IssuedAtUtc"/>
    /// falls inside the UTC calendar month identified by <paramref name="asOfUtc"/>.
    /// Returns 0 if the student has no credits that month.
    /// </summary>
    /// <remarks>
    /// PhotoDiagnosticQuotaGate calls this on every CheckAsync. It is the
    /// single point where the "ledger, not counter-decrement" design lands
    /// on the intake path. Implementations MUST scope the window to the
    /// student's UTC calendar month (matching MonthlyUsageKey.For).
    /// </remarks>
    Task<int> CountFreeCreditsAsync(
        string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct);
}
