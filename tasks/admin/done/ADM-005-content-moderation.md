# ADM-005: Content Moderation Queue

**Priority:** P1 — core moderator workflow
**Blocked by:** ADM-001 (auth), ADM-003 (moderator role)
**Estimated effort:** 4 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Moderators need to review, approve, or reject questions and content before they go live. This is the primary workflow for content quality control. Integrates with the CNT (content) pipeline tasks.

## Subtasks

### ADM-005.1: Moderation Queue List

**Files to create:**
- `src/admin/full-version/src/pages/apps/moderation/queue/index.vue`
- `src/admin/full-version/src/views/apps/moderation/` — moderation components

**Acceptance:**
- [ ] Data table: question text preview, subject, grade, author, submitted date, status
- [ ] Status filters: pending, in-review, approved, rejected, flagged
- [ ] Priority sorting: flagged items first, then oldest first
- [ ] Claim system: moderator claims item (locks for others)
- [ ] Bulk actions: approve selected, reject selected, assign to moderator

### ADM-005.2: Question Review Detail

**Files to create:**
- `src/admin/full-version/src/pages/apps/moderation/review/[id].vue`

**Acceptance:**
- [ ] Full question preview with math rendering (KaTeX/MathJax)
- [ ] Answer options displayed with correct answer highlighted
- [ ] Metadata: subject, topic, difficulty, grade level, concept tags
- [ ] AI quality assessment score (from ingestion pipeline)
- [ ] Side-by-side: original source vs. normalized version (if ingested)
- [ ] Action buttons: Approve, Reject (with reason), Request Changes, Flag
- [ ] Comment thread: moderator notes visible to other moderators
- [ ] History: who reviewed, when, what action taken

### ADM-005.3: Moderation Analytics

**Files to create:**
- `src/admin/full-version/src/views/apps/moderation/ModerationStats.vue`

**Acceptance:**
- [ ] Metrics: items reviewed today/week, approval rate, avg review time
- [ ] Per-moderator stats: items reviewed, avg time, approval/rejection ratio
- [ ] Content quality trend: approval rate over time
- [ ] Queue depth chart: pending items over time (are we keeping up?)

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/moderation/queue` | Paginated queue with filters |
| GET | `/api/admin/moderation/items/{id}` | Full item detail |
| POST | `/api/admin/moderation/items/{id}/claim` | Claim for review |
| POST | `/api/admin/moderation/items/{id}/approve` | Approve item |
| POST | `/api/admin/moderation/items/{id}/reject` | Reject with reason |
| POST | `/api/admin/moderation/items/{id}/flag` | Flag for escalation |
| POST | `/api/admin/moderation/items/{id}/comments` | Add moderator comment |
| GET | `/api/admin/moderation/stats` | Moderation analytics |

## Test

- [ ] Queue shows real pending items from backend
- [ ] Claiming an item prevents other moderators from editing (SignalR lock)
- [ ] Math equations render correctly in review view
- [ ] Approve → item moves to published, disappears from queue
- [ ] Reject → item moves to rejected with reason, author notified
- [ ] Arabic content renders RTL correctly in review
- [ ] Moderator stats update in real-time as items are processed
