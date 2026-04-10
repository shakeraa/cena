// =============================================================================
// Cena Platform — Classroom Document (STB-00b)
// Marten document for classroom/join code management
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Classroom document for student join codes and class management.
/// </summary>
public class ClassroomDocument
{
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
}
