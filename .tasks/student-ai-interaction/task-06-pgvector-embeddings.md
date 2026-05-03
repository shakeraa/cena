# Task 06: pgvector + Embedding Pipeline

**Track**: E (after Task 05)
**Effort**: 3-4 days
**Depends on**: Task 05 (content extraction)
**Blocks**: Task 07

---

## System Context

Cena is an event-sourced .NET platform running on PostgreSQL (via Marten), Redis, and NATS. After Task 05, the ingestion pipeline produces `ContentExtracted_V1` events containing semantic content blocks (definitions, theorems, examples, etc.) tagged with concept IDs and content types.

This task adds vector embeddings to those content blocks for semantic search. The platform already runs PostgreSQL — pgvector is a native extension, requiring zero new infrastructure. `DeduplicationService` has a Level 3 semantic dedup TODO referencing vector similarity that this task fulfills.

The embeddings enable:
1. **Semantic search** for RAG-based tutoring (Task 07) — find relevant content given a student's confusion context
2. **Semantic deduplication** — detect near-duplicate content blocks across different source documents
3. **Concept similarity** — discover unstated relationships between concepts based on content overlap

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Find "Level 3" TODO | Semantic dedup placeholder — this task fulfills it |
| `src/actors/Cena.Actors/Ingest/IngestEvents.cs` | Find `ContentExtracted_V1` | Content blocks from Task 05 — these are the embedding targets |
| PostgreSQL connection config | `Program.cs` or `appsettings.json` | Verify PostgreSQL version supports pgvector (PG 12+). Check connection string. |
| Marten configuration | `Program.cs` in Actors.Host | How Marten connects to PostgreSQL — embeddings use the same database |

---

## Implementation Requirements

### 1. Enable pgvector Extension

Create a migration or startup hook:
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

This runs on the same PostgreSQL instance that Marten uses. No new database.

### 2. Content Embeddings Table

```sql
CREATE TABLE content_embeddings (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    content_block_id TEXT NOT NULL,              -- from ContentBlock.BlockId
    pipeline_item_id TEXT NOT NULL,              -- source document
    embedding       vector(1536) NOT NULL,       -- 1536 dimensions (text-embedding-3-small)
    content_type    TEXT NOT NULL,               -- Definition, Theorem, Example, etc.
    concept_ids     TEXT[] NOT NULL DEFAULT '{}', -- array of linked concept IDs
    language        TEXT NOT NULL,               -- he, ar, en
    text_preview    TEXT NOT NULL,               -- first 200 chars for debugging
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT uq_content_block UNIQUE (content_block_id)
);
```

### 3. HNSW Index

```sql
CREATE INDEX idx_content_embeddings_hnsw
ON content_embeddings
USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 64);
```

HNSW (not IVFFlat) because:
- No need to pre-define number of clusters
- Better recall at low latency for our corpus size (expected <1M blocks)
- Supports incremental inserts without re-indexing

### 4. Embedding Service

**Location**: `src/actors/Cena.Actors/Services/EmbeddingService.cs`

```csharp
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
    Task<IReadOnlyList<ContentSearchResult>> SearchSimilarAsync(
        string query,
        SearchFilter? filter = null,
        int limit = 5,
        float minSimilarity = 0.7f,
        CancellationToken ct = default);
}

public sealed record SearchFilter(
    IReadOnlyList<string>? ConceptIds = null,   // filter by concept
    string? ContentType = null,                  // filter by type
    string? Language = null                       // filter by language
);

public sealed record ContentSearchResult(
    string ContentBlockId,
    string PipelineItemId,
    string ContentType,
    IReadOnlyList<string> ConceptIds,
    string TextPreview,
    float Similarity);
```

### 5. Embedding Model

Use OpenAI's `text-embedding-3-small` (1536 dimensions, $0.02/1M tokens) via the OpenAI .NET SDK. Reasons:
- Anthropic does not offer an embedding model
- `text-embedding-3-small` has strong multilingual support (Hebrew, Arabic, English)
- 1536 dimensions is the standard pgvector sweet spot

API key from `IConfiguration["OpenAI:ApiKey"]`. Wire through configuration, never hardcoded.

**Alternative**: If OpenAI dependency is unacceptable, use an open-source model (e.g., `sentence-transformers/all-MiniLM-L6-v2` via ONNX runtime). Discuss with architect before deciding.

### 6. Pipeline Integration

After `ContentExtracted_V1` is emitted (Task 05), embed all content blocks:

```
ContentExtracted_V1 event → EmbeddingService.EmbedBatchAsync(blocks) → INSERT into content_embeddings
```

- Batch embed: group blocks into batches of 100 for API efficiency
- Run as a Marten projection or NATS consumer — subscribe to `cena.ingest.content.extracted`
- Idempotent: `ON CONFLICT (content_block_id) DO UPDATE SET embedding = EXCLUDED.embedding`

### 7. Semantic Deduplication (Fulfill Level 3 TODO)

Update `DeduplicationService` to use vector similarity for Level 3 dedup:

```
For each new content block:
  1. Embed the block
  2. Search for similar existing blocks (cosine similarity > 0.95)
  3. If near-duplicate found: flag but do NOT auto-delete (different source docs may have slightly different phrasings that are pedagogically valuable)
  4. Store dedup results in the ContentExtracted event or a follow-up event
```

### 8. Similarity Search for RAG

The `SearchSimilarAsync` method is the primary interface for Task 07 (TutorActor). It must support:
- **Concept-scoped search**: filter by concept IDs so tutoring stays on-topic
- **Content type filtering**: prefer Explanations and WorkedExamples for tutoring, Definitions for reference
- **Language filtering**: serve content in the student's language
- **Sub-100ms latency**: HNSW index guarantees this for <1M vectors

---

## What NOT to Do

- Do NOT add Redis VSS — pgvector is sufficient and avoids a new data store
- Do NOT embed at query time — pre-compute during ingestion
- Do NOT auto-delete near-duplicate content — flag only, let humans decide
- Do NOT use Marten for embedding storage — raw SQL/Dapper for the embeddings table (Marten is for event-sourced aggregates)
- Do NOT create a separate database — use the existing PostgreSQL instance
- Do NOT implement RAG retrieval logic — that's Task 07. This task provides the search interface.

---

## Verification Checklist

- [ ] pgvector extension enabled on PostgreSQL
- [ ] `content_embeddings` table created with HNSW index
- [ ] Ingest content → embeddings stored (verify with `SELECT count(*) FROM content_embeddings`)
- [ ] Cosine similarity search returns relevant results (threshold > 0.7)
- [ ] Concept-scoped search filters correctly (query for concept X returns only blocks linked to X)
- [ ] Language filter works (Hebrew query returns Hebrew blocks)
- [ ] Search latency < 100ms for 10K+ vectors
- [ ] Near-duplicate detection flags blocks with cosine > 0.95
- [ ] Idempotent re-embedding: same content block re-processed without errors
- [ ] `DeduplicationService` Level 3 TODO replaced with real implementation
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
