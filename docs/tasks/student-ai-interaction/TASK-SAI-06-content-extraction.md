# TASK-SAI-06: Content Extraction Pipeline Stage

**Priority**: MEDIUM — prerequisite for Tier 3 conversational tutoring
**Effort**: 5-7 days
**Depends on**: Nothing (extends existing pipeline)
**Track**: E (independent of Tracks C + D)

---

## Context

The ingestion pipeline (`src/actors/Cena.Actors/Ingest/`) currently extracts **questions only** from OCR'd documents. Explanatory text, worked examples, definitions, theorems, and narrative content are discarded during the `QuestionSegmenter` stage.

For Tier 3 conversational tutoring (TASK-SAI-08), students need to ask "why?" and get answers grounded in curriculum content. This requires a **content corpus** — the explanatory text that surrounds questions in textbooks and worksheets.

### Existing Pipeline Stages

| Stage | Event | File | Status |
|-------|-------|------|--------|
| File received | `FileReceived_V1` | `IngestEvents.cs` | Exists |
| OCR completed | `OcrCompleted_V1` | `IngestEvents.cs` | Exists |
| Questions segmented | `QuestionsSegmented_V1` | `IngestEvents.cs` | Exists |
| Questions normalized | `QuestionsNormalized_V1` | `IngestEvents.cs` | Exists |
| **Content extracted** | **`ContentExtracted_V1`** | — | **NEW** |

The `DeduplicationService` (`src/actors/Cena.Actors/Ingest/DeduplicationService.cs`) has a Level 3 TODO comment for semantic dedup via pgvector/Redis VSS — this task creates the content that will eventually be deduplicated and embedded.

---

## Architecture

### New Event

**File**: `src/actors/Cena.Actors/Events/IngestEvents.cs`

```csharp
public sealed record ContentExtracted_V1(
    string PipelineItemId,
    IReadOnlyList<ExtractedContent> Segments,
    DateTimeOffset Timestamp);

public sealed record ExtractedContent(
    string ContentId,          // unique ID for this segment
    string Text,               // raw text content
    string TextHtml,           // HTML-formatted (math/diagrams preserved)
    ContentType Type,          // Definition, Theorem, WorkedExample, Explanation, Narrative
    string? AssociatedConceptId,  // if identifiable
    string? AssociatedQuestionId, // if this content is adjacent to a segmented question
    string Language,           // he/ar/en
    string Subject,
    string? Topic,
    int PageNumber,
    float Confidence);         // LLM confidence in classification

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
```

### Content Segmenter

**Create**: `src/actors/Cena.Actors/Ingest/ContentSegmenter.cs`

Uses the same Gemini OCR output that `QuestionSegmenter` consumes, but extracts the non-question text:

```csharp
public interface IContentSegmenter
{
    Task<IReadOnlyList<ExtractedContent>> SegmentAsync(OcrPageResult page, string subject, CancellationToken ct);
}
```

**LLM Prompt** (Gemini — same model used for OCR):

```
Given this OCR text from a {subject} textbook page in {language}:

{pageText}

Extract all non-question content segments. For each segment, classify as:
- Definition: formal definition of a concept
- Theorem: mathematical theorem or scientific law
- WorkedExample: step-by-step solution to a problem
- Explanation: conceptual explanation or reasoning
- Narrative: contextual text, introductions, transitions
- Formula: standalone formula or equation
- Summary: chapter/section summary

For each segment, output:
- text: the content (preserve math as LaTeX)
- type: classification
- associatedConcept: the concept this relates to (if identifiable)
- pageNumber: from OCR metadata
- confidence: 0.0-1.0

Output as JSON array.
```

### Storage

Store extracted content as a Marten document (not event-sourced — content is immutable once extracted):

**Create**: `src/actors/Cena.Actors/Ingest/ContentDocument.cs`

```csharp
public sealed class ContentDocument
{
    public string Id { get; init; }          // content-{guid}
    public string PipelineItemId { get; init; }
    public string Text { get; init; }
    public string TextHtml { get; init; }
    public ContentType Type { get; init; }
    public string? AssociatedConceptId { get; init; }
    public string? AssociatedQuestionId { get; init; }
    public string Language { get; init; }
    public string Subject { get; init; }
    public string? Topic { get; init; }
    public int PageNumber { get; init; }
    public float Confidence { get; init; }
    public DateTimeOffset ExtractedAt { get; init; }
}
```

Register in Marten configuration for document storage with GIN index on `AssociatedConceptId` and `Subject`.

### Wire Into Pipeline

**Modify**: `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs`

After `QuestionsSegmented_V1`, add a parallel stage:

```csharp
// Existing: segment questions
var questions = await _questionSegmenter.SegmentAsync(ocrResult, subject, ct);

// NEW: segment content (parallel)
var content = await _contentSegmenter.SegmentAsync(ocrResult, subject, ct);

// Persist content documents
foreach (var segment in content)
    session.Store(new ContentDocument { ... });

// Emit event
session.Events.Append(pipelineItemId, new ContentExtracted_V1(pipelineItemId, content, now));
```

---

## Coding Standards

- `ContentSegmenter` follows the same pattern as `QuestionSegmenter` — LLM prompt, JSON parse, confidence threshold.
- Content with confidence < 0.5 is discarded (log at `Debug` level).
- Math expressions must be preserved as LaTeX in both `Text` and `TextHtml` fields.
- Hebrew/Arabic text direction must be preserved in HTML (`dir="rtl"`).
- Do NOT embed content in this task — that's TASK-SAI-07. This task extracts and stores raw text.
- Unit test: given a sample OCR page, verify correct segmentation into content types.
- Integration test: run through the full pipeline with a real Bagrut worksheet PDF.

---

## Acceptance Criteria

1. `ContentExtracted_V1` event emitted during ingestion pipeline
2. `ContentSegmenter` classifies text into 8 content types
3. Extracted content stored as Marten documents with concept/question associations
4. Math expressions preserved as LaTeX
5. Hebrew/Arabic RTL preserved in HTML
6. Low-confidence segments (< 0.5) discarded
7. Pipeline runs content extraction in parallel with question segmentation (no added latency on the critical path)
8. At least 10 sample documents processed successfully in integration tests
