# TASK-PRR-231: Amend PRR-053 capacity plan — SAT+PET 7-window compound calendar

**Priority**: P1 — persona-sre
**Effort**: S (3-5 days)
**Lens consensus**: persona-sre
**Source docs**: persona-sre findings (§14.2 #20), PRR-053 (existing Bagrut-only capacity plan)
**Assignee hint**: SRE lead
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, sre, capacity
**Status**: Ready
**Source**: persona-sre review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md), coordinates with [PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md)

---

## Goal

Amend the existing Bagrut-focused capacity model (PRR-053) to reflect that SAT + PET at Launch turn a 2-spike calendar (Jun / Aug) into a 7-window compound calendar. Recalculate peak sizing + scale-out triggers.

## Windows to model

| Window | Dates (approx) | Traffic source |
|---|---|---|
| Bagrut Math Moed A | Jun 15 | BAGRUT_MATH students |
| Bagrut Math Moed B | Jul 20 | retake cohort |
| Bagrut Phys/Chem/Bio | Jun 22 / Jun 29 / Jul 3 | BAGRUT_* students |
| Bagrut English Modules | Jan + Jun | BAGRUT_ENGLISH |
| PET Apr sitting | mid-Apr | PET students |
| PET Jul sitting | mid-Jul | PET students |
| PET Sep sitting | mid-Sep | PET students |
| PET Dec sitting | mid-Dec | PET students |
| SAT quarterly | Mar/May/Aug/Oct/Dec | SAT students |

## Scope

- Update PRR-053 traffic forecast with SAT + PET populations (estimate based on catalog selection distribution).
- Recalculate 95th-percentile concurrent-session load.
- Scale-out triggers per window.
- Break-glass procedure: tenant-admin forced catalog overlay (PRR-220) lets us temporarily disable SAT or PET in a given tenant if capacity buckles.
- Coordinate with PRR-016 exam-day freeze window: extend freeze windows to cover PET + SAT sittings.

## Files

- Update: `tasks/pre-release-review/TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md` with amendment note.
- New: `docs/runbooks/exam-day-capacity-multi-target.md` (Extension of existing runbook).
- Update: deploy-pipeline freeze-window config.

## Definition of Done

- Capacity plan amended + reviewed by SRE.
- PRR-053 annotated with "Superseded-in-scope-by PRR-231".
- Freeze-window config includes SAT + PET dates.
- Synthetic probe coverage extended (PRR-039 pattern) to SAT + PET endpoints.

## Non-negotiable references

- PRR-016, PRR-053, PRR-039.
- Memory "Honest not complimentary" (numbers > vibes on capacity).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<amended doc URL + SRE sign-off>"`

## Related

- PRR-053, PRR-016, PRR-220 (break-glass overlay).
