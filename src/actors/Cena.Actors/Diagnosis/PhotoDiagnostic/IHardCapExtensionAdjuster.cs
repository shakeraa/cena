// =============================================================================
// Cena Platform — IHardCapExtensionAdjuster (EPIC-PRR-J PRR-402)
//
// Narrow read port consumed by PhotoDiagnosticQuotaGate to figure out how
// much the effective hard cap should be bumped for a given student this
// UTC month. Separated from IHardCapSupportTicketRepository so the quota
// gate doesn't depend on write-side methods (OpenAsync / ResolveAsync /
// RejectAsync / ListOpenAsync): it only needs the sum of active grants.
//
// Mirrors the split between IDiagnosticCreditLedger.CountFreeCreditsAsync
// (read) and the write surface the DiagnosticCreditService uses — except
// here the ports are kept in separate interfaces for even tighter
// least-privilege. Production DI binds both to the same underlying
// InMemory / Marten repository instance.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Port for querying how much support has granted the student in the UTC
/// calendar month of <paramref>asOfUtc</paramref>.
/// </summary>
public interface IHardCapExtensionAdjuster
{
    /// <summary>
    /// Sum of active <see cref="HardCapSupportTicketDocument.GrantedExtensionCount"/>
    /// for the student's UTC calendar month. Returns 0 when there is no
    /// active grant (behaves as a no-op adjuster).
    /// </summary>
    Task<int> GetActiveExtensionAsync(
        string studentSubjectIdHash,
        DateTimeOffset asOfUtc,
        CancellationToken ct);
}

/// <summary>
/// Default adapter that composes <see cref="IHardCapSupportTicketRepository"/>
/// into the narrow read port. Maps <paramref>asOfUtc</paramref> to the
/// canonical "YYYY-MM" key via <see cref="MonthlyUsageKey.For"/> and calls
/// <see cref="IHardCapSupportTicketRepository.ListActiveGrantsForStudentAsync"/>.
/// </summary>
public sealed class HardCapExtensionAdjuster : IHardCapExtensionAdjuster
{
    private readonly IHardCapSupportTicketRepository _repo;

    public HardCapExtensionAdjuster(IHardCapSupportTicketRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public Task<int> GetActiveExtensionAsync(
        string studentSubjectIdHash,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var monthWindow = MonthlyUsageKey.For(asOfUtc);
        return _repo.ListActiveGrantsForStudentAsync(studentSubjectIdHash, monthWindow, ct);
    }
}
