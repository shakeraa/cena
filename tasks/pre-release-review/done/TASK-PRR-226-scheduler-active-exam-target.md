# TASK-PRR-226: Scheduler ActiveExamTargetId + silent exam-week lock + TZ-safe determinism

**Priority**: P1
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-cogsci, persona-ethics, persona-sre
**Source docs**: brief §6, persona-cogsci (exam-week lock + renaming), persona-ethics (silent lock), persona-sre (TZ-safe seed)
**Assignee hint**: kimi-coder + cogsci review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, scheduler
**Status**: Blocked on PRR-218 (aggregate), PRR-222 (skill-keyed mastery)
**Source**: 10-persona review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Extend `SchedulerInputs` with `ActiveExamTargetId` and implement the session-start target selection policy: deadline-proximity rule (silent, no UX label) + weighted round-robin with TZ-safe deterministic seed. Consumes the skill-keyed mastery from PRR-222.

## Selection policy

1. **Deadline-proximity lock**: if any active target has `canonical_date - today ≤ 14 days`, lock session to that target. Scheduler-internal state — NO UX copy indicating "exam week". Behavioral cadence only.
2. **Weighted round-robin**: otherwise, weight by `target.WeeklyHours / sum(WeeklyHours)`. Deterministic seed per student per day.
3. **Student override**: "Study [different target] instead" button logs `ExamTargetOverrideApplied`. No penalty copy.

## Deterministic seed (persona-sre)

- Current brief proposal: `hash(userId, dayOfYear)`.
- Problem: day-boundary + timezone dead zone in 02:00-03:00 IST.
- Revised: `hash(userId, localDateISO, activeTargetSet)` where `localDateISO` is the student's local (IST) calendar date, `activeTargetSet` is the sorted ordered tuple of non-archived target IDs.
- Test suite: property tests across `{IST, UTC, UTC-8, UTC-5, UTC+10}` × midnight boundaries prove determinism + correct day selection.

## No LLM in picker (persona-finops)

- Selection is pure-deterministic. No LLM call. Protects per-session cost ceiling.

## Files

- `src/actors/Cena.Actors/Mastery/AdaptiveScheduler.cs` — add `ActiveExamTargetId` to `SchedulerInputs`.
- `src/actors/Cena.Actors/Mastery/TargetSelectionPolicy.cs` (new) — pure function, seeded RNG.
- `src/actors/Cena.Actors/Mastery/SessionStartHandler.cs` — invokes policy.
- Tests: proximity lock, round-robin distribution, TZ property tests, override event emission.

## Definition of Done

- `ActiveExamTargetId` flows through session-start.
- Proximity lock silent — shipgate scanner (PRR-224) passes on session-start code path.
- TZ property tests green across 5 time zones + DST boundaries.
- No LLM call in picker (CI-enforced; PRR-233 observability).
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0048 (positive framing — silent lock).
- Memory "Math always LTR" (not applicable here, but TZ correctness is the analog).
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + TZ test output>"`

## Related

- PRR-218, PRR-222, PRR-224, PRR-233.
