# SAI-008: pgvector + Embedding Pipeline

**Priority:** P2 — enables semantic search for Tier 3 RAG
**Blocked by:** SAI-007 (content extraction — needs content to embed)
**Estimated effort:** 3-4 days
**Stack:** .NET 9, PostgreSQL (pgvector), Marten, LLM ACL (embedding endpoint)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

`DeduplicationService.cs` already has a TODO comment (line 84-86): "Level 3: Semantic embedding (placeholder — requires vector DB). TODO: Integrate mE5-large embeddings with pgvector or Redis VSS. For now, skip semantic dedup until corpus > 10K items."

This task implements:
1. pgvector extension in existing PostgreSQL (Marten already runs there)
2. Embedding generation via LLM ACL
3. Similarity search for RAG retrieval in Tier 3

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Level 3 TODO — connects here |
| `src/actors/Cena.Actors/Content/ContentDocument.cs` | From SAI-007 — content to embed |
| `src/infra/docker/init-db.sql` | PostgreSQL initialization — add pgvector extension |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | Marten config — add vector column |

## Subtasks

### SAI-008.1: Enable pgvector Extension

**Files to modify:**
- `src/infra/docker/init-db.sql`

Add: `CREATE EXTENSION IF NOT EXISTS vector;`

Create embeddings table (separate from Marten — Marten doesn't natively support pgvector columns):

```sql
CREATE TABLE IF NOT EXISTS cena.content_embeddings (
    content_id TEXT PRIMARY KEY,
    embedding vector(384),    -- mE5-small dimension
    content_type TEXT NOT NULL,
    subject TEXT,
    topic TEXT,
    concept_ids TEXT[],
    language TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_content_embeddings_vector
    ON cena.content_embeddings USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);
```

**Acceptance:**
- [ ] pgvector extension enabled in init-db.sql
- [ ] Embeddings table created with vector(384) column
- [ ] IVFFlat index for cosine similarity
- [ ] Docker compose rebuilds cleanly with new extension

---

### SAI-008.2: Embedding Generation Service

**Files to create:**
- `src/actors/Cena.Actors/Embeddings/IEmbeddingService.cs`
- `src/actors/Cena.Actors/Embeddings/LlmAclEmbeddingService.cs`

**Implementation:**

Calls LLM ACL embedding endpoint to generate vector for content text:

```csharp
public interface IEmbeddingService
{
    Task<float[]> GenerateAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<(string ContentId, float[] Embedding)>> GenerateBatchAsync(
        IReadOnlyList<(string ContentId, string Text)> items, CancellationToken ct);
}
```

Model: mE5-small (384 dimensions, multilingual — supports Hebrew/Arabic). Called via LLM ACL gRPC.

**Acceptance:**
- [ ] mE5-small embeddings (384 dimensions)
- [ ] Batch API for pipeline (up to 100 items per batch)
- [ ] Single-item API for query embedding
- [ ] Circuit breaker check before LLM ACL calls
- [ ] Counter: `cena.embeddings.generated_total`

---

### SAI-008.3: Embedding Storage and Search

**Files to create:**
- `src/actors/Cena.Actors/Embeddings/IContentVectorStore.cs`
- `src/actors/Cena.Actors/Embeddings/PgVectorContentStore.cs`

**Implementation:**

```csharp
public interface IContentVectorStore
{
    Task UpsertAsync(string contentId, float[] embedding, ContentMetadata metadata, CancellationToken ct);
    Task<IReadOnlyList<ContentSearchResult>> SearchAsync(float[] queryEmbedding, int topK, ContentSearchFilter? filter, CancellationToken ct);
}

public sealed record ContentSearchResult(
    string ContentId,
    string ContentType,
    string Text,
    float Score,           // cosine similarity
    string? Subject,
    IReadOnlyList<string> ConceptIds);

public sealed record ContentSearchFilter(
    string? Subject,
    string? Language,
    IReadOnlyList<string>? ConceptIds);
```

Search uses pgvector `<=>` operator (cosine distance):
```sql
SELECT content_id, 1 - (embedding <=> @query) AS score, ...
FROM cena.content_embeddings
WHERE language = @language
ORDER BY embedding <=> @query
LIMIT @topK;
```

**Acceptance:**
- [ ] Upsert is idempotent (ON CONFLICT DO UPDATE)
- [ ] Search returns top-K by cosine similarity
- [ ] Filter by subject, language, concept IDs
- [ ] Search latency < 50ms for 10K content items
- [ ] Uses Npgsql for pgvector parameter binding

---

### SAI-008.4: Pipeline Integration

**Files to modify:**
- `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs`

After `ContentExtracted_V1` events are emitted (SAI-007), generate embeddings:

1. Subscribe to `ContentExtracted_V1` events
2. Generate embedding via `IEmbeddingService`
3. Store via `IContentVectorStore.UpsertAsync()`
4. Also connect to `DeduplicationService` Level 3 (semantic dedup)

**Acceptance:**
- [ ] Embeddings generated automatically on content extraction
- [ ] Embedding generation does NOT block pipeline (async, fire-and-forget with retry)
- [ ] DeduplicationService Level 3 uses embeddings when available (cosine similarity > 0.95 = duplicate)

---

## Testing

```csharp
[Fact]
public async Task Search_ReturnsSimilarContent()
{
    // Store 3 content items about "quadratic equations"
    // Query with "how to solve quadratic equations"
    // Assert top result is the most relevant
}

[Fact]
public async Task Search_FiltersbyLanguage()
{
    // Store Hebrew and Arabic content
    // Search with language="he" filter
    // Assert only Hebrew results returned
}
```
