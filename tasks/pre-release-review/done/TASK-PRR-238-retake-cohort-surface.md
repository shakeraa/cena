# TASK-PRR-238: Retake-cohort surface + retrieval-strength framing

**Priority**: P1 — promoted to Launch 2026-04-21 (was Post-Launch)
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-cogsci, persona-educator
**Source docs**: persona-cogsci findings (retake-cohort is distinct surface per Karpicke/Roediger retrieval-strength)
**Assignee hint**: kimi-coder + cogsci + educator review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, ui, pedagogy, retake
**Status**: Blocked on PRR-218 (aggregate with `ReasonTag=Retake`)
**Source**: User scope expansion 2026-04-21
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Students with `ReasonTag=Retake` on a target get a surface tuned for retrieval-strength work (different from first-attempt learners). Rapid low-stakes retrieval practice, not concept re-teaching. Persona-educator: Moed-Bet retake candidates are a major Bagrut population and need real support.

## Scope

- Retake-mode detection: any `ExamTarget` with `ReasonTag=Retake` triggers this.
- Session pattern: higher retrieval-practice density, lower worked-example density. Tunable via `ReasonTag`-scoped config.
- UX surface: dedicated "retake prep" landing card with minor visual differentiation (not cosmetic celebration — functional indicator).
- Syllabus coverage is same as first-time students, but weighting favors areas the student struggled on in prior attempt (if attempt data is available via Mashov integration or student self-assessment).
- No comparative language ("you failed last time"). Positive frame per ADR-0048.

## Files

- `src/actors/Cena.Actors/Mastery/RetakeScheduler.cs` (new — specialization of the base scheduler)
- `src/student/full-version/src/components/home/RetakeModeCard.vue` (new)
- Tests: ReasonTag=Retake triggers retrieval-dense schedule, first-time ReasonTag doesn't.

## Definition of Done

- Retake cohort distinguishable in scheduler.
- UX surface copy reviewed by persona-ethics + persona-educator.
- persona-cogsci sign-off on retrieval-strength weighting.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0048.
- Memory "Ship-gate banned terms" — no "last chance" copy.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch>"`

## Related

- PRR-218, PRR-226, PRR-237.
