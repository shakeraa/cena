# ADM-020: Embedding Corpus Admin

**Priority:** P1 — management UI for SAI-008 pgvector embeddings
**Blocked by:** None (SAI-008 pgvector + HNSW index is complete)
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Admin API (.NET 9)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

SAI-008 added pgvector embeddings with HNSW indexing for semantic search over extracted content blocks. The EmbeddingService supports `SearchSimilar()` with concept/subject filtering. Admins have no way to see corpus coverage, test retrieval quality, or review near-duplicate content. This task adds an embedding corpus admin page.

## Backend: New Admin API Endpoints

### ADM-020.1: EmbeddingAdminService + Endpoints

**Files to create:**

- `src/api/Cena.Admin.Api/EmbeddingAdminDtos.cs`
- `src/api/Cena.Admin.Api/EmbeddingAdminService.cs`

**Files to modify:**

- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` — add `MapEmbeddingAdminEndpoints`

**Endpoints:**

```
GET  /api/admin/embeddings/corpus-stats
     Returns: Total blocks, by subject, by concept, avg embedding dimension, index size

POST /api/admin/embeddings/search
     Body: { query, topK, subjectFilter?, conceptFilter?, similarityThreshold? }
     Returns: Ranked results with similarity scores, content preview, source metadata

GET  /api/admin/embeddings/duplicates
     Query: ?threshold=0.95&page=1&pageSize=20
     Returns: Near-duplicate pairs above similarity threshold for review

POST /api/admin/embeddings/reindex
     Body: { scope: "all"|"subject"|"concept", filter? }
     Returns: { jobId, estimatedBlocks }
     (Fires background job via NATS)
```

**Data source:** Direct pgvector queries via `IContentVectorStore`. Corpus stats from `SELECT count(*), subject, concept FROM content_embeddings GROUP BY ...`. Duplicates via self-join with cosine similarity.

**Acceptance:**

- [ ] Corpus stats returns real counts from `content_embeddings` table
- [ ] Search endpoint calls `EmbeddingService.SearchSimilar()` — same path students use
- [ ] Duplicates query uses pgvector `<=>` operator with threshold
- [ ] Reindex publishes NATS message to `embedding.reindex` subject (handled by EmbeddingIngestionHandler)
- [ ] All endpoints require `SuperAdminOnly` auth policy

### ADM-020.2: Embedding Corpus Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/system/embeddings.vue`
- `src/admin/full-version/src/views/apps/system/embeddings/CorpusStatsCards.vue`
- `src/admin/full-version/src/views/apps/system/embeddings/SemanticSearchTester.vue`
- `src/admin/full-version/src/views/apps/system/embeddings/DuplicateReviewTable.vue`

**Acceptance:**

- [ ] Stat cards: Total Blocks, Subjects Covered, Concepts Covered, Index Size (MB)
- [ ] Coverage heatmap: subject × concept matrix showing block count density
- [ ] Semantic search tester: text input + filters → ranked results with similarity scores
- [ ] Each result shows: content preview (first 200 chars), source (file/page), similarity score, subject, concept
- [ ] Duplicate review table: pairs of near-duplicates with side-by-side preview + "Merge" / "Keep Both" actions
- [ ] Reindex button with scope selector and confirmation dialog

### ADM-020.3: Navigation

**Files to modify:**

- `src/admin/full-version/src/navigation/vertical/apps-and-pages.ts`

**Acceptance:**

- [ ] Add "Embeddings" under System heading
- [ ] Icon: `tabler-vector`
- [ ] CASL subject: `System`, action: `manage`
