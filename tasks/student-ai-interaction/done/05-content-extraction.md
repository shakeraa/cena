# Task 05: Content Extraction Pipeline Stage

**Effort**: 5-7 days | **Track**: E (independent of Tasks 02-04) | **Depends on**: Existing ingest pipeline | **Blocks**: 06

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Proto.Actor, Marten. The ingestion pipeline lives in `src/actors/Cena.Actors/Ingest/` (9 files):

- **IngestionOrchestrator.cs** — orchestrates the pipeline
- **GeminiOcrClient.cs** / **MathpixClient.cs** — OCR providers (extract text from PDFs/images)
- **QuestionSegmenter.cs** — segments extracted text into individual questions
- **DeduplicationService.cs** — 2-level dedup (exact hash + fuzzy). Has a Level 3 TODO: semantic dedup via pgvector
- **PipelineItemDocument.cs** — Marten document for pipeline items
- **IngestEvents.cs** — domain events for ingestion lifecycle

The pipeline currently extracts **questions only**. Explanatory content (textbook paragraphs, worked examples, concept definitions, theorems) is extracted by OCR but then discarded during segmentation. Only question-shaped text survives to become `PipelineItemDocument`.

For conversational tutoring (Task 07), the system needs a retrieval corpus of explanatory content — not just questions. This task adds a `ContentExtracted` pipeline stage that captures semantic content blocks.

---

## Objective

Add a parallel pipeline stage that extracts, chunks, and concept-links semantic content blocks from source documents. Store as Marten events for audit trail and downstream embedding (Task 06).

---

## Files to Read First (MANDATORY)

| File | Path | Why |
|------|------|-----|
| IngestionOrchestrator | `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` | Understand the pipeline flow |
| IngestEvents | `src/actors/Cena.Actors/Ingest/IngestEvents.cs` | Existing event patterns |
| QuestionSegmenter | `src/actors/Cena.Actors/Ingest/QuestionSegmenter.cs` | Current segmentation logic — content extraction runs AFTER this, on non-question segments |
| DeduplicationService | `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Level 3 semantic dedup TODO at top — this task enables it |
| PipelineItemDocument | `src/actors/Cena.Actors/Ingest/PipelineItemDocument.cs` | Existing document pattern |
| IConceptGraphCache | Search for this interface | Prerequisites, concept names — needed for concept linking |

---

## Implementation

### 1. New Domain Event: `ContentExtracted_V1`

Add to `IngestEvents.cs`:

```csharp
public sealed record ContentExtracted_V1(
    string ContentBlockId,          // Guid-based unique ID
    string SourceDocId,             // Which document this came from
    string ContentType,             // "definition", "theorem", "example", "explanation", "exercise_solution"
    string RawText,                 // Original extracted text
    string ProcessedText,           // Cleaned, structured (Markdown)
    IReadOnlyList<string> ConceptIds,  // Linked concepts from curriculum graph
    string Language,                // "he", "ar", "en"
    string? PageRange,              // "12-13" from source doc
    string Subject,
    string Topic,
    DateTimeOffset Timestamp);
```

### 2. Chunking Strategy

Split OCR output into semantically meaningful blocks — NOT arbitrary token windows:

- **Heading boundaries**: Hebrew textbooks use numbered sections (1.2, 1.2.1). Split on section changes.
- **Paragraph breaks**: Double newlines indicate topic shifts.
- **Math notation boundaries**: Do NOT split in the middle of a LaTeX equation block.
- **Content type detection**: Classify each block:
  - `definition` — starts with "הגדרה:" or "تعريف:" or bold definition markers
  - `theorem` — starts with "משפט:" or "نظرية:" or theorem numbering
  - `example` — starts with "דוגמה:" or "مثال:" or "Example"
  - `explanation` — prose explaining a concept (most common)
  - `exercise_solution` — worked solution (follows question)

### 3. Concept Linking

For each content block, link to curriculum concepts:
1. Extract key terms from the block
2. Match against `IConceptGraphCache` concept names (Hebrew + Arabic + English)
3. Assign `ConceptIds` — may be multiple concepts per block
4. Confidence threshold: only link if term match is unambiguous

### 4. Marten Document

Create `ContentBlockDocument` for read-model queries:

```csharp
public sealed class ContentBlockDocument
{
    public string Id { get; set; }
    public string SourceDocId { get; set; }
    public string ContentType { get; set; }
    public string ProcessedText { get; set; }
    public IReadOnlyList<string> ConceptIds { get; set; }
    public string Language { get; set; }
    public string Subject { get; set; }
    public string Topic { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

### 5. Pipeline Integration

Add content extraction as a **parallel stage** after OCR, running alongside `QuestionSegmenter`:
- OCR output → QuestionSegmenter (existing: extracts questions)
- OCR output → ContentExtractor (NEW: extracts everything that's NOT a question)

This means the same OCR output feeds both pipelines. No re-processing needed.

---

## What NOT to Do

- Do NOT implement embeddings — that's Task 06
- Do NOT modify `GeminiOcrClient` or `MathpixClient` — add a new stage after OCR
- Do NOT modify `QuestionSegmenter` — run content extraction in parallel, on non-question text
- Do NOT create a new actor for content extraction — it's a service called by `IngestionOrchestrator`
- Do NOT store raw PDF bytes — only extracted and processed text

---

## Verification Checklist

- [ ] Ingest a sample PDF → `ContentExtracted_V1` events emitted
- [ ] Content blocks correctly typed (definition, theorem, example, explanation)
- [ ] Concept linking produces correct `ConceptIds`
- [ ] Hebrew content blocks preserve RTL text correctly
- [ ] Arabic content blocks preserve RTL text correctly
- [ ] Math LaTeX notation preserved (not split mid-equation)
- [ ] `ContentBlockDocument` queryable in Marten
- [ ] Existing question ingestion pipeline unaffected (no regression)
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
