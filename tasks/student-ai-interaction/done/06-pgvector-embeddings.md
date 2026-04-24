# Task 06: pgvector + Embedding Pipeline

**Effort**: 3-4 days | **Track**: E (after Task 05) | **Depends on**: Task 05 (content blocks) | **Blocks**: 07

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Marten on PostgreSQL. After Task 05, the system has `ContentBlockDocument` records with explanatory content linked to concepts.

The platform already runs PostgreSQL (Marten event store + read models). pgvector is a PostgreSQL extension — no new infrastructure needed.

`DeduplicationService.cs` has a Level 3 TODO for semantic deduplication via pgvector. This task fulfills that TODO and creates the embedding pipeline for RAG-based tutoring (Task 07).

---

## Objective

Enable pgvector in PostgreSQL. Create an embedding pipeline that vectorizes content blocks. Provide semantic search for RAG retrieval. Fulfill the Level 3 semantic dedup TODO.

---

## Files to Read First (MANDATORY)

| File | Path | Why |
|------|------|-----|
| DeduplicationService | `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Level 3 semantic dedup TODO — pgvector fulfills this |
| ContentBlockDocument | (Task 05 output) | Documents to embed |
| PostgreSQL connection | Check `appsettings.json` or Marten configuration | Verify pgvector can be enabled |

---

## Implementation

### 1. Enable pgvector Extension

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

Add to Marten's database migration or a separate migration script.

### 2. Embedding Table

```sql
CREATE TABLE content_embeddings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    content_block_id TEXT NOT NULL REFERENCES mt_doc_contentblockdocument(id),
    embedding vector(1536) NOT NULL,
    concept_ids TEXT[] NOT NULL,
    language TEXT NOT NULL,
    subject TEXT NOT NULL,
    content_type TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- HNSW index for fast similarity search
CREATE INDEX idx_content_embeddings_hnsw
    ON content_embeddings USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- Filter indexes for scoped search
CREATE INDEX idx_content_embeddings_concept ON content_embeddings USING GIN (concept_ids);
CREATE INDEX idx_content_embeddings_subject ON content_embeddings (subject);
```

### 3. Embedding Service

```csharp
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbedding(string text);
    Task EmbedContentBlock(ContentBlockDocument block);
    Task<IReadOnlyList<SimilarContent>> SearchSimilar(
        string queryText,
        string? subjectFilter = null,
        string[]? conceptFilter = null,
        int limit = 5,
        float minSimilarity = 0.7f);
}

public record SimilarContent(
    string ContentBlockId,
    string ProcessedText,
    string ContentType,
    float Similarity);
```

### 4. Embedding Model

Use a cost-effective embedding model. Options in order of preference:
1. Anthropic Voyage embeddings (if available via SDK)
2. OpenAI `text-embedding-3-small` (1536 dims, cheap)
3. Local sentence-transformers model (zero API cost, higher latency)

Pre-compute embeddings during ingestion. NEVER embed at query time — query-time embeddings only for the search query itself.

### 5. Ingestion Pipeline Integration

After `ContentExtracted_V1` is stored, trigger embedding:
```
ContentExtracted_V1 → ContentBlockDocument stored → EmbeddingService.EmbedContentBlock()
```

This can be async via NATS event `cena.content.block.extracted`.

### 6. Semantic Dedup (Fulfill TODO)

Extend `DeduplicationService` with Level 3:
```csharp
// Level 3: Semantic dedup via pgvector
var similar = await _embeddingService.SearchSimilar(
    processedText, subjectFilter: subject, minSimilarity: 0.95f);
if (similar.Any())
    return DeduplicationResult.NearDuplicate(similar.First().ContentBlockId);
```

### 7. RAG Search Interface

For Task 07 (TutorActor), provide a search method:
```csharp
// Find relevant content for a student's question about a concept
var results = await _embeddingService.SearchSimilar(
    queryText: studentQuestion,
    conceptFilter: new[] { currentConceptId },
    subjectFilter: "mathematics",
    limit: 3,
    minSimilarity: 0.7f);
```

---

## What NOT to Do

- Do NOT add Redis VSS (Vector Similarity Search) — pgvector is sufficient, avoids another data store
- Do NOT embed at query time for content blocks — pre-compute during ingestion
- Do NOT use a separate vector database (Pinecone, Weaviate, FAISS) — pgvector in existing PostgreSQL
- Do NOT create a new PostgreSQL database — use the existing Marten database with a new schema/table
- Do NOT embed questions — questions have explicit concept IDs. Structured lookup beats semantic search for questions.

---

## Verification Checklist

- [ ] pgvector extension enabled in PostgreSQL
- [ ] Content blocks embedded and stored in `content_embeddings` table
- [ ] HNSW index created and functional
- [ ] Semantic search returns relevant results (cosine similarity > 0.7)
- [ ] Concept-filtered search narrows results correctly
- [ ] Level 3 semantic dedup catches near-duplicate content blocks (>0.95 similarity)
- [ ] Embedding happens asynchronously (doesn't block ingestion)
- [ ] Existing question pipeline and dedup unaffected
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
