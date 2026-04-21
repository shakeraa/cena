# TASK-PRR-235: Ministry reporting export endpoint shape (spec-only v1)

**Priority**: P2 — persona-ministry + persona-enterprise
**Effort**: S (2-3 days — spec only)
**Lens consensus**: persona-ministry, persona-enterprise
**Source docs**: persona-ministry findings (reporting flows), persona-enterprise findings (school export shape)
**Assignee hint**: human-architect
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p2, spec, ministry
**Status**: Ready
**Source**: persona-ministry + persona-enterprise review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Spec the shape of a future school / Ministry reporting export so that the multi-target data model doesn't paint us into a corner. Implementation is out-of-scope for v1; spec-only landing allows tenants that need to export to Ministry/school systems to plan.

## Scope

- Define endpoint shape `GET /api/export/exam-targets?enrollmentId=&format=csv|json|ministry-xml`.
- Export fields: `student_id`, `enrollment_id`, `exam_code`, `ministry_subject_code`, `ministry_question_paper_codes[]`, `track`, `sitting_code`, `academic_year`, `season`, `moed`, `source`, `assigned_by_id`, `weekly_hours`, `reason_tag`, `created_at`, `archived_at?`.
- Distinction: `source ∈ {Classroom, Tenant}` → exportable in school reports; `source = Student` → excluded unless explicit consent.
- No magen (מגן) or predicted-score fields (persona-ministry non-goal).
- Tenant-scoped, JWT-auth, audit-logged.

## Files

- `docs/api/ministry-reporting-export-spec.md` (new spec doc)
- Annotate data contracts in `ExamTarget` VO with export-readiness flag.
- No endpoint implementation v1.

## Definition of Done

- Spec reviewed by persona-ministry + persona-enterprise.
- No implementation code — spec only.
- Referenced from EPIC-PRR-F and from Phase 2 tenancy rollout doc.

## Non-negotiable references

- ADR-0001 (tenancy).
- Memory "Bagrut reference-only".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<spec doc URL>"`

## Related

- PRR-037 (grade passback), PRR-072 (coverage matrix).
