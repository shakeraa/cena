// =============================================================================
// Cena Platform — Enrollment Document (TENANCY-P1a)
// Marten document for student enrollments in institutes and tracks
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Lifecycle status of a student enrollment.
/// </summary>
public enum EnrollmentStatus
{
    Active,
    Paused,
    Withdrawn,
    Completed
}

/// <summary>
/// Enrollment document binding a student directly to an institute and curriculum track.
/// Phase 1 uses direct Track + Institute binding; Program indirection is deferred to Phase 2.
/// </summary>
public class EnrollmentDocument
{
    /// <summary>
    /// Stable Marten document identity. Format: <c>enrollment-{slug}</c>.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Domain identity alias. Always equal to <see cref="Id"/>.
    /// </summary>
    public string EnrollmentId { get; set; } = "";

    /// <summary>
    /// Student who is enrolled.
    /// </summary>
    public string StudentId { get; set; } = "";

    /// <summary>
    /// Institute the student is enrolled in.
    /// </summary>
    public string InstituteId { get; set; } = "";

    /// <summary>
    /// Curriculum track the student is following.
    /// </summary>
    public string TrackId { get; set; } = "";

    /// <summary>
    /// Current lifecycle status of the enrollment.
    /// </summary>
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;

    /// <summary>
    /// Timestamp when the enrollment began.
    /// </summary>
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when the enrollment ended, if applicable.
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }
}
