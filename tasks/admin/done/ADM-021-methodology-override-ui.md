# ADM-021: Methodology Override UI

**Priority:** P1 — API exists (`POST /api/admin/mastery/students/{id}/methodology-override`), no UI
**Blocked by:** None (endpoint is live in AdminApiEndpoints.cs:229)
**Estimated effort:** 1 day
**Stack:** Vue 3 + Vuetify 3 + TypeScript

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The mastery API has a `methodology-override` endpoint that lets teachers force a specific learning methodology (Socratic, Direct Instruction, Feynman, Worked Example) for a student. The existing mastery student detail page (`/apps/mastery/student/[id]`) shows methodology profile but has no UI to trigger an override. This is a frontend-only task — the API is complete.

## Subtasks

### ADM-021.1: Override Dialog Component

**Files to create:**

- `src/admin/full-version/src/views/apps/mastery/student/MethodologyOverrideDialog.vue`

**Acceptance:**

- [ ] Dialog triggered by "Override Methodology" button on student detail page
- [ ] Methodology selector: dropdown with Socratic, DirectInstruction, Feynman, WorkedExample
- [ ] Level selector: dropdown with current concept levels from student's knowledge map
- [ ] Reason field: required text area (min 20 chars) — logged in audit trail
- [ ] Duration selector: "Until mastery", "7 days", "14 days", "30 days", "Permanent"
- [ ] Preview: "Student X will use [Methodology] for [Level/Concept] for [Duration]"
- [ ] Submit calls `POST /api/admin/mastery/students/{id}/methodology-override` with body `{ methodology, level?, reason, durationDays? }`
- [ ] Success: snackbar confirmation + refresh methodology profile section
- [ ] Error: display API error message in dialog

### ADM-021.2: Student Detail Page Enhancement

**Files to modify:**

- `src/admin/full-version/src/pages/apps/mastery/student/[id].vue`

**Acceptance:**

- [ ] "Override Methodology" button added to methodology profile card header
- [ ] Button only visible if user has `manage` permission on `Pedagogy` subject (CASL)
- [ ] If an active override exists, show alert banner: "Active override: [Methodology] until [date] — set by [admin]"
- [ ] "Remove Override" button on the alert banner (calls override endpoint with `methodology: null`)
- [ ] Override history section: table of past overrides with date, admin, methodology, reason
