# TASK-PRR-228: Per-target diagnostic blocks (replaces unified diagnostic)

**Priority**: P1
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-cogsci
**Source docs**: persona-cogsci findings (unified diagnostic cold-start bias; recommend per-target blocks)
**Assignee hint**: kimi-coder + cogsci review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, diagnostic, pedagogy
**Status**: Blocked on PRR-221 (onboarding wiring) + PRR-222 (skill-keyed mastery)
**Source**: persona-cogsci review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Replace the current single unified diagnostic quiz with per-target blocks: 6-8 items per target + shared 3-item warmup. Eliminates order-effect / cold-start allocation bias. Diagnostic priors are recorded per-target → collapsed into skill-keyed mastery posteriors (PRR-222).

## Scope

- Shared warmup: 3 items, adaptive difficulty, across all student-target syllabi union.
- Per-target blocks: 6-8 items each, adaptive within block, mapped to skills in that target's syllabus.
- Skip affordance: "Skip [target] diagnostic, complete later" — marks target as cold-start but allows onboarding to finish.
- Priors emitted into mastery projection (skill-keyed per PRR-222; target-scope carried as metadata on the diagnostic event for analytics).
- Completion time budget: total ≤ 12 min across warmup + all blocks for typical 2-target student.

## Files

- `src/student/full-version/src/components/onboarding/DiagnosticQuiz.vue` — refactor to block-structured.
- `src/student/full-version/src/stores/onboardingStore.ts` — per-target diagnostic state.
- `src/api/Cena.Student.Api.Host/Endpoints/DiagnosticEndpoints.cs` — accept per-target responses.
- Backend diagnostic prior computation updated.
- Tests: two-target student produces two separate priors; skipped target stays cold-start flagged.

## Definition of Done

- Diagnostic takes ≤ 12 min for 2-target student (measured).
- Priors land in skill-keyed projection correctly.
- Cold-start flag visible to scheduler for skipped targets.
- persona-cogsci sign-off on no-order-bias claim (property test across target order permutations).
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0002 (CAS oracle).
- Memory "Honest not complimentary" (cold-start flagged honestly).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + cogsci review note>"`

## Related

- PRR-221, PRR-222.
