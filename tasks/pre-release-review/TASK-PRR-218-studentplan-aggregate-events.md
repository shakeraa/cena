# TASK-PRR-218: StudentPlan aggregate + ExamTarget events

**Priority**: P0
**Effort**: L (2-3 weeks)
**Lens consensus**: persona-enterprise, persona-ministry, persona-redteam, persona-privacy
**Source docs**: ADR-0049 (PRR-217), persona-enterprise findings (source discriminator + EnrollmentId), persona-redteam findings (server invariants)
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, aggregate
**Status**: Blocked on PRR-217 (ADR-0049 lock)
**Source**: 10-persona review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Implement the event-sourced `StudentPlan` aggregate and `ExamTarget*` events on the StudentActor successor aggregate (per ADR-0012). All fields and invariants from ADR-0049 must be wired and server-enforced from day one.

## Scope

### Aggregate

`StudentPlan : Entity` keyed by `StudentId`. Contains `IReadOnlyList<ExamTarget> Targets`.

### Events

- `ExamTargetAdded(targetId, source, assignedById, enrollmentId?, examCode, track?, sittingCode, weeklyHours, reasonTag?, createdAt)`
- `ExamTargetUpdated(targetId, track?, sittingCode, weeklyHours, reasonTag?)` — no source/examCode/assignedById change.
- `ExamTargetArchived(targetId, archivedAt, reason)` — archival is soft; event is terminal for the target.
- `ExamTargetOverrideApplied(targetId, sessionId, appliedAt)` — student picked a different target for this session (scheduler telemetry; no behavior change).

### Command handlers

- `AddExamTarget(...)` — enforces `count(Targets) ≤ 5`, `sum(WeeklyHours) + new.WeeklyHours ≤ 40`, catalog-code validity, sitting-code validity.
- `UpdateExamTarget(...)` — rejects if target archived.
- `ArchiveExamTarget(...)` — idempotent; rejects double-archive.
- `ApplyExamTargetOverride(...)` — no-op if target not in plan; records event.

### Server invariants (non-negotiable; redteam)

- `sum(Targets.WeeklyHours) ≤ 40` (compound across active targets).
- `count(active Targets) ≤ 5` (archived don't count).
- `target.ArchivedAt != null ⇒ no further Updated/Archived events`.
- `examCode + sittingCode + track` tuple unique across active targets per student (can't add 2 identical targets).
- All validation at the aggregate root, not in endpoints.

### Endpoints

- `POST /api/me/exam-targets` — add.
- `PUT /api/me/exam-targets/{id}` — update.
- `POST /api/me/exam-targets/{id}/archive` — archive.
- `GET /api/me/exam-targets` — list (with `?includeArchived=true`).

Auth: JWT with `student_id`; parent/teacher flows gated through EPIC-PRR-C (not here).

## Files

- `src/actors/Cena.Actors/Students/StudentPlan.cs` (new aggregate)
- `src/actors/Cena.Actors/Students/ExamTarget.cs` (new VO)
- `src/actors/Cena.Actors/Students/ExamTargetEvents.cs` (new events)
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` (command handlers)
- `src/api/Cena.Student.Api.Host/Endpoints/ExamTargetEndpoints.cs` (new)
- Fluent validators for each endpoint (mirror `StudyPlanSettingsEndpoints.cs:130-164` pattern).
- Tests: aggregate invariants, replay, overflow attempts (WeeklyHours=10000, count=10000), archive replay.

## Definition of Done

- Aggregate + events + command handlers implemented.
- Server invariants enforced at the aggregate level (not just endpoints).
- 100% test coverage on invariant-violating inputs (property-based where possible).
- Endpoints respect PRR-011 session JWT + PRR-009 IDOR helper.
- Full `Cena.Actors.sln` builds cleanly.
- No raw `DateTimeOffset` deadlines anywhere — sitting code is canonical.

## Non-negotiable references

- ADR-0001 (tenancy isolation, EnrollmentId).
- ADR-0012 (StudentActor split).
- ADR-0049 (this task implements it).
- Memory "No stubs — production grade".
- Memory "Full sln build gate".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sha>"`

## Related

- PRR-217 (ADR), PRR-219 (migration), PRR-220 (catalog for examCode + sittingCode validation), PRR-222 (skill-keyed mastery).
