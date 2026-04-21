# TASK-PRR-205: Student — wire `HintLadder.vue` into session runner; deprecate inline single-hint

**Priority**: P0 — ship-blocker
**Effort**: M — 3-4 days
**Lens consensus**: persona-cogsci, persona-a11y, persona-educator, persona-ethics
**Source docs**: `src/student/full-version/src/components/session/HintLadder.vue`, `src/student/full-version/src/pages/session/[sessionId]/index.vue`, `docs/adr/0045-hint-and-llm-tier-selection.md`
**Assignee hint**: claude-subagent-runner-ui (student Vue)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+a11y+ethics
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Replace the single-hint button currently inside `QuestionCard.vue` with the standalone `HintLadder.vue` (already built, zero integration references today). Runner page drives rung advancement by calling the new prr-203 endpoint. Respects scaffolding levels from `ScaffoldingService`, BKT expertise-reversal gate, and the anxiety-safe copy rules baked into the component.

## Files

- `src/student/full-version/src/pages/session/[sessionId]/index.vue` — mount `<HintLadder>` with props bound to ladder state store
- `src/student/full-version/src/components/session/QuestionCard.vue` — remove inline hint button + related props; emit `request-hint-rung` upward
- `src/student/full-version/src/composables/useHintLadder.ts` (new) — state machine for rungs consumed + remaining + loading
- `src/student/full-version/src/api/sessions.ts` — add `postHintNext(sessionId, questionId, rung)`
- `src/student/full-version/tests/unit/HintLadder.integration.spec.ts` (new) — integration across page + component + composable
- `src/student/full-version/src/plugins/i18n/locales/{en,he,ar}.json` — confirm rung labels present, qualitative only

## Non-negotiable references

- ADR-0045 — rung policy enforced by backend (prr-203); UI displays what the server returned.
- Anxiety-safe invariants (see `HintLadder.vue` header comment lines 9-26) — no ordinal numerics like "Hint 1 of 5", no visible BKT deductions.
- Math-always-LTR (user memory `feedback_math_always_ltr`) — rung text containing math wrapped in `<bdi dir="ltr">`.

## Definition of Done

- Runner page renders `<HintLadder>` when the current question type is MCQ or step-solver AND scaffolding level is not `None`.
- Ladder is hidden by default when BKT mastery > 0.60 (expertise-reversal); surfaces via explicit "I'm stuck" affordance that calls the same endpoint.
- Pressing "request next rung" calls `postHintNext`; response appends to the ladder; loading state on the button during flight; error state on 4xx/5xx with a retry action.
- `show_solution_always_available` flag from the response drives a visible "show me the solution" CTA that always renders when truthy (consistency with prr-029).
- Inline hint button removed from `QuestionCard.vue`; no dead code, no feature flag for the old path (per no-stubs rule).
- Axe CI passes for the runner page with the ladder open.
- Keyboard traversal: Tab enters ladder, Enter advances rung, Esc collapses; visible focus ring.
- aria-live region announces the rung level and source ("template", "method suggestion", "worked example") in the user's locale.
- Vitest integration test exercises: L1 request → L2 request → L3 request → "show solution" CTA → a11y smoke assertions.
- `tests/unit/HintLadder.anxietySafe.spec.ts` continues to pass against the new runner integration.
- Student-web build clean.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-runner-ui --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: expertise-reversal gate enforced client-side with server confirmation; productive-failure debounce on rung-advance during active typing. Owned here.
- **persona-a11y**: axe pass + keyboard traversal + aria-live + focus ring. Owned here.
- **persona-educator**: rung text in 3 locales reviewed by educator persona before ship (manual gate on the PR).
- **persona-ethics**: "show solution" CTA always visible when allowed; no engagement-coupled gating. Owned here.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-203 (endpoint)
- Adjacent: prr-206 (step-solver wiring), prr-207 (sidekick drawer), prr-211 (shipgate scanner)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
