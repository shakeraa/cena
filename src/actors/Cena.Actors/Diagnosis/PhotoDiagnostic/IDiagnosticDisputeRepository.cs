// =============================================================================
// Cena Platform — IDiagnosticDisputeRepository (EPIC-PRR-J PRR-385/390)
//
// Repository port for the dispute aggregate. Two backends:
//   - MartenDiagnosticDisputeRepository (production)
//   - InMemoryDiagnosticDisputeRepository (tests + dev)
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface IDiagnosticDisputeRepository
{
    Task InsertAsync(DiagnosticDisputeDocument dispute, CancellationToken ct);

    Task<DiagnosticDisputeDocument?> GetAsync(string disputeId, CancellationToken ct);

    Task<IReadOnlyList<DiagnosticDisputeDocument>> ListRecentAsync(
        DisputeStatus? status, int take, CancellationToken ct);

    Task<IReadOnlyList<DiagnosticDisputeDocument>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct);

    Task UpdateStatusAsync(
        string disputeId,
        DisputeStatus newStatus,
        DateTimeOffset reviewedAt,
        string? reviewerNote,
        CancellationToken ct);
}
