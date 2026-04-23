// =============================================================================
// Cena Platform — MartenDiagnosticCreditLedger (EPIC-PRR-J PRR-391)
//
// Production Marten-backed implementation of the credit ledger. Mirrors
// MartenDiagnosticDisputeRepository in style (LightweightSession for writes,
// QuerySession for reads, pre-evaluated projections to ToList).
//
// Indexing and query shape: CountFreeCreditsAsync is on the intake hot
// path (PhotoDiagnosticQuotaGate.CheckAsync calls it on every upload
// request). We scope the query to the student and let Marten project the
// UTC year/month from IssuedAtUtc. The Marten JSON schema is auto-
// managed; if this ever shows up on a query plan we'll add a configured
// Index on (StudentSubjectIdHash, IssuedAtUtc) via MartenConfiguration.
// Until then, the existing generic document index on id + the small row
// volume (credits are rare by design) keeps latency within budget.
// =============================================================================

using Marten;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class MartenDiagnosticCreditLedger : IDiagnosticCreditLedger
{
    private readonly IDocumentStore _store;

    public MartenDiagnosticCreditLedger(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task IssueAsync(DiagnosticCreditLedgerDocument credit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credit);
        if (string.IsNullOrWhiteSpace(credit.Id))
            throw new ArgumentException("Credit Id is required.", nameof(credit));
        if (string.IsNullOrWhiteSpace(credit.DisputeId))
            throw new ArgumentException("DisputeId is required.", nameof(credit));
        await using var session = _store.LightweightSession();
        session.Insert(credit);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<DiagnosticCreditLedgerDocument?> GetByDisputeAsync(
        string disputeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
            throw new ArgumentException("disputeId is required.", nameof(disputeId));
        await using var session = _store.QuerySession();
        return await session.Query<DiagnosticCreditLedgerDocument>()
            .Where(c => c.DisputeId == disputeId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DiagnosticCreditLedgerDocument>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        await using var session = _store.QuerySession();
        var list = await session.Query<DiagnosticCreditLedgerDocument>()
            .Where(c => c.StudentSubjectIdHash == studentSubjectIdHash)
            .OrderByDescending(c => c.IssuedAtUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list.ToList();
    }

    public async Task<int> CountFreeCreditsAsync(
        string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));

        // Compute month bounds in UTC. We can't push MonthlyUsageKey.For
        // through LINQ (it's a C# method), so instead we filter by the
        // [first-of-month, first-of-next-month) window which Marten can
        // translate to SQL comparisons against IssuedAtUtc.
        var utc = asOfUtc.UtcDateTime;
        var monthStart = new DateTimeOffset(
            new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeSpan.Zero);
        var nextMonth = monthStart.AddMonths(1);

        await using var session = _store.QuerySession();
        var rows = await session.Query<DiagnosticCreditLedgerDocument>()
            .Where(c => c.StudentSubjectIdHash == studentSubjectIdHash
                && c.CreditKind == DiagnosticCreditKind.FreeUploadQuota
                && c.IssuedAtUtc >= monthStart
                && c.IssuedAtUtc < nextMonth)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Sum(r => r.UploadQuotaBumpCount);
    }
}
