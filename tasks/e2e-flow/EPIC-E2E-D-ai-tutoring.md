# EPIC-E2E-D — AI tutoring & content-safety round-trips

**Status**: Proposed
**Priority**: P0 (CAS oracle is a design non-negotiable — ADR-0002)
**Related ADRs**: [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md), [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md), [ADR-0047](../../docs/adr/0047-no-pii-in-llm-prompts.md), [EPIC-PRR-B](../pre-release-review/EPIC-PRR-B-llm-routing-governance.md)

---

## Why this exists

AI tutoring is the product's differentiator AND its biggest liability surface. Failure modes include:

- **CAS bypass** — LLM answer ships to student without SymPy verification (ADR-0002 ship blocker)
- **PII leak** — student free-text hits an LLM provider without scrubbing (ADR-0047)
- **Tier bypass** — Basic user routes to Opus (wrong tier) → cost explodes
- **Tutor-handoff deadlock** — session hands off to AI tutor that never returns

## Workflows

### E2E-D-01 — Tutor handoff → AI chat → return to session

**Journey**: student in `/session/{id}` stuck → "Ask the tutor" button → handoff to `/tutor/chat/{id}` → AI responds CAS-verified → student back-navigates → session resumes with tutor-context preserved.

**Boundaries**: DOM (chat UI, back-navigation to session), DB (TutorSessionContext row with session linkage), LLM router (correct tier selected for the user's subscription), CAS gate (every AI math response verified), bus (`TutorHandoffInitiatedV1`, `TutorHandoffClosedV1`).

**Regression caught**: tutor responds with un-verified math, tutor-session orphaned (session can't resume), tier-6 user routed to tier-3 (stingy) or tier-3 user routed to tier-7 (expensive).

### E2E-D-02 — LLM tier enforcement by subscription

**Journey**: Basic-tier student → tutor request → routed to Haiku (tier-2) → Plus-tier student → routed to Sonnet (tier-3). Observed via OTel trace tags.

**Boundaries**: OTel `llm.model` tag matches expected tier, cost metric (`llm.cost_usd`) scales as expected.

**Regression caught**: router misses the entitlement check, costing real money per session.

### E2E-D-03 — CAS verification — happy path

**Journey**: AI generates a math explanation → SymPy sidecar verifies the algebraic chain → verified-green badge surfaces to student.

**Boundaries**: DB (CasVerificationBinding row with `Status=Verified`), NATS round-trip to `cena.cas.verify.*`, student UI shows verified badge.

**Regression caught**: sidecar timeout → verification skipped → unverified math shipped (shipgate CI scanner should also catch).

### E2E-D-04 — CAS gate fails → LLM response blocked

**Journey**: LLM produces a subtly wrong step → SymPy detects mismatch → response held, student shown "I need to double-check this" fallback (PRR-032 UX) → admin queue logs the failure.

**Boundaries**: DB (CasVerificationBinding with `Status=Failed`), admin surface (`/apps/system/cas-failures` shows the row), student UI (no wrong math surfaces), bus (`CasVerificationFailedV1` for ops alert).

**Regression caught**: failed-CAS-still-shipped; failure queue invisible to ops; student-facing fallback copy wrong.

### E2E-D-05 — PII scrubber on LLM input (ADR-0047)

**Journey**: student free-texts "my address is 5 King St, Tel Aviv" in an explanation → backend scrubber strips → LLM prompt never contains the original string.

**Boundaries**: DB audit trail shows `scrubbed=true`, the actual LLM call payload (captured by test-mode LLM recorder) contains `<redacted:address>` not the original.

**Regression caught**: PII regex drift missing new pattern → leak → compliance incident.

### E2E-D-06 — LLM token budget exhausted → graceful fallback

**Journey**: student exhausts their weekly token budget → next tutor request → denied with quota message → fallback to cached-canonical-explanation (prr-112 cost dashboard).

**Boundaries**: DOM (quota UI), DB (TokenBudget rows), cost circuit breaker triggers, no silent empty response.

**Regression caught**: quota exceeded but next request still hits the LLM (runaway cost), fallback never shown.

### E2E-D-07 — Stem-grounded hints (PRR-262)

**Journey**: student requests a hint → hint generator returns stem-grounded text (uses ONLY the question's stem + student's prior attempt, no external context) → CAS-verified.

**Boundaries**: LLM prompt audit (captured payload matches stem + attempt only, no other context bleed), DB (HintGeneratedV1 event with source=`stem-grounded`).

**Regression caught**: hint pulling from other students' sessions (ADR-0003 violation); hint ungrounded → nonsense math.

## Out of scope

- Content authoring flows (admin-side) — EPIC-E2E-G
- Tutor-session rate limiting — covered by EPIC-E2E-J (resilience)
- Multi-turn conversation memory — aspirational (not yet shipped)

## Definition of Done

- [ ] All 7 workflows green with a **test-mode LLM recorder** that captures the actual payload (not just status)
- [ ] D-03, D-04, D-05 tagged `@cas @ship-gate` — blocks merge if red (ADR-0002 + ADR-0047 are non-negotiable)
- [ ] Cost-metric assertions not flaky (use tolerance band, not exact)
- [ ] PII scrubber corpus expanded to catch region-specific patterns (Israeli ID, UK postcode, etc.)
