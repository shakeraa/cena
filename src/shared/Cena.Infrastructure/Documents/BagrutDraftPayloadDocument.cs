// =============================================================================
// Cena Platform — BagrutDraftPayloadDocument
//
// Stores the actual extracted content for a Bagrut draft: prompt text,
// LaTeX, figure spec, source page, extraction confidence. Sibling to
// PipelineItemDocument (same Id) — PipelineItemDocument is the kanban
// tracking row, this is the payload the variant-generator reads back
// to seed AI generation.
//
// Kept as a separate document so:
//   - PipelineItemDocument stays focused on kanban concerns and doesn't
//     bloat with payloads from one specific source type.
//   - The variant-generator (and any future consumer of Bagrut payloads)
//     queries one row by Id.
// =============================================================================

namespace Cena.Infrastructure.Documents;

public sealed class BagrutDraftPayloadDocument
{
    /// <summary>Same id as the linked PipelineItemDocument (the "draftId").</summary>
    public string Id { get; set; } = "";

    public string ExamCode { get; set; } = "";
    public string SourcePdfId { get; set; } = "";
    public int SourcePage { get; set; }

    public string Prompt { get; set; } = "";
    public string? LatexContent { get; set; }
    public string? FigureSpecJson { get; set; }
    public double ExtractionConfidence { get; set; }

    /// <summary>
    /// Persisted flag set by the curator at upload time — signals that
    /// this draft was derived from a Ministry-published past Bagrut
    /// (independent of whether MinistrySubjectCode / MinistryQuestionPaperCode
    /// are filled, which separately drives the BagrutCorpusItemDocument
    /// side-effect for the ADR-0043 isomorph rejector). Filterable so
    /// curators can scope the kanban / variant-generation flow to
    /// Ministry-only or non-Ministry-only items.
    /// </summary>
    public bool IsMinistryReference { get; set; }

    public List<string> ReviewNotes { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
}
