// =============================================================================
// Cena Platform — PhotoHashLedgerDocument (EPIC-PRR-J PRR-412)
//
// Persistent "proof of upload" row for every diagnostic photo. The photo
// itself is deleted within 5 minutes per the PRR-412 SLA; the ledger
// survives so abuse-detection (PRR-403 AbuseDetectionWorker, duplicate-
// upload / rate-limit probes, support auditing per PRR-390) can reason
// about a student's upload history WITHOUT retaining the PII-bearing
// image or its OCR'd LaTeX.
//
// Privacy invariants (ADR-0003 misconception-session-scope + PPL Amd 13):
//   - No PII stored. StudentSubjectIdHash is the salted hash used across
//     PhotoDiagnosticUsageDocument / DiagnosticDisputeDocument, never the
//     raw subject id.
//   - PhotoSha256Hash is a perceptual-free bytes hash of the uploaded
//     file. It identifies re-uploads of the same bytes but does not
//     leak image content (it's a one-way digest of encrypted bytes
//     post-EXIF-strip).
//   - DisputeHoldUntilUtc, when non-null, extends the photo's delete
//     window so support (PRR-390 audit view) can inspect the evidence
//     for the duration of the open dispute. Once past, the PhotoDeletion-
//     Worker deletes the photo and leaves the ledger row intact.
//
// Retention: ledger rows live far longer than the 5-min photo SLA. They
// cap out at the AbuseDetectionWorker's 30-day rolling window and
// PRR-390's 30-day support-audit window; a separate retention pass
// (future work, tracked in EPIC-PRR-J) will prune ledger rows past
// 30 days. We do NOT delete ledger rows on photo deletion — that would
// defeat the whole purpose (re-upload detection post-delete).
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Immutable ledger row describing one photo upload. Primary key is the
/// diagnostic id (1:1 with the upload), which is also the S3/blob key
/// used by <see cref="IPhotoBlobStore"/> to address the photo binary.
/// </summary>
public sealed record PhotoHashLedgerDocument
{
    /// <summary>
    /// Diagnostic id — the upload's canonical identifier. Primary key.
    /// Also serves as the blob key passed to <see cref="IPhotoBlobStore"/>
    /// for deletion (we want the ledger Id and blob key to be the same
    /// string so the deletion worker can derive one from the other
    /// without a second lookup).
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// SHA-256 of the scrubbed (post-EXIF-strip) photo bytes, hex-encoded.
    /// Lets abuse detection spot "same photo re-uploaded under a new
    /// diagnostic id" without keeping the photo. One-way; not reversible.
    /// </summary>
    public string PhotoSha256Hash { get; init; } = "";

    /// <summary>
    /// Salted hash of the student subject id. Matches the same hash format
    /// used by PhotoDiagnosticUsageDocument + DiagnosticDisputeDocument so
    /// cross-context joins (e.g., "has this student disputed the photo
    /// they uploaded at 14:02?") are straightforward.
    /// </summary>
    public string StudentSubjectIdHash { get; init; } = "";

    /// <summary>
    /// UTC instant the photo was accepted at the upload boundary. The
    /// 5-min deletion SLA is computed as <c>UploadedAtUtc + 5 min</c>;
    /// the <see cref="PhotoDeletionWorker"/> never deletes a photo older
    /// than this threshold (except via an active dispute hold).
    /// </summary>
    public DateTimeOffset UploadedAtUtc { get; init; }

    /// <summary>
    /// If a dispute was filed against this diagnostic before the 5-min
    /// delete fired, the photo is kept until this UTC instant so support
    /// can inspect it (see PRR-390). Null means no hold; the normal
    /// 5-min SLA applies. A row transitions from null → non-null when
    /// <see cref="DiagnosticDisputeService"/> opens a dispute, and stays
    /// non-null until the dispute-retention worker prunes the dispute
    /// record (PRR-410).
    /// </summary>
    public DateTimeOffset? DisputeHoldUntilUtc { get; init; }

    /// <summary>
    /// True once <see cref="PhotoDeletionWorker"/> has successfully deleted
    /// the blob via <see cref="IPhotoBlobStore"/>. Set by the worker
    /// after a successful delete; used by <see cref="PhotoDeletionAuditJob"/>
    /// to distinguish "photo still extant and past SLA" (a violation)
    /// from "photo already deleted, ledger kept for audit" (normal).
    /// </summary>
    public bool PhotoDeleted { get; init; }

    /// <summary>
    /// When the photo was actually deleted, UTC. Null until
    /// <see cref="PhotoDeleted"/> becomes true. Logged + emitted as the
    /// <c>deletion_lag_ms</c> metric so SLO dashboards can alert on
    /// lag creep past the 5-min budget.
    /// </summary>
    public DateTimeOffset? DeletedAtUtc { get; init; }
}
