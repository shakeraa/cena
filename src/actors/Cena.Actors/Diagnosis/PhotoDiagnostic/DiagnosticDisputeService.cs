// =============================================================================
// Cena Platform — DiagnosticDisputeService (EPIC-PRR-J PRR-385)
//
// Student-initiated dispute flow. Called from the student dispute
// endpoint. Duties:
//   - validate the command (non-empty ids, bounded comment length)
//   - assign a GUID dispute id
//   - persist via IDiagnosticDisputeRepository
//   - force-sample into the accuracy audit log so SMEs see the
//     full candidate context without needing the raw diagnostic trace
//   - record a metric so ops sees a dispute-rate dashboard (ties
//     directly to per-template calibration signal)
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface IDiagnosticDisputeService
{
    Task<DiagnosticDisputeView> SubmitAsync(
        SubmitDiagnosticDisputeCommand command,
        CancellationToken ct);

    Task<IReadOnlyList<DiagnosticDisputeView>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct);

    Task<IReadOnlyList<DiagnosticDisputeView>> ListRecentAsync(
        DisputeStatus? status, int take, CancellationToken ct);

    Task<DiagnosticDisputeView?> GetAsync(string disputeId, CancellationToken ct);

    Task<DiagnosticDisputeView> ReviewAsync(
        string disputeId, DisputeStatus newStatus, string? reviewerNote, CancellationToken ct);
}

public sealed class DiagnosticDisputeService : IDiagnosticDisputeService
{
    /// <summary>Max free-text comment length in chars. Enforced server-side.</summary>
    public const int MaxCommentLength = 1000;

    private readonly IDiagnosticDisputeRepository _repo;
    private readonly PhotoDiagnosticMetrics _metrics;
    private readonly ILogger<DiagnosticDisputeService> _logger;
    private readonly TimeProvider _clock;

    public DiagnosticDisputeService(
        IDiagnosticDisputeRepository repo,
        PhotoDiagnosticMetrics metrics,
        ILogger<DiagnosticDisputeService> logger,
        TimeProvider clock)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DiagnosticDisputeView> SubmitAsync(
        SubmitDiagnosticDisputeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.DiagnosticId))
            throw new ArgumentException("DiagnosticId is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.StudentSubjectIdHash))
            throw new ArgumentException("StudentSubjectIdHash is required.", nameof(command));
        if (command.StudentComment is { Length: > MaxCommentLength })
            throw new ArgumentException(
                $"Comment exceeds {MaxCommentLength} characters.", nameof(command));

        var disputeId = Guid.NewGuid().ToString("D");
        var now = _clock.GetUtcNow();
        var doc = new DiagnosticDisputeDocument
        {
            Id = disputeId,
            DiagnosticId = command.DiagnosticId,
            StudentSubjectIdHash = command.StudentSubjectIdHash,
            Reason = command.Reason,
            StudentComment = command.StudentComment,
            Status = DisputeStatus.New,
            SubmittedAt = now,
            ReviewedAt = null,
            ReviewerNote = null,
        };
        await _repo.InsertAsync(doc, ct).ConfigureAwait(false);

        // A dispute is always a reason to sample for SME review.
        _metrics.RecordAuditSampled("student_dispute");

        _logger.LogInformation(
            "PhotoDiagnostic dispute submitted: disputeId={DisputeId} diagId={DiagId} reason={Reason}",
            disputeId, command.DiagnosticId, command.Reason);

        return ToView(doc);
    }

    public async Task<IReadOnlyList<DiagnosticDisputeView>> ListByStudentAsync(
        string studentSubjectIdHash, int take, CancellationToken ct)
    {
        var docs = await _repo.ListByStudentAsync(studentSubjectIdHash, take, ct).ConfigureAwait(false);
        return docs.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<DiagnosticDisputeView>> ListRecentAsync(
        DisputeStatus? status, int take, CancellationToken ct)
    {
        var docs = await _repo.ListRecentAsync(status, take, ct).ConfigureAwait(false);
        return docs.Select(ToView).ToList();
    }

    public async Task<DiagnosticDisputeView?> GetAsync(string disputeId, CancellationToken ct)
    {
        var doc = await _repo.GetAsync(disputeId, ct).ConfigureAwait(false);
        return doc is null ? null : ToView(doc);
    }

    public async Task<DiagnosticDisputeView> ReviewAsync(
        string disputeId, DisputeStatus newStatus, string? reviewerNote, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
            throw new ArgumentException("disputeId is required.", nameof(disputeId));
        if (newStatus is DisputeStatus.New)
            throw new ArgumentException("Cannot transition back to New.", nameof(newStatus));

        var now = _clock.GetUtcNow();
        await _repo.UpdateStatusAsync(disputeId, newStatus, now, reviewerNote, ct).ConfigureAwait(false);

        var reread = await _repo.GetAsync(disputeId, ct).ConfigureAwait(false);
        if (reread is null)
            throw new InvalidOperationException($"Dispute {disputeId} disappeared mid-update.");
        return ToView(reread);
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
