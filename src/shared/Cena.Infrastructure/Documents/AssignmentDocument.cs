// =============================================================================
// Cena Platform — Assignment Document (TENANCY-P2c)
// Mentor-assigned question sets with due dates and progress tracking.
// =============================================================================

namespace Cena.Infrastructure.Documents;

public enum AssignmentStatus
{
    Pending,
    InProgress,
    Completed,
    Overdue,
    Withdrawn
}

/// <summary>
/// TENANCY-P2c: An assignment created by a mentor for a student or cohort.
/// </summary>
public sealed class AssignmentDocument
{
    public string Id { get; set; } = "";
    public string AssignmentId { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string AssignedByMentorId { get; set; } = "";

    /// <summary>Null = assigned to entire cohort.</summary>
    public string? AssignedToStudentId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string[] QuestionIds { get; set; } = Array.Empty<string>();
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int TotalQuestions => QuestionIds.Length;
    public int QuestionsCompleted { get; set; }
    public double ProgressPercent => TotalQuestions > 0 ? (double)QuestionsCompleted / TotalQuestions * 100 : 0;
}
