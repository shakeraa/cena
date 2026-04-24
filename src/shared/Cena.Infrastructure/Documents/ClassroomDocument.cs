// =============================================================================
// Cena Platform — Classroom Document (STB-00b + TENANCY-P1b)
// Marten document for classroom/join code management.
// TENANCY-P1b: Added InstituteId, ProgramId, Mode, MentorIds, JoinApprovalMode,
//              Status, StartDate, EndDate for multi-institute support.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Classroom delivery mode. Determines how students interact with the classroom.
/// </summary>
public enum ClassroomMode
{
    /// <summary>Teacher-led with scheduled sessions (default for school classrooms).</summary>
    InstructorLed = 0,

    /// <summary>Students progress at their own pace through the curriculum.</summary>
    SelfPaced = 1,

    /// <summary>1:1 mentoring (Phase 2 — PersonalMentorship classroom type).</summary>
    PersonalMentorship = 2
}

/// <summary>
/// How new students may join this classroom.
/// </summary>
public enum JoinApprovalMode
{
    /// <summary>Anyone with the join code is admitted immediately.</summary>
    AutoApprove = 0,

    /// <summary>Join requests require teacher/mentor approval.</summary>
    ManualApprove = 1,

    /// <summary>Students can only join via explicit invite link (no public join code).</summary>
    InviteOnly = 2
}

/// <summary>
/// Lifecycle status of a classroom.
/// </summary>
public enum ClassroomStatus
{
    /// <summary>Open for enrollment and active learning.</summary>
    Active = 0,

    /// <summary>Hidden from new enrollment, read-only for existing students.</summary>
    Archived = 1,

    /// <summary>Curriculum completed, final grades issued.</summary>
    Completed = 2
}

/// <summary>
/// Classroom document for student join codes and class management.
/// </summary>
public class ClassroomDocument
{
    // ---- Original fields (STB-00b) ----
    public string Id { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeacherId { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public string[] Subjects { get; set; } = Array.Empty<string>();
    public string Grade { get; set; } = "";
    public string? SchoolId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // ---- TENANCY-P1b: Multi-institute fields ----

    /// <summary>Foreign key to InstituteDocument. Null for legacy/pre-tenancy classrooms.</summary>
    public string? InstituteId { get; set; }

    /// <summary>Foreign key to CurriculumTrack (was "Program" in early design). Null for unscoped classrooms.</summary>
    public string? ProgramId { get; set; }

    /// <summary>Delivery mode. Default InstructorLed for back-compat with existing rows.</summary>
    public ClassroomMode Mode { get; set; } = ClassroomMode.InstructorLed;

    /// <summary>Mentor/tutor IDs assigned to this classroom (for PersonalMentorship or co-teaching).</summary>
    public string[] MentorIds { get; set; } = Array.Empty<string>();

    /// <summary>How new students join. Default AutoApprove for back-compat.</summary>
    public JoinApprovalMode JoinApproval { get; set; } = JoinApprovalMode.AutoApprove;

    /// <summary>Lifecycle status. Default Active for back-compat.</summary>
    public ClassroomStatus Status { get; set; } = ClassroomStatus.Active;

    /// <summary>When the classroom term/semester begins. Null for open-ended.</summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>When the classroom term/semester ends. Null for open-ended.</summary>
    public DateTimeOffset? EndDate { get; set; }
}
