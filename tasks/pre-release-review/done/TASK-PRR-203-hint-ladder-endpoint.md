# TASK-PRR-203: Backend — hint-ladder endpoint `POST /sessions/{sid}/question/{qid}/hint/next` (L1/L2/L3 per ADR-0045)

**Priority**: P0 — ship-blocker (unblocks prr-205)
**Effort**: M — 3-5 days
**Lens consensus**: persona-cogsci, persona-educator, persona-a11y, persona-ethics, persona-finops, persona-sre
**Source docs**: `docs/adr/0045-hint-and-llm-tier-selection.md`, `docs/adr/0026-llm-three-tier-routing.md`, `docs/research/cena-question-engine-architecture-2026-04-12.md:§7`
**Assignee hint**: kimi-coder (backend)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+ethics+finops+sre
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Ship the hint-ladder endpoint defined by ADR-0045: L1 template (no LLM), L2 Haiku (`ideation_l2_hint`), L3 Sonnet (`worked_example_l3_hint`). Existing single-hint route is deprecated by migration — new route is additive, old route proxies to L1 for a grace window then is removed. Respects scaffolding level, BKT mastery, anxiety-safe copy, and the no-dark-pattern invariant.

## Files

- `src/api/Cena.Admin.Api/Sessions/HintLadderEndpoint.cs` (new or extended from existing hint endpoint)
- `src/actors/Cena.Actors/Hints/HintLadderOrchestrator.cs` (new)
- `src/actors/Cena.Actors/Hints/L1TemplateHintGenerator.cs` (new — no LLM; deterministic step-index lookup + i18n template)
- `src/actors/Cena.Actors/Hints/L2HaikuHintGenerator.cs` (extends existing `HintGenerator`, tagged `[TaskRouting(2, "ideation_l2_hint")]`)
- `src/actors/Cena.Actors/Hints/L3WorkedExampleHintGenerator.cs` (new, tagged `[TaskRouting(3, "worked_example_l3_hint")]`)
- `src/actors/Cena.Actors/Hints/HintAdjustedBktService.cs` (extend — rung-aware penalty)
- `src/student/full-version/src/api/types/common.ts` (extend `SessionHintResponseDto` with `hintLevel`, `rungSource`)
- `src/actors/Cena.Actors.Tests/Hints/HintLadderOrchestratorTests.cs` (new)
- `contracts/llm/routing-config.yaml` — add `ideation_l2_hint` and `worked_example_l3_hint` rows

## Non-negotiable references

- ADR-0045 (hint tier) — L1 MUST NOT import `Anthropic.SDK`, `OpenAI`, `ITutorLlmService`, etc. Enforced by test + shipgate scanner.
- ADR-0026 (tier routing) — every generator tagged `[TaskRouting]`; scanner blocks silent-default-to-Sonnet.
- ADR-0002 (SymPy oracle) — L1 templates may use SymPy-rendered math snippets, never arbitrary unverified math.
- ADR-0003 (misconception session scope) — ladder consumes session-scoped misconception tally, does not persist.

## Definition of Done

- Endpoint `POST /api/sessions/{sid}/question/{qid}/hint/next` returns `{ hintLevel: 1|2|3, hintText, hintsRemaining, rungSource: "template"|"haiku"|"sonnet" }`.
- Rung advancement: server-enforced — client cannot skip L1 by passing `level: 2`. Server tracks per-question rung state in the session aggregate.
- L1 is deterministic: given (question_id, step_state, locale, methodology), identical output. Zero LLM calls. Zero seconds of spend.
- L2 is Haiku-tier, cache-aware (reuses `ExplanationCacheService` key scheme).
- L3 is Sonnet-tier, cache-aware, budget-capped via `SocraticCallBudget`.
- Expertise-reversal: when student BKT mastery for the question's concept > 0.60, the ladder is hidden by default and only surfaces on explicit "I'm stuck" click — enforced server-side by returning `hintsRemaining: 0` until the client explicitly requests.
- Anxiety-safe copy: rung text passes the shipgate scanner (extended in prr-211) — no "Hint 1 of 5", no visible BKT credit deduction, no shame counters.
- Fallback chain (persona-sre): Haiku down → log-info + L1 template; Sonnet down → log-info + cached L3 if available, else L2 upgrade.
- Tenant isolation: every call tenant-scoped via session aggregate.
- Metrics: `cena_hint_rung_delivered_total{rung,source}`, `cena_hint_rung_latency_seconds{rung}`, `cena_hint_fallback_total{from,to,reason}`.
- Tests: deterministic L1 replay, tier-enforcement (reject L2 request before L1 consumed), fallback chain, expertise-reversal gate, anxiety-safe copy assertion.
- SLOs documented in endpoint header comment: L1 p99 ≤ 50ms, L2 p99 ≤ 800ms, L3 p99 ≤ 2500ms.
- Full `Cena.Actors.sln` clean build; routing-config.yaml rows added.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: L1 template-only enforced by test; expertise-reversal gate at BKT > 0.60. Owned here.
- **persona-educator**: L3 worked example uses methodology-aware prompt per ADR-0040 — a Halabi student gets a Halabi-framed worked example. Owned here.
- **persona-a11y**: response payload carries `rungSource` so the UI can announce the correct aria-live text; ladder UI owned by prr-205.
- **persona-ethics**: `show_solution_always_available` flag always true in response; no escape hatch suppression. Consistency with prr-029.
- **persona-finops**: L1 zero-cost enforced; L2 cache-aware; L3 cached + budget-capped. Metrics per rung for dashboard (prr-046).
- **persona-sre**: fallback chain + SLOs owned here.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Consumer: prr-205 (student UI wiring)
- ADR: ADR-0045 tier policy

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
