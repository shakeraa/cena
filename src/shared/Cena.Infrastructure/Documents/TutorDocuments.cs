// =============================================================================
// Cena Platform — Tutor Documents (STB-04 Phase 1)
// Marten documents for AI tutor threads and messages
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Tutor thread document for AI tutoring sessions.
/// </summary>
public class TutorThreadDocument
{
    public string Id { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subject { get; set; }
    public string? Topic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; } = 0;
    public bool IsArchived { get; set; } = false;
}

/// <summary>
/// Tutor message document for AI tutor conversation history.
/// </summary>
public class TutorMessageDocument
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Role { get; set; } = ""; // 'user' | 'assistant' | 'system'
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Model { get; set; } // For assistant messages: 'gpt-4', etc.
    public int? TokenCount { get; set; }
}
