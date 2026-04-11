// =============================================================================
// Cena Platform — Question Read Model (Marten Document)
// Flat denormalized document for fast list queries with filtering/sorting.
// Maintained by QuestionListProjection (inline projection).
// =============================================================================

namespace Cena.Actors.Questions;

/// <summary>
/// Marten document for the question list view. Indexed for fast filtering
/// by subject, status, Bloom's level, difficulty, quality score, and grade.
/// </summary>
public class QuestionReadModel
{
    public string Id { get; set; } = "";
    public string StemPreview { get; set; } = "";
    public string Subject { get; set; } = "";
    public List<string> Concepts { get; set; } = new();
    public List<string> ConceptNames { get; set; } = new();
    public int BloomsLevel { get; set; }
    public float Difficulty { get; set; }
    public string Status { get; set; } = "Draft";
    public int QualityScore { get; set; }
    public int UsageCount { get; set; }
    public float? SuccessRate { get; set; }
    public string SourceType { get; set; } = "authored";
    public string Language { get; set; } = "he";
    public List<string> Languages { get; set; } = new(); // FIND-data-008: tracks added language versions
    public string Grade { get; set; } = "";
    public string Topic { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Explanation { get; set; }
}
