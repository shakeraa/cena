// =============================================================================
// Cena Platform — MartenHardCapSupportTicketRepository (EPIC-PRR-J PRR-402)
//
// Production Marten-backed implementation of the hard-cap support-ticket
// aggregate. Mirrors MartenDiagnosticCreditLedger in style (LightweightSession
// for writes, QuerySession for reads, pre-evaluated projections to ToList).
//
// Query plan notes
// ----------------
// - ListActiveGrantsForStudentAsync is hot (every PhotoDiagnosticQuotaGate
//   check calls it). It filters on (StudentSubjectIdHash, MonthlyWindow,
//   Status == Resolved) — three equality predicates all on fields of the
//   same document. Marten's generic id index + the narrow result cardinality
//   (≤ 1-2 rows per student per month by policy) keeps latency inside budget.
//   If profile data ever says otherwise we'll add a composite
//   (StudentSubjectIdHash, MonthlyWindow) index via ConfigureMarten; the
//   existing MartenDiagnosticCreditLedger comments there apply verbatim.
// - ListOpenAsync is cold (support-agent view). Full-scan of Status == Open
//   is fine; the Open queue is expected to be dozens of rows, not thousands.
// =============================================================================

using Marten;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class MartenHardCapSupportTicketRepository : IHardCapSupportTicketRepository
{
    private readonly IDocumentStore _store;

    public MartenHardCapSupportTicketRepository(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task OpenAsync(HardCapSupportTicketDocument ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (string.IsNullOrWhiteSpace(ticket.Id))
            throw new ArgumentException("Ticket Id is required.", nameof(ticket));
        if (string.IsNullOrWhiteSpace(ticket.StudentSubjectIdHash))
            throw new ArgumentException("StudentSubjectIdHash is required.", nameof(ticket));
        if (string.IsNullOrWhiteSpace(ticket.MonthlyWindow))
            throw new ArgumentException("MonthlyWindow is required.", nameof(ticket));
        await using var session = _store.LightweightSession();
        session.Insert(ticket);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<HardCapSupportTicketDocument?> GetAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.", nameof(id));
        await using var session = _store.QuerySession();
        return await session.LoadAsync<HardCapSupportTicketDocument>(id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HardCapSupportTicketDocument>> ListOpenAsync(CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var list = await session.Query<HardCapSupportTicketDocument>()
            .Where(t => t.Status == HardCapSupportTicketStatus.Open)
            .OrderByDescending(t => t.RequestedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list.ToList();
    }

    public async Task ResolveAsync(
        string id,
        int grantedExtension,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(resolvedBy))
            throw new ArgumentException("resolvedBy is required.", nameof(resolvedBy));

        await using var session = _store.LightweightSession();
        var existing = await session.LoadAsync<HardCapSupportTicketDocument>(id, ct)
            .ConfigureAwait(false);
        if (existing is null)
            throw new InvalidOperationException($"Hard-cap ticket {id} not found.");
        if (existing.Status != HardCapSupportTicketStatus.Open)
            throw new InvalidOperationException(
                $"Hard-cap ticket {id} is not Open (current: {existing.Status}).");

        var updated = existing with
        {
            Status = HardCapSupportTicketStatus.Resolved,
            GrantedExtensionCount = grantedExtension,
            GrantedBy = resolvedBy,
            ResolvedAtUtc = resolvedAtUtc,
        };
        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RejectAsync(
        string id,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(resolvedBy))
            throw new ArgumentException("resolvedBy is required.", nameof(resolvedBy));

        await using var session = _store.LightweightSession();
        var existing = await session.LoadAsync<HardCapSupportTicketDocument>(id, ct)
            .ConfigureAwait(false);
        if (existing is null)
            throw new InvalidOperationException($"Hard-cap ticket {id} not found.");
        if (existing.Status != HardCapSupportTicketStatus.Open)
            throw new InvalidOperationException(
                $"Hard-cap ticket {id} is not Open (current: {existing.Status}).");

        var updated = existing with
        {
            Status = HardCapSupportTicketStatus.Rejected,
            GrantedExtensionCount = 0,
            GrantedBy = resolvedBy,
            ResolvedAtUtc = resolvedAtUtc,
        };
        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ListActiveGrantsForStudentAsync(
        string studentSubjectIdHash,
        string monthWindow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(monthWindow))
            throw new ArgumentException("monthWindow is required.", nameof(monthWindow));

        await using var session = _store.QuerySession();
        // Pull Resolved rows for (student, month) and sum in memory. The
        // expected row count per (student, month) is ≤ 1 under policy
        // (service rejects concurrent duplicates), so projecting server-
        // side via Sum() would be a premature optimization over a list
        // whose count is measured in single digits.
        var rows = await session.Query<HardCapSupportTicketDocument>()
            .Where(t =>
                t.StudentSubjectIdHash == studentSubjectIdHash
                && t.MonthlyWindow == monthWindow
                && t.Status == HardCapSupportTicketStatus.Resolved)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Sum(r => r.GrantedExtensionCount);
    }

    public async Task<bool> HasOpenTicketInMonthAsync(
        string studentSubjectIdHash,
        string monthWindow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(monthWindow))
            throw new ArgumentException("monthWindow is required.", nameof(monthWindow));

        await using var session = _store.QuerySession();
        return await session.Query<HardCapSupportTicketDocument>()
            .AnyAsync(t =>
                t.StudentSubjectIdHash == studentSubjectIdHash
                && t.MonthlyWindow == monthWindow
                && t.Status == HardCapSupportTicketStatus.Open, ct)
            .ConfigureAwait(false);
    }
}
