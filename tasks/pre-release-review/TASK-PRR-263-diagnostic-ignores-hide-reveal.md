# TASK-PRR-263: PRR-228 diagnostic ignores hide-reveal toggle (carve-out)

**Priority**: P2 — spec + test
**Effort**: S (1-2 days)
**Lens consensus**: persona-cogsci (blocker: retrieval-strength assessment ≠ retrieval-practice intervention)
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3.5](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md)
**Assignee hint**: kimi-coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p2, diagnostic, test, carve-out, q2
**Status**: Blocked on [PRR-260](TASK-PRR-260-hide-reveal-session-toggle.md) + [PRR-228](TASK-PRR-228-per-target-diagnostic-blocks.md)
**Source**: 10-persona 002-brief review 2026-04-22 (persona-cogsci)
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Per persona-cogsci: PRR-228 onboarding diagnostic is a **retrieval-strength assessment** (Roediger & Karpicke 2006). Hide-reveal is a **retrieval-practice intervention**. Mixing them contaminates the posterior the scheduler builds from the diagnostic. PRR-228 questions MUST always render with options visible, regardless of the student's session-level `attemptMode` toggle.

Also threatens PRR-228's 85% completion SLO — hidden options + diagnostic pressure is a double-cost.

## Scope

- Session engine detects `isDiagnostic === true` (PRR-228) and force-overrides `attemptMode → 'visible'` for that session's items.
- No UI indication of the override (persona-ethics: not a student-facing concept).
- Property test: any diagnostic session with `attemptMode=hidden_reveal` on the student profile still returns visible options.
- Regression test covers cross-session toggle persistence not leaking into diagnostic.

## Files

- `src/actors/Cena.Actors/Sessions/SessionRenderContext.cs` (or equivalent) — force-visible in diagnostic sessions.
- `src/student/full-version/src/components/session/McOptionsGate.vue` — skip hidden-render logic for diagnostic.
- Tests: two new cases in `DiagnosticEndpoints.Tests` + frontend spec.

## Definition of Done

- Property test passes: diagnostic session × any `attemptMode` = visible.
- Completion SLO measurement (PRR-228) unaffected.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [PRR-228](TASK-PRR-228-per-target-diagnostic-blocks.md).
- Memory "Honest not complimentary" — diagnostic accuracy load-bearing.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + property-test output>"`

## Related

- PRR-228, PRR-260.
- Persona-cogsci 002 findings (§3.5 explicit blocker).
