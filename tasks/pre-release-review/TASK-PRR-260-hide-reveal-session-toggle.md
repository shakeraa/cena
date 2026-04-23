# TASK-PRR-260: Student-controlled hide-then-reveal session toggle

**Priority**: P1
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-cogsci, persona-ethics, persona-a11y, persona-educator, persona-enterprise, persona-sre (8/10 converge on visible-first + opt-in hide)
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md) + 10-persona findings under [pre-release-review/reviews/persona-*/student-input-modalities-2-findings.md](../../pre-release-review/reviews/)
**Assignee hint**: kimi-coder (after research prompt lands) or front-end coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p1, ui, pedagogy, q2
**Status**: Ready (awaiting research-prompt output) — **decision-gate confirmed 2026-04-23: option A (run research, gate launch on it)**. No code work begins until research output lands. Downstream PRR-261 / PRR-262 / PRR-263 remain blocked on this task per their existing status lines.
**Source**: 10-persona 002-brief review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Add a session-level student-controlled toggle that hides MC answer options until the student clicks "Show options". **Default = visible** (traditional behavior). Realizes Bjork's generation effect for students who opt in; zero friction for students who don't.

## Scope

### Session state

- New field on `SessionState`: `attemptMode: 'visible' | 'hidden_reveal'`. Default `'visible'`.
- Toggle affordance at session start **and** mid-session (settings drawer). Persists for the session, NOT across sessions (student re-opts each session to preserve autonomy per persona-ethics).
- Toggle is **ignored during PRR-228 diagnostic** (see [PRR-263](TASK-PRR-263-diagnostic-ignores-hide-reveal.md)).

### UI pattern (persona-a11y ranked)

Use **placeholder + `aria-live`** (preferred):
- When `hidden_reveal` and student hasn't revealed yet: render a placeholder "Click to reveal options" with a prominent button.
- Announce the reveal via `aria-live="polite"` on the options container.
- Preserve layout to avoid reflow jump (persona-a11y WCAG 4.1.3).

Reject DOM-absent pattern (breaks SR virtual cursor) and plain `<details>` (weaker affordance).

### Per-question applicability

- Applies only to questions with MC options. Step-solver math, chem reactions, essay items skip the flow entirely (`attemptMode` is a no-op for them).
- Author-level `forceOptionsVisible: true` flag respected — some items (e.g. "choose which graph is correct") ARE the options.

### Commit-and-compare (OPEN — not in this task)

Cogsci wants the student to type a guess before revealing; a11y + sre want to defer. **Not in PRR-260 scope.** If the decision-holder greenlights v1, create PRR-265 as a follow-up.

### Server-side enforcement

- Student-mode uses client-side hiding only. Redteam flagged bypass via DevTools — acceptable for self-discipline mode.
- Classroom-enforced server-side redaction is [PRR-261](TASK-PRR-261-classroom-redacted-projection.md), separate task.

### Excluded from scope

- Hidden-first default (cogsci: would misrepresent effect size — reject).
- Timer-based auto-hide (ADR-0048 countdown ban).
- Option C pedagogy-driven hide (dark pattern).

## Files

- `src/student/full-version/src/stores/sessionStore.ts` — add `attemptMode` state + mutations.
- `src/student/full-version/src/components/session/McOptionsGate.vue` (new) — placeholder + reveal-button + aria-live.
- `src/student/full-version/src/pages/session/[sessionId]/index.vue` — integrate gate in render.
- `src/student/full-version/src/components/session/SessionSettingsDrawer.vue` (new or extend) — toggle control.
- Tests: toggle persistence within session, toggle reset across sessions, PRR-228 diagnostic unaffected, author `forceOptionsVisible` respected, SR-announcement on reveal.

## Definition of Done

- Default state = visible across all session types.
- Toggle-on → subsequent MC items render placeholder until reveal clicked.
- Toggle-off mid-session → remaining items show options immediately.
- Author `forceOptionsVisible` flag honored.
- Non-MC items unaffected.
- PRR-228 diagnostic unaffected (cross-ref test).
- WCAG 4.1.3 passes (SR test with NVDA + VoiceOver).
- Shipgate scanner (PRR-224 + PRR-264) passes on copy (no timer / countdown language).
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no time-pressure mechanics.
- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md).
- Memory "No stubs — production grade".
- Memory "Ship-gate banned terms".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + SR test recording URL>"`

## Related

- [PRR-261](TASK-PRR-261-classroom-redacted-projection.md) — server-side classroom enforcement (separate API shape).
- [PRR-262](TASK-PRR-262-scaffolding-stem-grounded-hints.md) — hint routing under hidden mode.
- [PRR-263](TASK-PRR-263-diagnostic-ignores-hide-reveal.md) — PRR-228 diagnostic carve-out.
- [PRR-264](TASK-PRR-264-hide-reveal-shipgate-audit.md) — shipgate audit for countdown/timer language.
- Persona findings: cogsci, ethics, a11y, educator, enterprise, sre 002 files.
