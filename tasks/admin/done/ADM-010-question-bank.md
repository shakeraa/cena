# ADM-010: Question Bank Browser

**Priority:** P1 — content management core
**Blocked by:** ADM-001 (auth), ADM-003 (permissions)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, KaTeX
**Contract:** `docs/question-ingestion-specification.md` (CenaItem schema)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The question bank is the searchable, filterable repository of all content items in Cena. Moderators review, admins manage, and teachers browse. Adapts Vuexy's `apps/ecommerce/product/list` pattern for a data-rich filterable table.

## Subtasks

### ADM-010.1: Question List with Filters

**Files to create:**

- `src/admin/full-version/src/pages/apps/questions/list/index.vue`
- `src/admin/full-version/src/views/apps/questions/` — question components

**Filters:**

- Subject (Math, Physics)
- Bloom's taxonomy level (1-6)
- Difficulty (slider range)
- Bagrut unit level (3/4/5)
- Status (draft, in-review, approved, published, deprecated)
- Language (Hebrew, Arabic, bilingual)
- Concept (autocomplete from curriculum graph)
- Source type (authored, ingested)

**Table Columns:**

- ID, stem preview (truncated), subject, concepts (chips), Bloom's level, difficulty, status, quality score, usage count, success rate

**Acceptance:**

- [ ] Server-side pagination, sorting, and filtering
- [ ] Math equations render inline in stem preview (KaTeX)
- [ ] Bulk actions: approve, deprecate, export
- [ ] Quick filters saved as presets

### ADM-010.2: Question Detail Drawer

**Files to create:**

- `src/admin/full-version/src/views/apps/questions/QuestionDetail.vue`

**Acceptance:**

- [ ] Full question rendering with KaTeX (Hebrew + Arabic)
- [ ] Answer options with correct answer highlighted
- [ ] Distractor analysis (why each wrong answer is wrong)
- [ ] Concept mapping: which curriculum nodes this question covers
- [ ] Student performance stats: times served, accuracy rate, avg time, discrimination index
- [ ] Provenance chain: source item that inspired this question (if ingested)
- [ ] Edit button (for admins): inline editing with LaTeX support

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| GET | `/api/admin/questions` | Paginated, filterable question list |
| GET | `/api/admin/questions/{id}` | Full question detail |
| GET | `/api/admin/questions/{id}/performance` | Usage and performance stats |
| PUT | `/api/admin/questions/{id}` | Update question |
| POST | `/api/admin/questions/{id}/deprecate` | Deprecate question |
| GET | `/api/admin/questions/concepts` | Concept autocomplete for filters |

## Test

- [ ] List loads with paginated data, filters work server-side
- [ ] KaTeX renders math in both Hebrew and Arabic correctly
- [ ] Performance stats match actual student interaction data
- [ ] Deprecating a question removes it from serving but keeps history
- [ ] Concept filter narrows to questions mapped to that curriculum node
