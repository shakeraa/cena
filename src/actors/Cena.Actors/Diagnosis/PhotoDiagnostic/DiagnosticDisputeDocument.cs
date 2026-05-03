// =============================================================================
// Cena Platform — DiagnosticDisputeDocument (EPIC-PRR-J PRR-385/390)
//
// Marten-backed dispute record. Primary key is a GUID; queryable by
// diagnosticId, studentSubjectIdHash, status, or date range.
//
// Retention: 90 days from submittedAt. No PII.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed record DiagnosticDisputeDocument
{
    public string Id { get; init; } = "";
    public string DiagnosticId { get; init; } = "";
    public string StudentSubjectIdHash { get; init; } = "";
    public DisputeReason Reason { get; init; }
    public string? StudentComment { get; init; }
    public DisputeStatus Status { get; init; }
    public DateTimeOffset SubmittedAt { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ReviewerNote { get; init; }
}
