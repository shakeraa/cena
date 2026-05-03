// =============================================================================
// Cena Platform -- FERPA Compliance: Data Retention Policy Constants
// REV-013.3: Retention periods per data category
//
// Archival implementation deferred to a scheduled background job.
// Approach: use Marten's built-in soft-delete + a Quartz.NET or
// IHostedService periodic job to archive/purge expired documents
// from their respective tables based on these retention windows.
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Defines data retention periods for FERPA and Israeli privacy law compliance.
/// These constants are used by the retention background job (future task) and
/// surfaced via the <c>/api/admin/compliance/data-retention</c> endpoint.
/// </summary>
public static class DataRetentionPolicy
{
    /// <summary>
    /// Education records: 7 years after last enrollment (FERPA requirement).
    /// Applies to: event streams, mastery snapshots, tutoring transcripts.
    /// </summary>
    public static readonly TimeSpan StudentRecordRetention = TimeSpan.FromDays(365 * 7);

    /// <summary>
    /// Audit logs: 5 years (compliance best practice).
    /// Applies to: StudentRecordAccessLog documents.
    /// </summary>
    public static readonly TimeSpan AuditLogRetention = TimeSpan.FromDays(365 * 5);

    /// <summary>
    /// Session analytics: 2 years.
    /// Applies to: focus scores, session timing, learning analytics aggregates.
    /// </summary>
    public static readonly TimeSpan AnalyticsRetention = TimeSpan.FromDays(365 * 2);

    /// <summary>
    /// Engagement data (XP, streaks, badges): 1 year after inactivity.
    /// </summary>
    public static readonly TimeSpan EngagementRetention = TimeSpan.FromDays(365);

    /// <summary>
    /// FIND-privacy-008: Tutor message retention — 90 days.
    /// Third-party AI processor (Anthropic) conversations are retained for a
    /// shorter window than general student records to minimise PII exposure.
    /// Applies to: TutorMessageDocument, TutorThreadDocument.
    /// Enforced by RetentionWorker (FIND-privacy-004).
    /// </summary>
    public static readonly TimeSpan TutorMessageRetention = TimeSpan.FromDays(90);

    /// <summary>
    /// RDY-006 / ADR-0003 Decision 2: Session misconception events — 30 days.
    /// Active remediation window — student may return to similar problems.
    /// Applies to: MisconceptionDetected_V1, MisconceptionRemediated_V1,
    /// SessionMisconceptionsScrubbed_V1.
    /// </summary>
    public static readonly TimeSpan SessionMisconceptionRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// RDY-006 / ADR-0003 Decision 2: Hard legal cap — 90 days.
    /// COPPA data minimization ceiling. No misconception event may survive
    /// beyond this regardless of tenant overrides.
    /// </summary>
    public static readonly TimeSpan SessionMisconceptionHardCap = TimeSpan.FromDays(90);
}
