// =============================================================================
// Cena Platform — Content Document
// SAI-06: Marten document for extracted content segments (non-question content
// from textbooks — definitions, theorems, worked examples, etc.)
// Immutable once extracted; NOT event-sourced.
// =============================================================================

namespace Cena.Actors.Ingest;

public enum ContentType
{
    Definition,
    Theorem,
    WorkedExample,
    Explanation,
    Narrative,
    Formula,
    Diagram,
    Summary
}

public sealed class ContentDocument
{
    public string Id { get; init; } = $"content-{Guid.NewGuid():N}";
    public string PipelineItemId { get; init; } = "";
    public string Text { get; init; } = "";
    public string TextHtml { get; init; } = "";
    public ContentType Type { get; init; }
    public string? AssociatedConceptId { get; init; }
    public string? AssociatedQuestionId { get; init; }
    public string Language { get; init; } = "he";
    public string Subject { get; init; } = "";
    public string? Topic { get; init; }
    public int PageNumber { get; init; }
    public float Confidence { get; init; }
    public DateTimeOffset ExtractedAt { get; init; }
}
