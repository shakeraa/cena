// =============================================================================
// Cena Platform — Cultural Context Review Board DLQ Document (prr-034)
//
// MVP slice of the cultural-context community review board ops queue.
// Captures moderation misses for items that may have cultural-context
// implications the automated scanners and first-pass moderators missed —
// the review board reads from this queue, deliberates, and records a
// sign-off artifact.
//
// SCOPE (prr-034 MVP):
//   - One document per DLQ entry.
//   - Tenant-scoped (schoolId captured at enqueue time, immutable).
//   - Reviewer roster and SLA timers are OUT OF SCOPE for this slice; a
//     follow-up task wires the full ops queue pattern described in
//     ADR-0053 (external-integration adapter DLQ) for the broader
//     cultural-context ops queue.
//
// RELATED:
//   - ADR-0052 (saga pattern) — DLQ pattern for saga failures.
//   - ADR-0053 (external-integration adapter) — DLQ pattern for third-
//     party adapter failures. Cultural-context DLQ follows the same UI
//     shell.
// =============================================================================

using System;
using System.Collections.Generic;

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Single ops-queue entry describing a cultural-context moderation miss
/// that the community review board needs to evaluate.
/// </summary>
public class CulturalContextReviewBoardDocument
{
    /// <summary>Queue entry id. ULID.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// School / tenant id captured at enqueue. TenantScope guarantees every
    /// enqueue carries a school id — anonymous reports are enqueued under
    /// the platform school (`cena-platform`).
    /// </summary>
    public string SchoolId { get; set; } = "";

    /// <summary>Subject item type being reviewed.</summary>
    /// <example>question | explanation | hint | bagrut-recreation</example>
    public string SubjectKind { get; set; } = "";

    /// <summary>Subject item id being reviewed (question id, explanation id, etc.).</summary>
    public string SubjectId { get; set; } = "";

    /// <summary>
    /// Category of cultural-context concern flagged. Narrow set — ops
    /// queue UI filters on this.
    /// </summary>
    /// <example>
    /// language-register | cultural-reference-clarity |
    /// religious-sensitivity | identity-respect |
    /// regional-context-mismatch | unknown
    /// </example>
    public string ConcernCategory { get; set; } = "unknown";

    /// <summary>Free-text reason from the miss detector / submitter.</summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// Signal that routed this item to the DLQ.
    /// "scanner-miss" (automated scanner below-threshold)
    /// | "moderator-flagged" (human moderator flagged after first-pass
    ///     approval)
    /// | "student-reported" (student feedback).
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// The correlation id of the upstream moderation event. Cross-hop
    /// tracing — useful when the review board needs to inspect the
    /// original moderation trail.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Operator id of the moderator who enqueued (if any). Null for
    /// student-reported or scanner-triggered enqueues.
    /// </summary>
    public string? EnqueuedByOperatorId { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Board sign-off state.
    /// "pending" | "under-review" | "approved" | "rejected" | "superseded".
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Assigned reviewer (board member). Null until claimed.
    /// </summary>
    public string? AssignedReviewerId { get; set; }

    /// <summary>
    /// Decision artifact — the board's signed-off notes on this entry.
    /// Null until status is approved/rejected/superseded.
    /// </summary>
    public BoardDecisionArtifact? Decision { get; set; }

    /// <summary>
    /// Light audit trail. Appended to on every state transition. Kept
    /// in-document (not as a projected event stream) to keep the MVP
    /// slice cheap; a full event-sourced migration follows in the
    /// broader ops queue task.
    /// </summary>
    public List<BoardAuditEntry> AuditTrail { get; set; } = new();
}

/// <summary>
/// Signed-off decision produced by the review board. Exportable for
/// regulator / institute audits.
/// </summary>
public class BoardDecisionArtifact
{
    public string DecidedByReviewerId { get; set; } = "";
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>approved | rejected | superseded</summary>
    public string Outcome { get; set; } = "";
    public string Rationale { get; set; } = "";

    /// <summary>
    /// If the board agreed a follow-up content change is required, the
    /// corresponding task id (from the pre-release review queue, or a
    /// new task spawned for the change). Optional.
    /// </summary>
    public string? FollowUpTaskId { get; set; }
}

public class BoardAuditEntry
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Note { get; set; }
}
