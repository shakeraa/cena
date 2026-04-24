# TASK-PRR-234: Close out PRR-148 (superseded + legacy `StudentPlanConfig` removal)

**Priority**: P2 — housekeeping
**Effort**: S (1-2 days)
**Lens consensus**: —
**Source docs**: [PRR-148 (Done, DoD-incomplete)](done/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md), EPIC-PRR-F
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p2, housekeeping
**Status**: Blocked on PRR-218, PRR-219 (migration complete in all tenants)
**Source**: meta
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Honestly close out PRR-148. It was marked Done 2026-04-20 but its DoD #1 ("Student can set exam date + weekly time budget during onboarding") was never met (the component was not wired). Plus the single-target `StudentPlanConfig` VO is now superseded by the multi-target aggregate.

## Scope

1. Move PRR-148 task file to `tasks/pre-release-review/superseded/` (create folder if needed) with a "Superseded-By: PRR-217..PRR-234 (EPIC-PRR-F)" note.
2. Remove the legacy `StudentPlanConfig` VO from the codebase after all tenants are on the multi-target aggregate (post-migration; coordinate with PRR-219).
3. Remove any `/api/me/study-plan` endpoints that accept single-target payloads after PRR-218 endpoints are live.
4. Annotate README in `tasks/pre-release-review/` to document the supersedes relationship.

## Files

- Move: `tasks/pre-release-review/done/TASK-PRR-148-*.md` → `tasks/pre-release-review/superseded/TASK-PRR-148-*.md` (or annotate in place + link from EPIC-PRR-F).
- Remove: legacy `StudentPlanConfig.cs`, `StudyPlanSettings.vue` (orphan), `StudyPlanSettingsEndpoints.cs` single-target variant.
- Update: `tasks/pre-release-review/README.md` with close-out note.

## Definition of Done

- PRR-148 annotated as superseded.
- All legacy single-target code removed.
- Full `Cena.Actors.sln` builds cleanly post-removal.
- No dangling imports.

## Non-negotiable references

- Memory "No stubs — production grade" (removing superseded stubs).
- Memory "Always merge to main".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + removed file list>"`

## Related

- PRR-148, PRR-218, PRR-219.
