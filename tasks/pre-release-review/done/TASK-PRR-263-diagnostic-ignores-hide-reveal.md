# TASK-PRR-263: PRR-228 diagnostic ignores hide-reveal toggle (carve-out)

**Priority**: P2 — spec + test
**Effort**: S (1-2 days)
**Lens consensus**: persona-cogsci (blocker: retrieval-strength assessment ≠ retrieval-practice intervention)
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3.5](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md)
**Assignee hint**: kimi-coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p2, diagnostic, test, carve-out, q2
**Status**: Partial — diagnostic-mode carve-out policy + 17 property tests shipped 2026-04-23 (`SessionAttemptModeContextBuilder.Build` emits `IsDiagnosticBlock: true` when `queue.Mode == "diagnostic"`; existing `SessionAttemptModePolicy.ResolveEffective` then forces Visible). Tenant call-site integration (session-render / question-fetch paths) lands when PRR-228 diagnostic blocks wire into the session engine; frontend `McOptionsGate.vue` skip-hidden-render is the Vue pass.
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

## What shipped (2026-04-23)

Backend carve-out — DoD item #1 ("Property test passes: diagnostic
session × any attemptMode = visible") is green.

### Context builder

[`SessionAttemptModeContextBuilder`](../../src/actors/Cena.Actors/Sessions/SessionAttemptMode.cs) in the same namespace as the policy:

- `Build(sessionMode, storedAttemptModeWire, isMultipleChoice, authorForceVisible)` — pure, defensive on nulls, case-insensitive on session-mode matching.
- `ResolveFrom(...)` convenience that composes Build + policy so the common call-site only needs the effective mode.
- `DiagnosticModeString = "diagnostic"` — hoisted const matching the value `AdaptiveQuestionPool.MapModeToGoal` already consumes on `queue.Mode`. The `Builder_DiagnosticModeString_constant_matches_canonical_value` test fails loudly if either side drifts.

### Property tests (17 new, 39 total in the file)

[`SessionAttemptModeTests.cs`](../../src/actors/Cena.Actors.Tests/Sessions/SessionAttemptModeTests.cs):

- **Core carve-out property** (5 theory rows): diagnostic session × `{visible, hidden_reveal, null, "", unknown_mode_from_future_client}` → always `Visible`. Covers the exact persona-cogsci contamination risk (retrieval-practice intervention vs. retrieval-strength assessment).
- **Case + whitespace tolerance** (3 theory rows): `"Diagnostic"`, `"DIAGNOSTIC"`, `" diagnostic "` all trigger the carve-out, so a future producer with casing drift doesn't silently disable it.
- **Inverse guard** (6 theory rows): non-diagnostic modes (`practice` / `review` / `challenge` / `exam` / empty / null) with stored `hidden_reveal` correctly honour the stored mode. The carve-out is scoped to `"diagnostic"` specifically, not any "non-practice" session.
- **Cross-session-leak regression**: if a stored hidden-reveal projection value somehow reached a diagnostic render (bug / replay), the carve-out still fires. Locks the "storage bug can't un-do the carve-out" guarantee.
- **Garbage-value safety**: unknown stored mode (e.g. mid-deployment schema mismatch) parses as Visible rather than crashing — traditional render is always the safe fallback.

Full `Cena.Actors.sln` build green; 39/39 SessionAttemptMode tests pass.

## What is deferred

- **Call-site integration**. The session-render path that assembles `SessionQuestionDto` (or its equivalent) needs to call `SessionAttemptModeContextBuilder.ResolveFrom(queue.Mode, queue.AttemptMode, questionDoc.QuestionType == "multiple-choice" && (questionDoc.Choices?.Length ?? 0) >= 1, questionDoc.ForceOptionsVisible)` and surface the effective mode on the wire. This lands naturally when PRR-228 per-target diagnostic blocks wire into the session engine — today PRR-228 is not yet shipped so there's no diagnostic-block session to integrate with. When PRR-228 lands, the builder is already tested against the `"diagnostic"` string it will write.
- **Vue `McOptionsGate.vue` skip-hidden-render**. Frontend consumer; reads the effective mode off the wire and renders traditional options when it says `visible`.
- **Cross-session regression frontend test** (Vue spec). Frontend-side; the backend property tests already lock the server-side invariant.

Closing as **Partial** per memory "Honest not complimentary": the carve-out **policy** is real and property-tested with all five inputs; the call-site that feeds the policy is a natural integration point for PRR-228 when it lands. No code needs to change in this file for PRR-228 to pick up the carve-out — the builder is the stable seam.
