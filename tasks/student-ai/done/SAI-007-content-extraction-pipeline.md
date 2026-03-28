# SAI-007: Content Extraction Pipeline Stage

**Priority:** P2 — enables Tier 3 conversational tutoring
**Blocked by:** None (can start in parallel with Track A)
**Estimated effort:** 5-7 days
**Stack:** .NET 9, Marten event sourcing, Gemini 2.5 Flash OCR, NATS

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The ingestion pipeline (`PipelineItemDocument`) has 10 stages but extracts **questions only** — no explanatory text, worked examples, concept definitions, or reference material. Even question options are empty at ingestion (Bagrut math is open-ended).

For Tier 3 conversational tutoring to work, we need a retrieval corpus of explanatory content. This task adds a parallel pipeline stage that extracts explanatory content from the same source documents the question pipeline processes.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` | Main pipeline orchestrator — add new stage |
| `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` | OCR client — reuse for content extraction |
| `src/actors/Cena.Actors/Ingest/QuestionSegmenter.cs` | Question segmentation — model the content segmenter similarly |
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Level 3 semantic dedup TODO — connects here |
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | Event record patterns to follow |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | Register new events and document types |

## Subtasks

### SAI-007.1: ContentExtracted Event and Document Types

**Files to create:**
- `src/actors/Cena.Actors/Events/ContentEvents.cs`
- `src/actors/Cena.Actors/Content/ContentDocument.cs`

**Implementation:**

Define the Marten event and document for extracted explanatory content:

```csharp
public sealed record ContentExtracted_V1(
    string ContentId,           // SHA-256 of content text
    string SourceDocId,         // Links to PipelineItemDocument
    string ContentType,         // "explanation", "definition", "worked_example", "theorem", "proof"
    string Text,                // Extracted content (Markdown with LaTeX)
    string? Subject,
    string? Topic,
    IReadOnlyList<string> ConceptIds,  // Mapped concepts (may be empty initially)
    string Language,
    int PageNumber,
    float ExtractionConfidence,
    string ExtractedBy,         // "gemini-2.5-flash" etc.
    DateTimeOffset ExtractedAt);

public sealed class ContentDocument
{
    public string Id { get; set; }           // ContentId
    public string SourceDocId { get; set; }
    public string ContentType { get; set; }
    public string Text { get; set; }
    public string? Subject { get; set; }
    public string? Topic { get; set; }
    public List<string> ConceptIds { get; set; } = new();
    public string Language { get; set; }
    public float ExtractionConfidence { get; set; }
    public DateTimeOffset ExtractedAt { get; set; }
}
```

Register in `MartenConfiguration.cs` under content events.

**Acceptance:**
- [ ] `ContentExtracted_V1` registered in Marten event store
- [ ] `ContentDocument` registered as Marten document type
- [ ] Content-addressed by SHA-256 (same dedup pattern as questions)
- [ ] NATS subject: `cena.ingest.content.extracted`

---

### SAI-007.2: Content Segmenter Service

**Files to create:**
- `src/actors/Cena.Actors/Ingest/ContentSegmenter.cs`

**Implementation:**

Similar to `QuestionSegmenter`, but extracts non-question content:

1. Takes OCR output (already produced by existing pipeline `OcrProcessing` stage)
2. Calls Gemini 2.5 Flash with a content-extraction prompt (NOT question extraction)
3. Classifies content blocks as: explanation, definition, worked_example, theorem, proof, or discard
4. Maps to concept IDs where possible (using concept name matching against `IConceptGraphCache`)
5. Returns structured `ContentBlock[]`

Prompt must explicitly instruct: "Extract explanatory content, definitions, theorems, worked examples, and proofs. Do NOT extract questions or exercises — those are handled separately."

**Acceptance:**
- [ ] Reuses existing `GeminiOcrClient` for LLM calls (no new API client)
- [ ] Content types: explanation, definition, worked_example, theorem, proof
- [ ] Concept mapping via name/keyword matching (not semantic — pgvector comes in SAI-008)
- [ ] Confidence score per extraction (>0.7 threshold to keep)
- [ ] Hebrew and Arabic RTL support (same as existing OCR)
- [ ] Max 5000 chars per content block

---

### SAI-007.3: Integrate into Ingestion Pipeline

**Files to modify:**
- `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs`

**Implementation:**

Add content extraction as a **parallel stage** after OCR (runs alongside question segmentation):

```
OcrProcessing → [QuestionSegmenter (existing)]
             → [ContentSegmenter (NEW)]    ← parallel
```

Both stages read from the same OCR output. Content extraction emits `ContentExtracted_V1` events; question extraction continues as before.

Update `PipelineItemDocument` with content extraction results:
- `ExtractedContentCount: int`
- `ContentExtractionCompleted: bool`

**Acceptance:**
- [ ] Content extraction runs in parallel with question segmentation (not sequential)
- [ ] Existing pipeline stages unmodified
- [ ] `PipelineItemDocument` tracks content extraction status
- [ ] `PipelineCompleted_V1` waits for both question and content extraction
- [ ] Failure in content extraction does NOT block question pipeline (graceful degradation)

---

## Testing

```csharp
[Fact]
public async Task ContentSegmenter_ExtractsExplanation()
{
    var ocrOutput = new OcrPageResult
    {
        RawText = "The Pythagorean theorem states that a² + b² = c²...",
        MathExpressions = new() { { "eq1", "a^2 + b^2 = c^2" } },
        Confidence = 0.95f
    };

    var blocks = await _segmenter.ExtractContentAsync(ocrOutput, "math", "he");

    Assert.Single(blocks);
    Assert.Equal("theorem", blocks[0].ContentType);
    Assert.Contains("Pythagorean", blocks[0].Text);
}
```
