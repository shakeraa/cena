// =============================================================================
// Cena Platform — IHardCapSupportTicketRepository (EPIC-PRR-J PRR-402)
//
// Port for the hard-cap support-ticket aggregate. Two backends mirror the
// dispute + credit-ledger pattern:
//   - MartenHardCapSupportTicketRepository (production)
//   - InMemoryHardCapSupportTicketRepository (dev + unit tests)
//
// Write shape: Open → Resolve | Reject. Resolve writes the granted
// extension count; Reject writes 0 (a rejected ticket contributes nothing
// to the effective hard cap).
//
// Read shape: ListOpenAsync feeds the support queue; ListActiveGrantsForStudentAsync
// is the hot-path query PhotoDiagnosticQuotaGate calls on every upload-intake
// request — it returns the SUM of GrantedExtensionCount across every Resolved
// ticket whose MonthlyWindow matches the current UTC month. Implementations
// MUST scope on MonthlyWindow (the pre-computed "YYYY-MM" key) to avoid
// timezone-boundary drift.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface IHardCapSupportTicketRepository
{
    /// <summary>Insert a new Open ticket. Throws on duplicate id.</summary>
    Task OpenAsync(HardCapSupportTicketDocument ticket, CancellationToken ct);

    /// <summary>Load by id, or null if no such ticket.</summary>
    Task<HardCapSupportTicketDocument?> GetAsync(string id, CancellationToken ct);

    /// <summary>
    /// List every Open ticket, newest first. Used by the support triage
    /// surface. No pagination for v1 — the Open queue is expected to be
    /// measured in dozens, not thousands, because hard-cap hits are rare
    /// by design.
    /// </summary>
    Task<IReadOnlyList<HardCapSupportTicketDocument>> ListOpenAsync(CancellationToken ct);

    /// <summary>
    /// Mark the ticket Resolved with a one-time extension. Writes
    /// ResolvedAtUtc + GrantedBy + GrantedExtensionCount. Throws if the
    /// ticket is not currently Open (idempotency guard — two support
    /// clicks must not mint two grants).
    /// </summary>
    Task ResolveAsync(
        string id,
        int grantedExtension,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct);

    /// <summary>
    /// Mark the ticket Rejected with no grant. Writes ResolvedAtUtc +
    /// GrantedBy, zero grant. Throws if the ticket is not currently Open.
    /// </summary>
    Task RejectAsync(
        string id,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct);

    /// <summary>
    /// Sum of <see cref="HardCapSupportTicketDocument.GrantedExtensionCount"/>
    /// across every Resolved ticket for the given student whose
    /// <see cref="HardCapSupportTicketDocument.MonthlyWindow"/> matches the
    /// UTC calendar month identified by <paramref name="monthWindow"/>
    /// (use <see cref="MonthlyUsageKey.For"/> to compute). Returns 0 if
    /// none.
    /// </summary>
    /// <remarks>
    /// Hot path: PhotoDiagnosticQuotaGate.CheckAsync calls this on every
    /// upload intake to bump the effective hard cap. Cost is bounded
    /// because the number of resolved tickets per (student, month) is
    /// at most 1-2 in practice.
    /// </remarks>
    Task<int> ListActiveGrantsForStudentAsync(
        string studentSubjectIdHash,
        string monthWindow,
        CancellationToken ct);

    /// <summary>
    /// Return true if the student has an Open ticket in the given month.
    /// Used by the service to reject duplicate submissions within the
    /// same month window (one open ticket per student per month is
    /// enough context for support; a second one is likely noise).
    /// </summary>
    Task<bool> HasOpenTicketInMonthAsync(
        string studentSubjectIdHash,
        string monthWindow,
        CancellationToken ct);
}
