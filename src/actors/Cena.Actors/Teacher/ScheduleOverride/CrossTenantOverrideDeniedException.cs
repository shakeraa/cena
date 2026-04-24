// =============================================================================
// Cena Platform — CrossTenantOverrideDeniedException (prr-150)
//
// Thrown by TeacherOverrideCommands when the teacher's InstituteId claim
// does NOT match the student's active enrollment institute. This is the
// tenant-isolation tripwire required by ADR-0001: a teacher at institute A
// must never be able to override schedule content for a student enrolled
// at institute B, even if they happen to know the student's pseudonymous
// id.
//
// The exception is a distinct type (not plain UnauthorizedAccessException)
// so:
//   - The admin-API endpoint layer can map it to a precise 403 with a
//     well-defined error code.
//   - The SIEM log entry produced by the command handler can be correlated
//     across tenants for redteam forensics (see TeacherOverrideNoCrossTenantTest).
//   - Architecture tests can prove every command path surfaces this exact
//     type rather than a leaky generic error.
// =============================================================================

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Raised when a teacher attempts to override a student's schedule from a
/// different institute than the student's active enrollment (ADR-0001
/// violation).
/// </summary>
public sealed class CrossTenantOverrideDeniedException : Exception
{
    /// <summary>Pseudonymous student id that was targeted.</summary>
    public string StudentAnonId { get; }

    /// <summary>Pseudonymous teacher id of the caller.</summary>
    public string TeacherActorId { get; }

    /// <summary>The teacher's claimed institute id.</summary>
    public string TeacherInstituteId { get; }

    /// <summary>The student's actual enrollment institute id.</summary>
    public string StudentInstituteId { get; }

    public CrossTenantOverrideDeniedException(
        string studentAnonId,
        string teacherActorId,
        string teacherInstituteId,
        string studentInstituteId)
        : base(BuildMessage(studentAnonId, teacherActorId, teacherInstituteId, studentInstituteId))
    {
        StudentAnonId = studentAnonId;
        TeacherActorId = teacherActorId;
        TeacherInstituteId = teacherInstituteId;
        StudentInstituteId = studentInstituteId;
    }

    private static string BuildMessage(
        string studentAnonId, string teacherActorId,
        string teacherInstituteId, string studentInstituteId)
        => $"Teacher '{teacherActorId}' at institute '{teacherInstituteId}' cannot " +
           $"override schedule for student '{studentAnonId}' (enrolled at institute " +
           $"'{studentInstituteId}'). Cross-tenant overrides are denied (ADR-0001).";
}
