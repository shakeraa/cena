# TASK-PRR-248: 6-persona review for ADR-0059 (reference library + variants)

**Priority**: P0 — blocks PRR-245 (reference library implementation) and ADR-0059 acceptance
**Effort**: M (3-5 days; parallel persona-agent runs + finding-file synthesis)
**Lens consensus**: this task IS the lens consensus
**Source docs**: [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md) §Q2 (lists the 6 lenses + per-lens prompts)
**Assignee hint**: claude-code (coordinator) — runs 6 parallel persona sub-agents and synthesizes findings
**Tags**: source=adr-0059-q2, epic=epic-prr-n, priority=p0, persona-review, governance
**Status**: Ready
**Tier**: launch-adjacent (gates PRR-245 implementation)
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Run the 6 persona lenses listed in ADR-0059 §Q2 against the reference-library + variant-generation design. File findings under `pre-release-review/reviews/persona-{lens}/reference-library-findings.md`. Synthesize verdicts into ADR-0059 §History; flip Status from Proposed to Accepted (or add revisions and re-run on the affected lens) once all six file a yellow-or-greener verdict.

## Lenses

Each lens follows the ADR-0050 review template (verdict ∈ {green, yellow, red}, findings file with per-prompt response, prioritized recommendations, sign-off).

### 1. persona-privacy
- Consent token model + 90-day expiry — defensible under PPL Amendment 13?
- RTBF cascade — `BagrutReferenceConsentGranted_V1` event crypto-shreddable on right-to-be-forgotten?
- Audit log retention — `BagrutReferenceItemRendered_V1` event retention policy?

### 2. persona-ministry
- In-app display of past Bagrut text vs PRR-242 backend-ingestion legal posture — are these legally distinct surfaces, and if so, what additional posture is required? (Coordinate with PRR-249 legal-delta memo.)
- Ministry attitude toward third-party prep tools surfacing past papers — known case-law or precedent in IL?

### 3. persona-cogsci
- Reference-anchor effect: does showing the Ministry question before the variant create a productive worked-example anchor (Sweller 1998), or does it bias the student toward pattern-matching the variant against the source?
- Variant cadence: are 20 parametric/day + 3 structural/day calibrated for desirable difficulty, or do they encourage over-practice?

### 4. persona-finops
- Confirm cost ceiling against ADR-0026 budget + PRR-244 pricing-resolver. Upper bound on a single student's variant spend per month?
- Prompt-cache hit rate impact — does the new structural variant route degrade the PRR-047 SLO floor (≥70% hit rate)?
- Storage cost for persisted variants (cached after first generation) — is the de-dup keying sufficient to keep growth linear?

### 5. persona-redteam
- Rate-limit bypass surface (enumerate variants by spamming source IDs across days; account-rotation).
- IDOR on variant ownership (can student A read student B's generated variant?).
- Consent-token forgery (is the token cryptographically bound to studentId?).
- Variant content injection — can a malicious source-question (if any landed in corpus through ingest pipeline error) leak through into a student-facing variant?

### 6. persona-a11y
- Reference-page DOM walkable in screen readers; Ministry text fragments correctly announced (math content, RTL Hebrew/Arabic, BDI fences).
- Consent disclosure surfaced as `aria-live` + dismissible via keyboard, not a modal trap.
- Variant-tier picker discoverable without a mouse.
- Reduced-motion respected on reference→variant transitions.

## Process

1. Coordinator (claude-code) spawns 6 sub-agents in parallel via the Task tool, one per lens, each with:
   - The lens prompts (from ADR-0059 §Q2 verbatim).
   - Read-only access to the codebase + ADR-0059 + ADR-0043 + ADR-0050 + the relevant existing persona-axis findings under `pre-release-review/reviews/persona-{lens}/`.
   - Instructions to file findings at `pre-release-review/reviews/persona-{lens}/reference-library-findings.md` with the canonical schema (verdict, prompts answered, recommendations, sign-off).
2. Coordinator synthesizes all 6 finding files into ADR-0059 §History delta + "Persona Review Synthesis" section (mirrors ADR-0050 §14).
3. Address red verdicts before flipping ADR-0059 to Accepted. Yellow verdicts may flip with documented mitigation tasks.
4. If any lens demands a fundamental redesign, this task fails with reason; redesign happens before re-run.

## Files

### New
- `pre-release-review/reviews/persona-privacy/reference-library-findings.md`
- `pre-release-review/reviews/persona-ministry/reference-library-findings.md`
- `pre-release-review/reviews/persona-cogsci/reference-library-findings.md`
- `pre-release-review/reviews/persona-finops/reference-library-findings.md`
- `pre-release-review/reviews/persona-redteam/reference-library-findings.md`
- `pre-release-review/reviews/persona-a11y/reference-library-findings.md`

### Modified
- `docs/adr/0059-bagrut-reference-browse-and-variant-generation.md` — synthesis section + Status flip

## Definition of Done

- All 6 finding files filed.
- ADR-0059 either Accepted (with synthesis section), or kicked back for revision with a follow-up review task spawned for affected lens.
- Any red verdicts addressed in writing in ADR-0059 (not deferred).
- Spawn-and-merge total elapsed ≤ 5 days (parallel persona runs).

## Blocking

- None — ready to start.

## Non-negotiable references

- [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md) §Q2.
- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md) §14 (review template precedent).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<6 finding-file shas + ADR-0059 acceptance sha or revision plan>"`
