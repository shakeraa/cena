# ADM-024: Existing Page Enhancements for SAI Features

**Priority:** P2 — polish existing pages to reflect new backend capabilities
**Blocked by:** None
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Several existing admin pages need updates to reflect new backend features from SAI-001 through SAI-009. These are enhancement tasks to existing pages, not new pages.

## Subtasks

### ADM-024.1: Questions List — Creation Method & Explanation Status

**Files to modify:**

- `src/admin/full-version/src/pages/apps/questions/list/index.vue`

**Acceptance:**

- [ ] New column: "Source" showing creation method — AI Generated, Manual, Ingested (from content extraction)
- [ ] Source chip color: purple (AI), blue (Manual), teal (Ingested)
- [ ] New column: "Explanation" showing status — Present (green), Missing (red), Draft (yellow)
- [ ] Filter by source type and explanation status
- [ ] Bulk action: "Generate Explanations" for selected questions without explanations (calls AI generation)

### ADM-024.2: Moderation Queue — Semantic Dedup Alerts

**Files to modify:**

- `src/admin/full-version/src/pages/apps/moderation/queue/index.vue`
- `src/admin/full-version/src/views/apps/moderation/queue/` — add DuplicateAlert component if needed

**Acceptance:**

- [ ] If a moderation item has near-duplicate content (from embedding similarity), show alert badge
- [ ] Click badge opens side panel with duplicate comparison (existing vs duplicate, similarity score)
- [ ] Actions: "Merge" (keep best version), "Keep Both", "Reject Duplicate"
- [ ] Requires new field in moderation queue API response — `duplicateOf: { itemId, similarity }` (backend addition to ContentModerationService)

### ADM-024.3: AI Settings — Cache & Budget Configuration

**Files to modify:**

- `src/admin/full-version/src/pages/apps/system/ai-settings.vue`

**Acceptance:**

- [ ] New section: "Explanation Cache Settings"
  - L2 TTL (hours): number input, default 24
  - L2 max entries: number input, default 10000
  - Quality gate minimum score: slider 0-1, default 0.6
- [ ] New section: "Token Budget"
  - Daily limit per student: number input (tokens)
  - Monthly total limit: number input (tokens)
  - Model cost display: show $/1K tokens for configured model
- [ ] Save updates both AI settings and token budget limits via respective endpoints
- [ ] Changes require `manage` permission on `Settings` subject

### ADM-024.4: Event Stream — Type Filtering & Source Tags

**Files to modify:**

- `src/admin/full-version/src/pages/apps/system/events.vue`

**Acceptance:**

- [ ] Add event type filter dropdown: All, Mastery, Focus, Tutoring, Explanation, Embedding, Hint, Experiment
- [ ] Add source tag chips on each event: which actor/service produced it
- [ ] Highlight new SAI event types with distinct colors:
  - TutoringSessionStarted/Completed: blue
  - ExplanationGenerated/Cached: purple
  - EmbeddingIndexed: teal
  - ExperimentCohortAssigned: orange
  - HintDelivered: green
  - ConfusionDetected: red
- [ ] Click event row expands to show full event payload (JSON viewer)

### ADM-024.5: Main Dashboard — SAI Feature Cards

**Files to modify:**

- `src/admin/full-version/src/views/admin/dashboard/` — add new cards or modify existing

**Acceptance:**

- [ ] "AI Activity" card: today's AI generations (explanations + hints + tutoring turns)
- [ ] "Content Corpus" card: total questions, total embedded blocks, extraction queue size
- [ ] "Active Experiments" card: count of running experiments, link to experiments page
- [ ] Cards use real data from respective admin API endpoints
- [ ] Cards positioned in dashboard layout alongside existing cards (focus, mastery, content pipeline)

### ADM-024.6: Architecture Page — SAI Components

**Files to modify:**

- `src/admin/full-version/src/pages/apps/system/architecture.vue`

**Acceptance:**

- [ ] Add TutorActor to actor system diagram (shows lifecycle: idle → active → budget_check → responding → completed)
- [ ] Add explanation cache hierarchy diagram: L1 (Marten) → L2 (Redis) → L3 (LLM) with fallback arrows
- [ ] Add embedding pipeline diagram: Upload → Extract → Chunk → Embed → Index → Search
- [ ] Add experiment framework flow: Assignment → Treatment/Control → Observation → Analysis
- [ ] Use existing Mermaid/D3 rendering approach from current architecture page
