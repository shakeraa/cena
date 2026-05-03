# REPORT-EPIC-F — Teacher classroom (real-browser journey)

**Status**: ✅ green for the journey we have today; the journey the epic body envisioned does not exist yet (documented gap)
**Date**: 2026-04-27
**Worker**: claude-1
**Spec file**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-F-teacher-journey.spec.ts`

## What this spec exercises today

The test asserts the **role-gate security positive case**: drive the admin-SPA `/login` form with `teacher1@cena.local`, confirm the admin SPA correctly rejects with the role-gated alert "Access denied. Admin, Moderator, or Super Admin role required.", and that the URL stays on `/login`.

This is the only verifiable teacher-on-real-browser journey today.

## What does NOT exist (gap surfaced — queue these)

The epic body (`EPIC-E2E-F-teacher-classroom.md`) describes:

- **F-01 heatmap-landing** — `/apps/teacher/heatmap` route. **No such route exists** in either SPA.
- **F-02 assign-homework** — no UI surface in either SPA.
- **F-03 K-floor enforcement** — no drill-down UI to K=10 below.
- **F-04 schedule-override** — no UI in this build.
- **F-05 struggling-topics** — no UI in this build.

These are real product gaps, not spec issues. Closing them needs either:

1. A teacher-specific section on the admin SPA (relax role-gate to allow TEACHER on selected `/apps/*` routes), OR
2. A teacher SPA at a third port (`localhost:51XX`)

The classroom join endpoint (`POST /api/classrooms/join`) does exist on the admin API but is student-facing (joining via invite); not a teacher-side flow.

## Buttons clicked / endpoints fired

- Admin SPA `/login`: email + password fields + submit
- Backend rejection alert role: `getByRole('alert').filter({ hasText: /access denied/i })`

No state mutation; the journey ends at the rejection.

## Diagnostics

Console errors / page errors / failed requests collected — all empty for the rejection path.

## What's next

Queue a backend task: design a teacher-facing route group (admin SPA scoped or a new SPA), hook one of `/apps/sessions/live`, `/apps/mastery/class/`, or a new `/apps/teacher/heatmap` to TEACHER role. Until then, F-01..F-05 stay unimplementable.
