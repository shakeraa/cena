# ADM-022: Explanation Version & Language Management

**Priority:** P1 — API exists (`PATCH /{id}/explanation`, `POST /{id}/language-versions`), no UI
**Blocked by:** None (endpoints are live in AdminApiEndpoints.cs:543, 562)
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

SAI-001 persists explanations to questions and the Question Bank API supports updating explanations and adding language versions (Hebrew, Arabic). The question edit page shows the explanation field but has no version history, no language management, and no quality gate score display. This task enhances the question edit page with full explanation management.

## Subtasks

### ADM-022.1: Explanation Version History Panel

**Files to create:**

- `src/admin/full-version/src/views/apps/questions/edit/ExplanationVersionHistory.vue`

**Files to modify:**

- `src/admin/full-version/src/pages/apps/questions/edit/[id].vue`

**Data source:** `GET /api/admin/questions/{id}/history` already returns event stream with explanation changes.

**Acceptance:**

- [ ] Collapsible panel below explanation editor showing version timeline
- [ ] Each version shows: timestamp, author, change type (Created/Updated/LanguageAdded), explanation preview
- [ ] Click version to view full text in a read-only dialog
- [ ] "Restore" button per version — calls `PATCH /api/admin/questions/{id}/explanation` with that version's text
- [ ] Restore requires confirmation dialog: "Restore explanation from [date]?"

### ADM-022.2: Quality Gate Score Display

**Files to create:**

- `src/admin/full-version/src/views/apps/questions/edit/QualityGateScores.vue`

**Files to modify:**

- `src/admin/full-version/src/pages/apps/questions/edit/[id].vue`

**Acceptance:**

- [ ] Card showing quality gate scores next to explanation editor
- [ ] Three gauge/progress indicators: Factual Accuracy, Linguistic Quality, Pedagogical Value
- [ ] Composite score with color: green (>0.8), yellow (0.6-0.8), red (<0.6)
- [ ] Scores fetched from question detail endpoint (already returned in explanation metadata)
- [ ] If no scores yet: "Quality gate not yet evaluated" with "Run Quality Check" button
- [ ] "Run Quality Check" button calls quality gate service and refreshes scores

### ADM-022.3: Language Version Management

**Files to create:**

- `src/admin/full-version/src/views/apps/questions/edit/LanguageVersionPanel.vue`

**Files to modify:**

- `src/admin/full-version/src/pages/apps/questions/edit/[id].vue`

**Acceptance:**

- [ ] Tab panel below question content: "English" | "Hebrew" | "Arabic"
- [ ] Each tab shows: question stem, options, explanation in that language
- [ ] If language version doesn't exist: "Add [Language] Version" button
- [ ] Add version dialog: fields for stem, options (A-D), explanation — all RTL-aware for Hebrew/Arabic
- [ ] RTL text direction set via `dir="rtl"` on Hebrew/Arabic text areas
- [ ] Submit calls `POST /api/admin/questions/{id}/language-versions` with `{ language, stem, options, explanation }`
- [ ] Edit existing version: inline edit with save button → calls same endpoint
- [ ] Side-by-side view option: English left, selected language right (for translation reference)
