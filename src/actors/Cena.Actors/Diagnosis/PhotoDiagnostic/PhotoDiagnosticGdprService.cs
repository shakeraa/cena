// =============================================================================
// Cena Platform — PhotoDiagnosticGdprService (EPIC-PRR-J PRR-411/412)
//
// GDPR Article 17 (right to erasure) + Article 20 (data portability) slice
// for the photo-diagnostic bounded context.
//
// Scope: only dispute records are student-persisted at this layer.
//   - Raw photos: session-scoped, never persisted by design (ADR-0003).
//   - OCR/CAS traces: session-scoped, ephemeral cache only.
//   - Usage counters (PhotoDiagnosticUsageDocument): aggregate counts,
//     not behavioural data; left in place as per persona #9 legal review
//     (aggregate metrics are carved out from GDPR Article 17 when they
//     have no cross-referenceable student identifier besides the
//     already-anonymized hash).
//
// Consumed by the student-facing /me/gdpr/export and /me/gdpr/delete
// endpoints via StudentDataExporter fan-out.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Export bundle returned to the student (or wrapped into the global GDPR export).</summary>
public sealed record PhotoDiagnosticExportBundle(
    string StudentSubjectIdHash,
    DateTimeOffset ExportedAt,
    IReadOnlyList<DiagnosticDisputeView> Disputes);

public interface IPhotoDiagnosticGdprService
{
    Task<PhotoDiagnosticExportBundle> ExportAsync(string studentSubjectIdHash, CancellationToken ct);
    Task<int> DeleteAllAsync(string studentSubjectIdHash, CancellationToken ct);
}

public sealed class PhotoDiagnosticGdprService : IPhotoDiagnosticGdprService
{
    /// <summary>Cap on per-student export size; 1000 disputes is well past any realistic student's tail.</summary>
    public const int ExportTake = 1000;

    private readonly IDiagnosticDisputeRepository _repo;
    private readonly TimeProvider _clock;
    private readonly ILogger<PhotoDiagnosticGdprService> _logger;

    public PhotoDiagnosticGdprService(
        IDiagnosticDisputeRepository repo,
        TimeProvider clock,
        ILogger<PhotoDiagnosticGdprService> logger)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PhotoDiagnosticExportBundle> ExportAsync(string studentSubjectIdHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var docs = await _repo.ListByStudentAsync(studentSubjectIdHash, ExportTake, ct).ConfigureAwait(false);
        var disputes = docs.Select(ToView).ToList();
        _logger.LogInformation(
            "PhotoDiagnostic GDPR export: student={Student} disputes={Count}",
            studentSubjectIdHash, disputes.Count);
        return new PhotoDiagnosticExportBundle(
            StudentSubjectIdHash: studentSubjectIdHash,
            ExportedAt: _clock.GetUtcNow(),
            Disputes: disputes);
    }

    public async Task<int> DeleteAllAsync(string studentSubjectIdHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var deleted = await _repo.DeleteByStudentAsync(studentSubjectIdHash, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "PhotoDiagnostic GDPR erasure: student={Student} disputesDeleted={Count}",
            studentSubjectIdHash, deleted);
        return deleted;
    }

    private static DiagnosticDisputeView ToView(DiagnosticDisputeDocument d) => new(
        DisputeId: d.Id,
        DiagnosticId: d.DiagnosticId,
        StudentSubjectIdHash: d.StudentSubjectIdHash,
        Reason: d.Reason,
        StudentComment: d.StudentComment,
        Status: d.Status,
        SubmittedAt: d.SubmittedAt,
        ReviewedAt: d.ReviewedAt,
        ReviewerNote: d.ReviewerNote);
}
