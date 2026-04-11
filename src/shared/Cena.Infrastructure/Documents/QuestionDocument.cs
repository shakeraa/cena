// =============================================================================
// Cena Platform — Question Document (HARDEN SessionEndpoints)
// Production-grade question storage for question bank
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Question document for the question bank. Stores question content,
/// metadata, and answer information for adaptive learning sessions.
/// </summary>
public class QuestionDocument
{
    public string Id { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Topic { get; set; }
    public string Difficulty { get; set; } = "medium"; // easy, medium, hard
    public string ConceptId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string QuestionType { get; set; } = "multiple-choice"; // multiple-choice, free-text, etc.
    public string[]? Choices { get; set; }
    public string CorrectAnswer { get; set; } = "";
    public string? Explanation { get; set; }
    public int? Grade { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
