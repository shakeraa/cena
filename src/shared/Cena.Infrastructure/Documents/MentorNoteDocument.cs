// =============================================================================
// Cena Platform — Mentor Note Document (TENANCY-P2d)
// Markdown notes anchored to sessions, questions, or student profiles.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// What the note is anchored to.
/// </summary>
public enum NoteAnchorType
{
    Student,
    Session,
    Question,
    Assignment
}

/// <summary>
/// TENANCY-P2d: Mentor/teacher notes attached to student work.
/// Markdown content. Can be anchored to a student, session, question, or assignment.
/// Only visible to the note author and other mentors — never shown to students.
/// </summary>
public sealed class MentorNoteDocument
{
    public string Id { get; set; } = "";
    public string MentorId { get; set; } = "";
    public string ClassroomId { get; set; } = "";

    public NoteAnchorType AnchorType { get; set; }
    public string AnchorId { get; set; } = "";

    /// <summary>Optional student ID when the note is about a specific student's work.</summary>
    public string? StudentId { get; set; }

    /// <summary>Markdown content.</summary>
    public string Content { get; set; } = "";

    /// <summary>Short tags for filtering (e.g., "misconception", "progress", "parent-contact").</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
}
