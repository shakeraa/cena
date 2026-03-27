# ADM-009: Content Ingestion Pipeline Dashboard

**Priority:** P1 — visibility into content pipeline
**Blocked by:** ADM-001 (auth), ADM-003 (moderator role), CNT-008 (ingestion backend)
**Estimated effort:** 4 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Vuexy Kanban
**Contract:** `docs/question-ingestion-specification.md`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The ingestion pipeline (CNT-008) processes external content through multiple stages: incoming files, OCR, segmentation, normalization, classification, deduplication, re-creation, review, and publishing. The admin dashboard needs a kanban-style pipeline view showing items at each stage with drill-down details.

Reuse Vuexy's `apps/kanban/` components for the pipeline visualization.

## Subtasks

### ADM-009.1: Pipeline Kanban View

**Files to create:**

- `src/admin/full-version/src/pages/apps/ingestion/pipeline.vue`
- `src/admin/full-version/src/views/apps/ingestion/` — pipeline components

**Kanban Columns (pipeline stages):**

1. Incoming (files in S3 `incoming/`)
2. OCR Processing
3. Segmented
4. Normalized
5. Classified
6. Deduplicated
7. Re-Created
8. In Review
9. Published

**Acceptance:**

- [ ] Each column shows item count and items as cards
- [ ] Card shows: source filename, source type (URL/S3/photo/batch), question count extracted, quality score, timestamp
- [ ] Color coding: green (healthy), yellow (slow), red (failed/stuck)
- [ ] Auto-refresh via SignalR for real-time pipeline updates
- [ ] Failed items highlighted with error badge

### ADM-009.2: Item Detail Side Panel

**Files to create:**

- `src/admin/full-version/src/views/apps/ingestion/ItemDetailPanel.vue`

**Acceptance:**

- [ ] Click a card to open side drawer with full details
- [ ] OCR output preview (original image + extracted text)
- [ ] Quality scores breakdown: math correctness, language quality, pedagogical quality, plagiarism score
- [ ] Processing time per stage
- [ ] Error details for failed items

### ADM-009.3: Pipeline Actions

**Acceptance:**

- [ ] Retry failed items (single or bulk)
- [ ] Move item to review manually
- [ ] Reject item with reason
- [ ] Upload new files (drag-and-drop or file picker)
- [ ] Submit URL for ingestion

### ADM-009.4: Pipeline Analytics

**Files to create:**

- `src/admin/full-version/src/views/apps/ingestion/PipelineStats.vue`

**Acceptance:**

- [ ] Throughput chart: items processed per hour/day
- [ ] Failure rate by stage
- [ ] Average processing time per stage
- [ ] Queue depth trend (are we keeping up with incoming volume?)

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| GET | `/api/admin/ingestion/pipeline-status` | Counts per stage, items per stage |
| GET | `/api/admin/ingestion/items?stage={stage}&page=1` | Items in a specific stage |
| GET | `/api/admin/ingestion/items/{id}/detail` | Full item detail |
| POST | `/api/admin/ingestion/items/{id}/retry` | Retry failed item |
| POST | `/api/admin/ingestion/items/{id}/reject` | Reject with reason |
| POST | `/api/admin/ingestion/upload` | File upload |
| POST | `/api/admin/ingestion/url` | URL submission |
| GET | `/api/admin/ingestion/stats` | Pipeline throughput metrics |

## Test

- [ ] Kanban columns reflect real pipeline stage counts
- [ ] Real-time updates via SignalR when items move between stages
- [ ] Retry failed item moves it back to appropriate stage
- [ ] File upload triggers pipeline processing
- [ ] URL submission downloads and begins processing
- [ ] Arabic/Hebrew filenames handled correctly
