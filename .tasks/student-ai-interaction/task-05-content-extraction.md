# Task 05: Content Extraction Pipeline Stage

**Track**: E (independent of Tasks 02-04)
**Effort**: 5-7 days
**Depends on**: Existing ingest pipeline
**Blocks**: Task 06

---

## System Context

Cena is an event-sourced .NET educational platform with a content ingestion pipeline at `src/actors/Cena.Actors/Ingest/`. The pipeline processes uploaded files (PDFs, images, URLs) through sequential stages tracked by `PipelineItemDocument`:

```
FileReceived → OcrCompleted → QuestionsSegmented → QuestionsNormalized →
QuestionsClassified → DeduplicationCompleted → QuestionsRecreated →
MovedToReview → PipelineCompleted
```

Each stage is a domain event in `IngestEvents.cs` (lines 9-89). The pipeline currently extracts **questions only** — it does not extract explanatory content (definitions, theorems, worked examples, proofs, chapter summaries). This means there is no retrieval corpus for RAG-based tutoring (Task 07).

The OCR layer (`GeminiOcrClient` + `MathpixClient` fallback) already produces structured text with LaTeX math expressions. The `QuestionSegmenter` splits OCR output into individual questions. What's needed is a parallel segmenter that extracts **semantic content blocks** for the knowledge base.

`DeduplicationService` has a Level 3 semantic dedup TODO referencing pgvector/Redis VSS — the embeddings from Task 06 will fulfill this.

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/actors/Cena.Actors/Ingest/IngestEvents.cs` | 9-89 | All 10 pipeline events — understand the event flow and naming conventions |
| `src/actors/Cena.Actors/Ingest/PipelineItemDocument.cs` | 12-24 | `PipelineStage` enum — understand stage progression |
| `src/actors/Cena.Actors/Ingest/PipelineItemDocument.cs` | 30-97 | Document structure, `OcrResult` with pages + math expressions |
| `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` | Full | Pipeline orchestration — understand how stages are chained and events emitted |
| `src/actors/Cena.Actors/Ingest/QuestionSegmenter.cs` | Full | How OCR text is segmented into questions — analogous to content segmentation |
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Find "Level 3" TODO | Semantic dedup placeholder referencing vector similarity |
| `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` | Full | OCR output format — structured text with math expressions |

---

## Implementation Requirements

### 1. New Domain Event: `ContentExtracted_V1`

Add to `IngestEvents.cs` following the existing naming convention:

```csharp
public sealed record ContentExtracted_V1(
    string PipelineItemId,          // links to source document
    IReadOnlyList<ContentBlock> Blocks,
    DateTimeOffset Timestamp);

public sealed record ContentBlock(
    string BlockId,                 // deterministic: SHA-256 of (PipelineItemId + pageNumber + blockIndex)
    ContentBlockType Type,          // Definition, Theorem, Example, Exercise, Explanation, Summary, Proof
    string RawText,                 // original OCR text
    string ProcessedText,           // cleaned, structured, LaTeX normalized
    IReadOnlyList<string> ConceptIds, // linked concepts from concept graph
    string Language,                // he, ar, en
    int PageNumber,
    int BlockIndex,                 // order within page
    float Confidence);              // extraction confidence 0-1
```

### 2. `ContentBlockType` Enum

```csharp
public enum ContentBlockType
{
    Definition,     // "A quadratic equation is..."
    Theorem,        // Named theorems, laws, rules
    Example,        // Worked examples with solutions
    Exercise,       // Practice problems (distinct from bank questions)
    Explanation,    // Conceptual explanations, "why" content
    Summary,        // Chapter/section summaries
    Proof           // Mathematical proofs
}
```

### 3. Create `ContentSegmenter`

**Location**: `src/actors/Cena.Actors/Ingest/ContentSegmenter.cs`

Parallel to `QuestionSegmenter` but extracts explanatory content blocks instead of questions.

**Segmentation strategy** — semantically meaningful blocks, not arbitrary token windows:
- **Heading boundaries**: Split on heading structure (H1, H2, H3 detected from formatting)
- **Paragraph breaks**: Double newline = potential block boundary
- **Math notation boundaries**: LaTeX display equations (`$$...$$`) are block boundaries — don't split mid-equation
- **Content type markers**: Detect "Definition:", "Theorem:", "Example:", "Proof:" prefixes (common in educational texts)
- **Maximum block size**: 500 tokens. If a block exceeds this, split at the nearest sentence boundary.
- **Minimum block size**: 50 tokens. Merge small blocks with adjacent blocks of the same type.

### 4. Concept Linking

Each content block must be tagged with concept IDs from the existing concept graph (`IConceptGraphCache`):
- Extract key terms from the block text
- Match against concept names and aliases in the graph
- Multiple concepts per block is normal (a worked example might reference 3 concepts)
- If no concept match: tag as `["unlinked"]` — these blocks are still useful for full-text search but won't appear in concept-scoped RAG queries

### 5. Pipeline Integration

The content extraction stage runs **after OCR** and **parallel to question segmentation**. It does NOT gate question extraction.

```
FileReceived → OcrCompleted → [QuestionSegmenter (existing)] + [ContentSegmenter (NEW)]
                                    ↓                              ↓
                              QuestionsSegmented            ContentExtracted_V1
```

Update `IngestionOrchestrator` to:
1. After `OcrCompleted`, run `ContentSegmenter` in parallel with `QuestionSegmenter`
2. Emit `ContentExtracted_V1` event
3. Update `PipelineItemDocument` with content extraction stats (block count, types, linked concepts)

### 6. New Fields on `PipelineItemDocument`

Add to track content extraction progress:

```csharp
// Content extraction results
public int ExtractedContentBlockCount { get; set; }
public Dictionary<string, int> ContentBlockTypeCounts { get; set; } = new(); // type → count
public List<string> LinkedConceptIds { get; set; } = new();
```

### 7. Event Sourcing

`ContentExtracted_V1` is stored as a Marten event for audit trail and reprocessing. Use the existing event store infrastructure — `IDocumentSession.Events.Append()` on the pipeline aggregate stream.

---

## What NOT to Do

- Do NOT implement embeddings — that's Task 06
- Do NOT modify the existing OCR pipeline (`GeminiOcrClient`) — add a new stage after OCR
- Do NOT gate question extraction on content extraction — they run in parallel
- Do NOT use LLM for segmentation — rule-based segmentation is sufficient and cost-free
- Do NOT create a new aggregate — content events belong to the pipeline item stream
- Do NOT add a new `PipelineStage` enum value — content extraction is a parallel track, not a sequential stage

---

## Verification Checklist

- [ ] Ingest a sample PDF with definitions, theorems, and examples → `ContentExtracted_V1` events emitted
- [ ] Content blocks have correct types (Definition vs Example vs Explanation)
- [ ] Concept linking produces correct concept IDs for blocks about known concepts
- [ ] Unlinked blocks tagged with `["unlinked"]`
- [ ] Math expressions preserved in LaTeX notation within blocks
- [ ] Block sizes between 50-500 tokens (no micro-fragments, no mega-blocks)
- [ ] Content extraction runs in parallel with question segmentation (not sequential)
- [ ] `PipelineItemDocument` updated with content extraction stats
- [ ] Existing pipeline unaffected — question extraction still works identically
- [ ] Hebrew/Arabic text blocks extracted correctly with RTL handling
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
