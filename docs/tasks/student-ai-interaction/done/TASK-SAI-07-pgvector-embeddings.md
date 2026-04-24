# TASK-SAI-07: pgvector + Embedding Pipeline

**Priority**: MEDIUM — enables semantic search for Tier 3
**Effort**: 3-4 days
**Depends on**: TASK-SAI-06 (content extraction)
**Track**: E (sequential after TASK-SAI-06)

---

## Context

Conversational tutoring (TASK-SAI-08) needs to retrieve relevant content passages when a student asks "why?" or "I don't understand." This requires vector embeddings stored in pgvector (PostgreSQL extension) — already available in the existing Postgres instance that runs Marten.

The `DeduplicationService` (`src/actors/Cena.Actors/Ingest/DeduplicationService.cs`) has a Level 3 TODO for semantic dedup via pgvector — this task creates that infrastructure and reuses it for RAG retrieval.

### Why pgvector (Not a Separate Vector DB)

- Marten already runs on Postgres 16 — pgvector is a single `CREATE EXTENSION`
- Corpus size will be 10K-100K documents (content segments). pgvector handles this easily with HNSW index.
- No operational overhead of Pinecone/Weaviate/FAISS for this scale
- Reassess at 1M+ documents

---

## Architecture

### Schema

```sql
-- Migration: add pgvector extension and content_embeddings table
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE content_embeddings (
    id UUID PRIMARY KEY,
    content_id TEXT NOT NULL REFERENCES mt_doc_contentdocument(id),
    embedding vector(1536),           -- OpenAI text-embedding-3-small dimension
    subject TEXT NOT NULL,
    concept_id TEXT,
    content_type TEXT NOT NULL,        -- Definition, Theorem, WorkedExample, etc.
    language TEXT NOT NULL,            -- he, ar, en
    text_preview TEXT NOT NULL,        -- first 200 chars for debugging
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- HNSW index for fast approximate nearest neighbor search
CREATE INDEX idx_content_embeddings_hnsw
    ON content_embeddings
    USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- Filtered search indexes
CREATE INDEX idx_content_embeddings_subject ON content_embeddings(subject);
CREATE INDEX idx_content_embeddings_concept ON content_embeddings(concept_id);
```

### Embedding Service

**Create**: `src/actors/Cena.Actors/Services/EmbeddingService.cs`

```csharp
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<ContentMatch>> SearchAsync(EmbeddingQuery query, CancellationToken ct);
}

public sealed record EmbeddingQuery(
    string QueryText,
    string? SubjectFilter,
    string? ConceptIdFilter,
    string? LanguageFilter,
    int TopK = 5,
    float MinSimilarity = 0.7f);

public sealed record ContentMatch(
    string ContentId,
    string Text,
    string TextHtml,
    ContentType Type,
    float Similarity);
```

**Embedding model**: Use a local embedding or a cheap API. Options in priority order:
1. `text-embedding-3-small` via OpenAI API ($0.02/1M tokens) — highest quality
2. Local sentence-transformers model if latency is critical
3. Gemini embedding API (already have the client)

### Embedding Pipeline

**Create**: `src/actors/Cena.Actors/Ingest/EmbeddingPipelineWorker.cs`

Subscribes to `ContentExtracted_V1` events via NATS JetStream (LEARNER_EVENTS or CURRICULUM_EVENTS stream). For each extracted content segment:

1. Generate embedding via `IEmbeddingService.EmbedAsync(segment.Text)`
2. Store in `content_embeddings` table via raw SQL (Npgsql with pgvector support)
3. Log: content_id, embedding dimension, latency

**Batch mode**: For initial backfill, process all existing `ContentDocument` records in batches of 100.

### Retrieval for Tutoring

**Create**: `src/actors/Cena.Actors/Services/ContentRetriever.cs`

```csharp
public interface IContentRetriever
{
    Task<IReadOnlyList<ContentMatch>> RetrieveAsync(TutoringContext context, CancellationToken ct);
}

public sealed record TutoringContext(
    string StudentQuestion,       // "Why does this formula work?"
    string CurrentQuestionStem,   // the question they're working on
    string ConceptId,
    string Subject,
    string Language);
```

Retrieval logic:
1. Embed `StudentQuestion + CurrentQuestionStem` as the query vector
2. Search with filters: `subject`, `concept_id` (if available), `language`
3. Return top 3-5 matches with similarity > 0.7
4. Prefer `WorkedExample` and `Explanation` types over `Narrative`

---

## Migration

**Create**: `src/api/Cena.Api.Host/Migrations/` (or wherever Marten migrations live)

Use Marten's `IDocumentStore.Advanced` or raw Npgsql to execute the SQL migration on startup.

Alternatively, add to `src/infra/docker/` as a SQL init script for the Postgres container.

---

## Coding Standards

- Use Npgsql's pgvector support (`Npgsql.Vectors` NuGet package) for parameterized queries. Never concatenate vectors into SQL strings.
- Embedding generation is async and must go through the circuit breaker if using an external API.
- Batch embedding: process 100 documents at a time with a 1-second delay between batches (rate limit respect).
- The `content_embeddings` table is a separate concern from Marten's document store. Use raw Npgsql, not Marten, for vector operations.
- HNSW index parameters (`m=16, ef_construction=64`) are good defaults for <100K documents. Document the tradeoff if these need tuning.
- Unit test: mock the embedding API, verify search returns correct results with cosine similarity.
- Integration test: insert 100 embeddings, search, verify top-K ordering.

---

## Acceptance Criteria

1. pgvector extension installed in Postgres
2. `content_embeddings` table with HNSW index created
3. `EmbeddingService` generates embeddings and stores them
4. `ContentRetriever` searches by semantic similarity with subject/concept/language filters
5. Embedding pipeline processes `ContentExtracted_V1` events from NATS JetStream
6. Batch backfill mode for existing content documents
7. Top-3 retrieval latency < 50ms for 10K documents
8. Similarity threshold filtering (> 0.7) prevents irrelevant results
