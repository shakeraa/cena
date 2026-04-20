# EPIC-PRR-E: Question-engine UX integration — full coverage + in-session hint ladder, step-solver, sidekick tutor

**Priority**: P0 (ship-blocker for the question-engine story)
**Effort**: XL (epic-level: 4-6 weeks aggregate across 12 sub-tasks)
**Lens consensus**: persona-cogsci, persona-educator, persona-a11y, persona-ethics, persona-enterprise, persona-finops, persona-ministry, persona-privacy, persona-redteam, persona-sre
**Source docs**: `docs/research/cena-question-engine-architecture-2026-04-12.md:§4` (parametric generation, ~80% coverage), `docs/research/cena-question-engine-architecture-2026-04-12.md:§7` (step-solver), `docs/adr/0045-hint-and-llm-tier-selection.md` (hint ladder tiering), `docs/adr/0002-sympy-correctness-oracle.md` (CAS oracle), `docs/adr/0032-cas-gated-question-ingestion.md` (ingestion gate)
**Assignee hint**: human-architect (epic coordination) + named sub-agents per sub-task
**Tags**: source=pre-release-review-2026-04-20, type=epic, epic=epic-prr-e
**Status**: Not Started
**Source**: Session review 2026-04-20 identified two categorical gaps between the designed question engine and the shipping product: (a) content coverage stalls at the ~80% parametric-addressable ceiling because Strategies 2 (LLM isomorph) and 3 (difficulty laddering) are not wired end-to-end; (b) the student runner renders MCQ + single inline hint only, while `HintLadder.vue`, `StepSolverCard.vue`, `FreeBodyDiagramConstruct.vue`, and the Tutor surface are built but not integrated. This epic closes both gaps to a production-ready bar.

---

## Epic goal

Ship question-engine UX that honors the full architecture doc, not its MCQ-only subset:

1. **100% coverage** of the active Bagrut 4-unit and 5-unit curriculum via a three-stage waterfall (deterministic parametric → LLM-isomorph w/ CAS verify → human-curator queue) with a measurable per-rung SLO — no `(topic, difficulty, methodology)` tuple ships under-covered.
2. **In-session hint ladder** (ADR-0045): L1 deterministic template → L2 Haiku → L3 Sonnet, wired into the runner via `HintLadder.vue`, replacing the current single-hint button.
3. **Step-solver + MathInput** routed for non-MCQ question types, with `StepSolverCard.vue` receiving CAS-verified step feedback and `FreeBodyDiagramConstruct.vue` active on physics.
4. **In-session sidekick tutor drawer** — the existing `TutorActor` surface (today only at `/tutor`) becomes a collapsible, session-context-seeded panel co-located with the question, honoring ADR-0003 misconception scope and ADR-0001 tenant isolation.

## Architectural substrate

Four already-decided ADRs are the substrate; this epic is pure integration, not new architecture:

- ADR-0002 (SymPy correctness oracle) — every variant and every step verified by CAS before student exposure.
- ADR-0032 (CAS-gated ingestion) — all three waterfall stages funnel through `ICasVerificationGate.VerifyForCreateAsync`.
- ADR-0045 (hint/tutor tier selection) — L1 no-LLM, L2 Haiku, L3 Sonnet; turn-budgets inherited from `SocraticCallBudget`.
- ADR-0003 (misconception session scope) — sidekick tutor context resets at session end; no profile-scoped misconception persistence.

## Absorbed tasks (12)

| ID | Title | Priority | Role in epic |
|---|---|---|---|
| prr-200 | Deterministic parametric template engine (Strategy 1, no-LLM) | P0 | foundation |
| prr-201 | Coverage waterfall orchestrator (template → LLM-isomorph → curator-queue) | P0 | foundation |
| prr-202 | Admin: parametric template authoring (CRUD + slot constraints + live preview) | P1 | feature |
| prr-203 | Backend: hint-ladder endpoint `POST /sessions/{sid}/question/{qid}/hint/next` (L1/L2/L3 per ADR-0045) | P0 | foundation |
| prr-204 | Backend: in-session tutor context API + session-scoped pre-seed | P0 | foundation |
| prr-205 | Student: wire `HintLadder.vue` into session runner; deprecate inline single-hint | P0 | feature |
| prr-206 | Student: route step-solver items to `StepSolverCard` + `MathInput` | P0 | feature |
| prr-207 | Student: in-session Sidekick drawer (tutor panel co-located with session) | P0 | feature |
| prr-208 | Student: wire `FreeBodyDiagramConstruct` for physics question items | P1 | feature |
| prr-209 | Admin: content-coverage heatmap (topic × difficulty × methodology × track) | P1 | feature |
| prr-210 | Coverage SLO ship-gate: ≥N parametric variants per rung, CI-enforced | P0 | foundation |
| prr-211 | Ship-gate scanner v2 extension — `HintLadder`, `StepSolverCard`, `Sidekick` DOM scanned for banned vocabulary | P1 | foundation |

