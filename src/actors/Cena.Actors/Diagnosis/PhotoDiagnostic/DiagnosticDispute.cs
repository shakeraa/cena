// =============================================================================
// Cena Platform — Diagnostic dispute types (EPIC-PRR-J PRR-385)
//
// Student-initiated dispute flow: when the narration doesn't match what
// the student actually did, they tap "that's not right" and optionally
// add a comment. The dispute is persisted for SME review and surfaces on
// the support audit dashboard (PRR-390).
//
// Data handling: the dispute holds diagnosticId + anonymized student hash
// only. No raw photo, no raw LaTeX. Retention is 90 days so SMEs have
// a real window to review and calibrate templates.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Categorical reason codes for a dispute.</summary>
public enum DisputeReason
{
    WrongNarration,
    WrongStepIdentified,
    OcrMisread,
    Other,
}

/// <summary>Lifecycle status of a dispute.</summary>
public enum DisputeStatus
{
    New,
    InReview,
    Upheld,
    Rejected,
    Withdrawn,
}

public sealed record SubmitDiagnosticDisputeCommand(
    string DiagnosticId,
    string StudentSubjectIdHash,
    DisputeReason Reason,
    string? StudentComment);

public sealed record DiagnosticDisputeView(
    string DisputeId,
    string DiagnosticId,
    string StudentSubjectIdHash,
    DisputeReason Reason,
    string? StudentComment,
    DisputeStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewerNote);
