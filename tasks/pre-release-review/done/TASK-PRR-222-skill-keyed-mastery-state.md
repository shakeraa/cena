# TASK-PRR-222: Skill-keyed mastery state + dedup invariant

**Priority**: P0 — persona-cogsci blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-cogsci
**Source docs**: persona-cogsci findings (Bagrut-math ↔ PET-quant double-counting)
**Assignee hint**: kimi-coder + cogsci review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, mastery
**Status**: Blocked on PRR-217
**Source**: persona-cogsci review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Lock mastery state (BKT+/IRT theta) to `skillId` as the primary key, NOT `(targetId, skillId)`. Bagrut-Math and PET-Quant share topic skills (Pythagoras, combinatorics, functions); keying per target creates phantom weakness and doubles the cold-start cost.

## Why this is a blocker per persona-cogsci

Current mastery model: if we key on `(target, skill)`, a student's attempts on "quadratic equations" in their Bagrut Math session don't inform the scheduler about their quadratic-equations mastery when they start a PET Quant session. Same skill, same student, two separate posteriors — garbage.

## Scope

1. **Skill is catalog-global**: `SkillId` identifies a concept (e.g. `math.algebra.quadratic-equations`). Item bank entries carry `skillIds[]`. Exam targets reference skills transitively via syllabus coverage, not via ownership.
2. **Mastery projection keyed on `(studentId, skillId)`**, no target dimension.
3. **Scheduler dispatch picks target → subset of skills that target's syllabus covers → BKT+ over those skills using the global per-student posterior.**
4. **Dedup invariant**: rewrite any current projection that keys on `(target, skill)` into `(student, skill)` with a migration.
5. **Audit trail**: per-attempt event still carries `targetId` (for analytics on which session it happened in), but the projection aggregates across targets.

## Files

- `src/actors/Cena.Actors/Mastery/MasteryProjection.cs` (refactor to skill-keyed)
- `src/actors/Cena.Actors/Mastery/BktPlusCalculator.cs` (verify skill-only inputs)
- Migration script to collapse existing `(target, skill)` projections into `(student, skill)` with event re-application.
- Tests: student attempts on shared skill across 2 targets converge to single posterior, not two.

## Definition of Done

- Projection re-keyed; all tests pass.
- Scheduler dispatch reads skill posteriors directly.
- Migration verified in staging against real data.
- persona-cogsci sign-off that overlap cases (Bagrut-Math ↔ PET-Quant) produce single posterior.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0002 (CAS oracle) — verification unaffected.
- ADR-0003 (misconception session-scope) — misconception projections separate; this is mastery only.
- Memory "Verify data E2E".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sha>"`

## Related

- PRR-217 (ADR), PRR-218 (aggregate), PRR-226 (scheduler uses the skill-keyed posterior).
