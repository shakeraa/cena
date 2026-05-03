// =============================================================================
// Cena Platform -- Content Block Marten Document
// SAI-05: Read model for extracted semantic content blocks (definitions,
// theorems, examples, explanations, exercise solutions).
// Projected from ContentExtracted_V1 events for fast retrieval queries.
// =============================================================================

namespace Cena.Actors.Ingest;

/// <summary>
/// Marten document for a single semantic content block extracted from a source
/// document. Used by downstream embedding (Task 06) and conversational
/// tutoring (Task 07) retrieval.
/// </summary>
public sealed class ContentBlockDocument
{
    public string Id { get; set; } = "";
    public string SourceDocId { get; set; } = "";
    public string ContentType { get; set; } = "";       // definition, theorem, example, explanation, exercise_solution
    public string RawText { get; set; } = "";
    public string ProcessedText { get; set; } = "";      // Cleaned Markdown
    public IReadOnlyList<string> ConceptIds { get; set; } = [];
    public string Language { get; set; } = "he";         // he, ar, en
    public string? PageRange { get; set; }
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
