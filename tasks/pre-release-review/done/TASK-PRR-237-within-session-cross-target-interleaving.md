# TASK-PRR-237: Within-session cross-target interleaving

**Priority**: P1 — promoted to Launch 2026-04-21 (was Post-Launch)
**Effort**: L (3-4 weeks + cogsci validation)
**Lens consensus**: persona-cogsci
**Source docs**: persona-cogsci findings (weighted round-robin is spacing, not interleaving — actual Rohrer effect requires within-session mixing)
**Assignee hint**: kimi-coder + cogsci sign-off
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, scheduler, pedagogy
**Status**: Blocked on PRR-222 (skill-keyed mastery), PRR-226 (base scheduler with ActiveExamTargetId)
**Source**: User scope expansion 2026-04-21
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Extend the scheduler to produce within-session interleaved items across the student's selected targets when the skills overlap (e.g. a Bagrut-Math + PET-Quant student gets quadratic-equations items drawn from both syllabi in the same session). Per persona-cogsci, this is what unlocks the Rohrer discrimination-practice effect that cross-session alternation alone cannot.

## Scope

- Item-selection policy: within a session, draw items from the union of active targets' skill-subsets, interleaving by skill-family not by target. Topic labels hidden from student during practice (Rohrer: topic-hidden mixing).
- Target attribution: each item retains `providence.targetIds[]` for mastery-update routing (mastery projection stays skill-keyed per PRR-222 — attribution is for analytics, not mastery state).
- Per-session target mix: policy tunable via config; defaults based on active targets' `WeeklyHours` weights.
- Deadline proximity still locks session to single target (no multi-target interleaving during 14-day exam-week lock).
- Effect-size claim honesty: no UI copy overstating expected gains (persona-cogsci: Brunmair 2019 meta d=0.34 not Rohrer cherry-pick d=1.05).

## Files

- `src/actors/Cena.Actors/Mastery/InterleavingPolicy.cs` (new)
- `src/actors/Cena.Actors/Mastery/AdaptiveScheduler.cs` — extend ItemSelection to accept multi-target inputs.
- Mastery update router that attributes attempts correctly.
- Tests: property tests on mixing distribution, deadline-lock override, skill-overlap coverage.
- persona-cogsci review artifact: effect-size claims documented per ADR copy.

## Definition of Done

- Interleaving produces verified mixed-topic sessions across overlapping targets.
- Deadline-proximity lock preempts interleaving correctly.
- persona-cogsci sign-off on effect-size claims + no-dark-pattern copy.
- Mastery projection correctly updated (no double-counting per PRR-222).
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0002 (CAS oracle) — per-item verification unaffected.
- ADR-0048 — no pressure copy.
- Memory "Honest not complimentary" — no overstated effect sizes.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + cogsci review note>"`

## Related

- PRR-222, PRR-226.