Absorbed task files live under `tasks/pre-release-review/TASK-PRR-NNN-*.md`.

## Suggested execution order

1. **prr-200** — deterministic parametric engine (unblocks Strategy 1 throughput).
2. **prr-210** — coverage SLO ship-gate (defines "done" before implementers start filling coverage).
3. **prr-201** — waterfall orchestrator (glues Strategy 1/2/3 + human curator queue).
4. **prr-202** — admin template authoring (so content team can feed prr-200 at scale).
5. **prr-203** — hint-ladder backend (unblocks prr-205).
6. **prr-204** — in-session tutor context API (unblocks prr-207).
7. **prr-205, prr-206, prr-208** — runner wiring in parallel (independent Vue components).
8. **prr-207** — sidekick drawer (depends on 204 + runner stability from 205/206).
9. **prr-209** — admin coverage heatmap (depends on 201's telemetry).
10. **prr-211** — shipgate scanner extension (gates the new DOM surfaces).

## Epic-level DoD (in addition to per-task DoDs)

- **Coverage proof**: `ops/reports/coverage-rung-status.md` shows 100% of active (topic, difficulty, methodology, track) rungs with ≥N ready variants — no red cells. N is per rung per prr-210's schedule.
- **E2E scenario**: one named smoke test (`tests/e2e/session-full-stack.spec.ts` or equivalent) drives: student opens a step-solver question generated via waterfall → opens HintLadder → requests L1 (template) → requests L2 (Haiku) → opens Sidekick drawer → asks "explain this step" → Tutor responds with session-scoped context → CAS verifies the next submitted step → session completes, misconception store flushes.
- **Shipgate pass**: ship-gate scanner v2 (prr-211) exits 0 on `student-web` build.
- **No stubs / no "Phase 1b later"** per 2026-04-11 user decision: every surface ships production-grade or doesn't ship.
- **Full `Cena.Actors.sln` clean build** on every PR in the epic (not branch-only).
- **SYNTHESIS.md epic section** updated on completion.
- **No absorbed task ships standalone** — the epic is atomic from a UX standpoint; partial delivery is worse than none because it reintroduces the gap the epic exists to close.

## Multi-persona lens re-review (2026-04-20)

This epic was re-reviewed against each persona lens after scoping. Concrete findings below — each is owned by one or more absorbed sub-tasks.

### persona-a11y
- HintLadder rungs must use tonal alerts, never warning color (penalty semantics forbidden). `aria-live="polite"` on the ladder region. Keyboard traversal: Tab enters ladder, Enter expands rung, Esc closes. Owned by prr-205.
- Sidekick drawer must be a true dialog with focus-trap, `role="complementary"`, restore-focus on close; NOT a click-away-dismisses overlay. Owned by prr-207.
- MathInput must expose aria-label in the user's language + voice-input alternative (deferred-allowed if tracked). Owned by prr-206.
- Coverage heatmap color cells must not rely on color alone — shape or pattern conveys state. Owned by prr-209.

### persona-cogsci
- L1 being template-driven (no LLM) is non-negotiable per ADR-0045; prr-203 must reject a PR that routes L1 through any LLM path.
- Expertise-reversal: students at mastery > 0.60 (BKT) should see `Minimal` scaffolding by default; the HintLadder must hide by default for them and surface via explicit "I'm stuck" affordance. Owned by prr-205.
- Productive failure path: Sidekick must not short-circuit struggle — debounce explanation requests while the student is actively typing/working, and offer a "try again" before giving the method. Owned by prr-207.
- Step-solver hint deliveries must tick `HintAdjustedBktService` so mastery estimates reflect scaffolded vs unscaffolded attempts. Owned by prr-206.

### persona-educator
- Teacher surface: coverage heatmap must be exposed to the teacher role (not only admin) with read-only scope over the teacher's cohort. Owned by prr-209.
- Bagrut track parity: 4-unit and 5-unit tracks must each reach 100% coverage independently; the heatmap separates the two tracks as a hard axis. Owned by prr-210.
- Methodology axis (Halabi vs Rabinovitch) per ADR-0040 must also be a heatmap axis — a variant authored for Rabinovitch doesn't cover the Halabi column. Owned by prr-209, prr-210.

### persona-enterprise
- Tenant isolation (ADR-0001): Sidekick tutor threads must be tenant-scoped; no cross-institute thread visibility even at the actor layer. Owned by prr-204, prr-207.
- Template authoring (prr-202) must respect the existing role matrix — content-author and super-admin only, never teacher.
- Coverage heatmap aggregates must respect k-anonymity floor per prr-026 (k=10) when surfaced to non-super-admin roles. Owned by prr-209.

### persona-ethics
- Sidekick must not be streak-coupled, daily-quota-coupled, or loss-aversion-styled. "Show me the solution" escape must be always-visible (consistency with prr-029). Owned by prr-207, prr-211.
- No variable-ratio reward mechanic on hint consumption. Hint-ladder L3 delivery is deterministic on request, not gated by a "reward" unlock. Owned by prr-205.

### persona-finops
- L1 template path is $0/call and must stay that way (lint rule: no `anthropic.*` or `openai.*` import in the L1 codepath). Owned by prr-203, prr-211.
- Every waterfall stage must carry a `[TaskRouting]` tag per ADR-0026; the scanner in prr-211 must fail the build on an untagged call.
- Coverage filling costs: LLM-isomorph stage has a per-institute daily spend cap + a global cap; prr-201 must emit the metric and prr-201 DoD includes the Grafana panel.
- L3 worked-example surface through Sidekick must share the L3 explanation cache (reuse `ExplanationCacheService`); duplicate L3 calls within a session are a bug, not a product choice. Owned by prr-207.

### persona-ministry
- Reference-only Ministry material (ADR-0043): the waterfall cannot emit a variant that is a minor surface edit of a Ministry stem — prr-201 must run the similarity check against the Ministry corpus and reject high-similarity outputs. Owned by prr-201.
- Bagrut curriculum alignment per `docs/research/cena-question-engine-architecture-2026-04-12.md:§22` — coverage rubric uses the Ministry topic taxonomy verbatim. Owned by prr-209, prr-210.

### persona-privacy
- Sidekick thread context: misconception data flows into the prompt scratchpad but must not be persisted on the student profile (ADR-0003). Session-end flush verified by an integration test. Owned by prr-204.
- Free-form tutor input must run through the PII scrubber (`prr-036` pattern) before persistence of the transcript. Owned by prr-204.
- Sub-processor registry (prr-035): if Sidekick calls a new vendor tier (e.g., Kimi fallback), the DPA must list it before the feature flag flips on. Owned by prr-204.

### persona-redteam
- Sidekick prompt-injection: free-form student input into the tutor is the #1 injection vector for the LLM stack. prr-204 must run AIMDS / `aidefence_analyze` on every inbound turn and reject on high-confidence manipulation.
- Answer-leak: Sidekick must not reveal the MCQ correct answer when asked directly ("which one is right?") — the tutor prompt must enforce a "you are a coach, not an answer key" rule, and CAS-verified step-equivalence is the only allowed disclosure channel. Owned by prr-204, prr-207.
- Template authoring (prr-202) must sanitize admin-supplied LaTeX through the same LaTeX-sanitization pipeline the ingest path uses (§28 of the engine doc).

### persona-sre
- Hint-ladder endpoint (prr-203) must have explicit fallback: if Haiku is down at L2, degrade to L1 template with an info-level log line (not 5xx). If Sonnet is down at L3, degrade to the cached `ExplanationCacheService` entry for the nearest rung. Runbook lives next to the endpoint.
- Sidekick must have a circuit breaker: on tutor-LLM failure, the drawer shows "the tutor is resting — try a hint instead" and links to HintLadder, never 500s. Owned by prr-207.
- Coverage dashboard (prr-209) is a read model, not a live query — it rebuilds from projections off the event stream on a 5-minute schedule. Owned by prr-209.
- SLOs: hint L1 p99 ≤ 50ms, L2 p99 ≤ 800ms, L3 p99 ≤ 2500ms, Sidekick first-token p99 ≤ 1200ms. Named in prr-203 and prr-207.

## Coverage definition (the "100%" claim is defined, not handwaved)

"Full production-ready coverage" for this epic means, for every cell in the matrix:

```
track       ∈ {4-unit, 5-unit}
subject     ∈ {algebra, functions, calculus, trig, geometry, probability, physics-kinematics, physics-dynamics, physics-em, physics-thermo}
topic       ∈ Ministry taxonomy (per §22 of engine doc)
difficulty  ∈ {easy, medium, hard}          # three rungs per §4.1 Strategy 3
methodology ∈ {Halabi, Rabinovitch}         # per ADR-0040
question_type ∈ {MCQ, step-solver, FBD-construct}
```

There must be:

- **≥5 ready variants** in the question bank (`QuestionStatus = Published`),
- **CAS-verified** (`QuestionCasBinding.Status = Verified`),
- **Quality-gate passed** (`QualityGateScore.Total ≥ 85`),
- **Methodology-tagged**,
- **Track-tagged**,
- per cell.

Cells with `question_type = MCQ` are satisfied by existing plus waterfall output. Cells with `question_type = step-solver` require prr-206 to have landed. Cells with `question_type = FBD-construct` require prr-208. prr-210 is the CI-enforced SLO that blocks release until the heatmap is green.

## Epic triage decisions 2026-04-20 (user)

**Adopted**: full scope, all 12 sub-tasks, multi-persona lens review above.

**Tightenings**:

1. **"Full production-ready coverage" means 100% of the matrix above, not "most cells".** prr-210 makes this a release gate, not aspiration.
2. **No 80% acceptance.** The design-doc's "~80% parametrizable" figure refers to Strategy 1 alone; the epic exists specifically to cover the remaining 20% via Strategies 2 + 3 + curator queue. prr-201 owns the waterfall that guarantees zero gaps.
3. **No stubs — production grade** (user feedback 2026-04-11): every sub-task ships production-quality or doesn't ship. No "Phase 1 stub → Phase 1b real" pattern.
4. **Runner UX must be atomic.** prr-205/206/207/208 ship as a coordinated release; partial rollout reintroduces the gap the epic closes.
5. **Sidekick is not a chatbot toy.** prr-207's DoD explicitly forbids a generic "ask me anything" framing — the drawer is session-context-bound and CAS-anchored.

**Tags**: user-decision=2026-04-20-epic-e-triaged

---

## Implementation Protocol — Senior Architect

Implementation of this epic must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this epic exist?** Two categorical gaps between the designed question engine and the shipping product: coverage ceiling and runner UX regression against the architecture doc.
- **Why this priority?** Without closing the coverage gap, the "content at scale" pillar is aspirational. Without the UX gaps, the Tutor and Step-solver surfaces are dead code.
- **Why these files?** Trace the question document lifecycle end-to-end: admin authoring → waterfall → CAS gate → published → session-runner render → hint/step/sidekick interactions → CAS verification of student input → misconception telemetry (session-scoped) → session summary.
- **Why are the non-negotiables above relevant?** Every surface in this epic touches at least one of: CAS oracle (ADR-0002), misconception session scope (ADR-0003), dark-pattern ban (shipgate), tenant isolation (ADR-0001), hint tiering (ADR-0045).

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** `QuestionBank`, `Session`, `Tutor`, `Explanation`, `Hint`, `CasRouter`, `Curriculum`.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?** Every new endpoint tenant-scoped; every content artifact produced via event sourcing through `QuestionBankService`; every math answer gate-checked by CAS; every sidekick turn session-scoped and flushed at session end.
- **How will it fail?** Runbook per endpoint (see persona-sre above). Bagrut exam morning scenario: what if Sonnet is down for 15 minutes at 08:30 IDT? Answer: L3 degrades to cache, Sidekick shows "tutor resting" and redirects to HintLadder, session continues.
- **How will it be verified end-to-end, with real data?** The named E2E scenario in Epic-level DoD, run against a seeded integration DB with a real CAS sidecar and a vendor-mocked LLM, asserting field names + tenant scoping.
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?** Each sub-task DoD reaffirms; shipgate v2 (prr-211) extended to scan the new surfaces.

### Before committing

- Full `Cena.Actors.sln` builds cleanly.
- Named E2E scenario passes locally + in CI.
- Coverage heatmap green for the tracks and methodologies claimed in the release tag.
- Shipgate scanner v2 exits 0.
- No dark-pattern copy introduced on any of the 4 new UX surfaces.

### If blocked

- Fail loudly: `node .agentdb/kimi-queue.js fail <id> --worker <you> --reason "<specific blocker>"`.
- Do not silently reduce scope. Do not skip a persona-lens finding without promoting it to its own task with a referenced prr-id.

### Definition of done is higher than the checklist above

- Labels match data on every new UI surface (HintLadder rung labels, Sidekick drawer title, coverage heatmap axes all describe the data they show).
- Root cause fixed, not masked — if the waterfall has a structural coverage gap in a specific topic, that gap gets an owned task, not a suppression.
- Observability added (metrics, structured logs with tenant/session IDs, runbooks).
- Related personas' cross-lens handoffs addressed or explicitly deferred with a new prr-id.

**Reference**: full protocol and rationale live in [`/tasks/pre-release-review/README.md`](./README.md#implementation-protocol-senior-architect).

---

## Related

- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Question engine architecture reference](../../docs/research/cena-question-engine-architecture-2026-04-12.md)
- [ADR-0045 hint/LLM tier](../../docs/adr/0045-hint-and-llm-tier-selection.md)
- [ADR-0032 CAS-gated ingestion](../../docs/adr/0032-cas-gated-question-ingestion.md)
- [ADR-0002 SymPy oracle](../../docs/adr/0002-sympy-correctness-oracle.md)
- [ADR-0003 misconception session scope](../../docs/adr/0003-misconception-session-scope.md)
- [ADR-0040 methodology parity](../../docs/adr/0040-accommodation-scope-and-bagrut-parity.md)
- [ADR-0043 Bagrut reference-only](../../docs/adr/0043-bagrut-reference-only-enforcement.md)
