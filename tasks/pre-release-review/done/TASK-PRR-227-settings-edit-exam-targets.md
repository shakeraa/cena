# TASK-PRR-227: `/settings/study-plan` edit UI (archive + add + edit)

**Priority**: P1
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-educator, persona-a11y, persona-ethics
**Source docs**: brief §5 edit-later section
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, ui, settings
**Status**: Blocked on PRR-221 (onboarding components reused)
**Source**: 10-persona review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Replace PRR-148's half-shipped `StudyPlanSettings.vue` with a proper multi-target edit UI at `/settings/study-plan` that reuses the `ExamTargetsStep` + `PerTargetPlanStep` components from PRR-221.

## Scope

- Table of active targets with inline edit: track, sitting, weekly hours, reason tag.
- "Add a target" button → opens onboarding-style flow for a single new target.
- "Archive target" → soft-archive; shows archived section separately with restore option during retention window (PRR-229).
- Total-hours counter and warning on edit.
- Server validates every mutation (reuses PRR-218 command handlers).
- Per-archive toast is neutral; no celebration, no badges, no counters (persona-ethics).
- Keyboard-only + SR scaffolding per persona-a11y non-negotiables.
- RTL mirror + numerals preference (PRR-232).

## Files

- `src/student/full-version/src/pages/settings/study-plan.vue` (new; replaces orphan `StudyPlanSettings.vue`)
- `src/student/full-version/src/components/settings/StudyPlanTable.vue` (new)
- `src/student/full-version/src/components/settings/ArchivedTargetsList.vue` (new)
- E2E tests: add, edit, archive, restore (within retention), validation errors.
- Retire: legacy `StudyPlanSettings.vue` if present as orphan.

## Definition of Done

- All CRUD operations work against PRR-218 endpoints.
- Archive → restore round-trip works inside retention window.
- Keyboard + SR tested en/he/ar.
- Shipgate scanner (PRR-224) passes on copy.

## Non-negotiable references

- ADR-0048.
- Memory "Math always LTR".
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch>"`

## Related

- PRR-218, PRR-221, PRR-229, PRR-232.
