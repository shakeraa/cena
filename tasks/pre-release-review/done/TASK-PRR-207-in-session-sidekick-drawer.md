# TASK-PRR-207: Student — in-session Sidekick drawer (tutor panel co-located with session)

**Priority**: P0 — ship-blocker
**Effort**: L — 5-7 days
**Lens consensus**: persona-cogsci, persona-a11y, persona-ethics, persona-privacy, persona-redteam, persona-sre, persona-finops
**Source docs**: `src/student/full-version/src/components/tutor/`, `src/student/full-version/src/pages/tutor/`, `docs/adr/0003-misconception-session-scope.md`
**Assignee hint**: claude-subagent-sidekick (student Vue)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+a11y+ethics+privacy+redteam
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Collapsible tutor drawer co-located with the session runner page. Opens via a persistent affordance. Pre-seeded with session context (current question, last attempt, rungs consumed, current step state) via prr-204's API. Intents: `explain-question`, `explain-step`, `explain-concept`, `free-form`. Session-scoped thread — discarded at session end. Tenant-isolated. "Coach, not answer key" framing enforced server-side.

## Files

- `src/student/full-version/src/components/session/SidekickDrawer.vue` (new)
- `src/student/full-version/src/components/session/SidekickIntentBar.vue` (new) — chip row: "explain question / explain step / explain concept / ask something"
- `src/student/full-version/src/components/session/SidekickMessageStream.vue` (new) — streaming message rendering (reuses existing `TutorMessageBubble` where possible)
- `src/student/full-version/src/pages/session/[sessionId]/index.vue` — mount drawer, wire context
- `src/student/full-version/src/composables/useSidekick.ts` (new) — SSE/fetch stream handling, circuit breaker, session-end teardown
- `src/student/full-version/src/api/sessions.ts` — `streamTutorTurn(sessionId, intent, userMessage?, stepIndex?)`
- `src/student/full-version/tests/unit/SidekickDrawer.spec.ts` (new)
- `src/student/full-version/tests/unit/SidekickAnswerLeak.spec.ts` (new) — client-side assertion that the rendered DOM never contains disallowed answer-disclosure patterns (defense-in-depth on top of server guard)

## Non-negotiable references

- ADR-0003 (misconception session scope) — drawer state + streamed thread destroyed at session end; no localStorage persistence.
- ADR-0001 (tenant isolation) — all network calls go through existing tenant-scoped `$api`.
- ADR-0045 (tier) — `socratic_question` tier, shares L3 cache.
- "Coach, not answer key" (prr-204 leak guard) — client adds a second-layer assertion that rendered bubbles don't contain MCQ-letter-choice disclosure patterns.
- Shipgate (§Behavioral rules) — drawer copy cannot introduce streaks, countdowns, or loss-aversion.

## Definition of Done

- Drawer is a true dialog (`role="complementary"` if side-docked, `role="dialog"` if modal-on-mobile); focus-trap on open; restore-focus on close; Esc closes.
- Affordance button is persistent but never disruptive (no pulsing animation, no notification badge, no "3 hints waiting" style counter).
- Intent chips pre-seed the request via prr-204's API — student doesn't have to type "explain this question" to get that intent.
- Productive-failure debounce: `explain-step` intent disabled for 15 seconds after a wrong step submission (client assertion; server also enforces).
- Session-end teardown: on route-leave or `SessionCompleted` event, the drawer clears state and aborts any in-flight request.
- Circuit breaker: on 5xx or stream error, drawer shows "the tutor is resting — try a hint instead" with a CTA that opens the hint ladder. No exception propagation to the runner.
- Keyboard-navigable; aria-live on streaming message region (polite); aria-label on each intent chip.
- RTL correctness: drawer docks right in LTR, left in RTL; math content wrapped in `<bdi dir="ltr">`.
- Mobile responsive: drawer becomes a modal sheet on small screens, does not obscure the active question.
- Sidekick thread UI is intentionally lighter than `/tutor` — no attachments, no PDF upload, no photo capture in-session (separate surface).
- No engagement-mechanic coupling (no "sidekick XP", no "streaks", no "you've asked 3 times today").
- Cost discipline: L3 cache reuse verified via instrumentation — duplicate session turns for the same (question, intent, step) hit cache.
- Tests: drawer open/close/focus, productive-failure debounce, circuit breaker fallback, answer-leak client guard, session-end teardown, RTL render.
- Student-web build clean; Axe CI passes with drawer open.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-sidekick --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: intent chips + productive-failure debounce prevent the drawer from short-circuiting struggle. Owned here.
- **persona-a11y**: focus-trap, restore-focus, aria-live, RTL correctness, keyboard flow. Owned here. Axe CI gate.
- **persona-ethics**: no engagement hooks, no counters, no streaks, no dark patterns. Enforced by prr-211 scanner extension.
- **persona-privacy**: session-end teardown enforced client + server. Owned here.
- **persona-redteam**: client-side answer-leak assertion is defense-in-depth; server owner is prr-204. Owned here.
- **persona-sre**: circuit breaker + graceful fallback to hint ladder. Owned here.
- **persona-finops**: cache reuse instrumentation confirms no duplicate L3 calls within a session. Owned here.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-204 (tutor context API)
- Adjacent: prr-205 (hint ladder — fallback target), prr-211 (shipgate scanner extension)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
