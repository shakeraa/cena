# TASK-PRR-236: Classroom-assigned target teacher UI

**Priority**: P1 — promoted to Launch 2026-04-21 (was Post-Launch)
**Effort**: M (2-3 weeks)
**Lens consensus**: persona-educator, persona-enterprise
**Source docs**: persona-educator findings (classroom roster flow), persona-enterprise findings (Source=Classroom path)
**Assignee hint**: kimi-coder + teacher-workflow review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, ui, teacher
**Status**: Blocked on PRR-218 (aggregate with `Source` discriminator), PRR-220 (catalog), PRR-021 (roster import)
**Source**: User scope expansion 2026-04-21 — "all options on release day"
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Teacher UI that assigns an exam target to a whole class or individual students. Creates `ExamTargetAdded` events with `Source=Classroom, AssignedById=teacherId, EnrollmentId=classroomEnrollment`. Respects ADR-0001 tenancy.

## Scope

- Classroom roster view (reuses Mashov sync + PRR-021 imported rosters).
- "Assign target to class" bulk action: pick `examCode + track + sittingCode`. Previews which students already have that target (skip) vs. additions.
- Per-student override: remove individual students from the bulk assignment before commit.
- Teacher-only route; role enforcement via PRR-009 helper.
- Un-assign flow: soft-archive of classroom-sourced target; student retains `Source=Student`-sourced targets.
- Audit log via PRR-062 (all assigns/unassigns logged).
- Interaction with student-sourced plan: classroom-assigned target coexists with student picks; distinction visible via `Source` badge in student's settings.

## Files

- `src/admin/full-version/src/pages/teacher/class-targets.vue` (new)
- `src/admin/full-version/src/components/teacher/ClassTargetAssign.vue` (new)
- `src/api/Cena.Admin.Api.Host/Endpoints/ClassroomTargetEndpoints.cs` (new)
- Tests: bulk assign, single remove, role-forbidden non-teacher, un-assign archival, audit entries.

## Definition of Done

- Teachers can bulk-assign a target to a class.
- Students see the assignment appear on their settings page with `Source=Classroom` badge.
- Un-assignment archives (does not delete) the target.
- Audit log complete.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0001 (tenancy).
- PRR-009 (IDOR / authz helper).
- PRR-062 (audit log).
- PRR-037 (grade passback — classroom-sourced targets carry `passback_eligible=true` from catalog).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch>"`

## Related

- PRR-218, PRR-220, PRR-021, PRR-062, PRR-037.
