# TASK-PRR-249: Bagrut corpus in-app display delta — legal memo

**Priority**: P0 — blocks PRR-245 (reference library) and gates ADR-0059 acceptance
**Effort**: S (2-5 days; legal review + 1-page memo)
**Lens consensus**: persona-ministry, persona-privacy
**Source docs**: [ADR-0059 §Q1](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md), PRR-242's `docs/legal/bagrut-corpus-usage.md`
**Assignee hint**: Shaker (project owner) drives; legal counsel reviews; coordinator drafts skeleton
**Tags**: source=adr-0059-q1, epic=epic-prr-n, priority=p0, legal, compliance, memo
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

PRR-242's legal review (`docs/legal/bagrut-corpus-usage.md`) confirmed Ministry papers are publicly published under terms permitting reference use **for backend ingestion** (LLM authoring anchor). The reference-library surface in ADR-0059 introduces a new question: **does the same posture cover serving raw Ministry text to authenticated students inside Cena's UI?** Coordinator's preliminary read says yes (every Israeli prep platform does this; Ministry papers are state-published; fair-use / educational-use exception is broad), but "everyone does it" is not a legal opinion.

This task produces `docs/legal/bagrut-corpus-display-delta.md` — a 1-page delta to PRR-242's memo addressing in-app display.

## Scope

### Inputs to gather
1. Read PRR-242's `docs/legal/bagrut-corpus-usage.md` to anchor on the existing posture.
2. Israeli state-copyright duration on Bagrut papers (50 years from creation per Israeli copyright law).
3. Fair-use / educational-use exceptions — which clauses apply to in-app display vs ingestion only?
4. Survey of comparable IL prep platforms (Bagrut Plus, Bagrut Tikshoret, school-portal PDFs) — what is the de-facto industry posture, and is any of them subject to a public legal challenge?
5. Ministry of Education's published terms-of-use for the past-paper archive at edu.gov.il.
6. Whether ADR-0059's mitigations (consent disclosure + provenance citation + no-grading-on-reference + variant-routed practice) meaningfully reduce legal exposure compared to plain raw-display.

### Memo structure
1. **Posture statement**: one sentence — "in-app display under ADR-0059's mitigations is / is not within the existing licensing posture."
2. **Risk analysis**: copyright + ministry + accessibility (e.g. PDF text extraction integrity).
3. **Mitigation requirements**: what ADR-0059 must include for the posture to hold (consent, citation, no-grading, etc.).
4. **Fallback if posture is restrictive**: metadata-only display (citation + topic + structure description, no raw question text). Variant generation surface unchanged.
5. **Sign-off**: legal counsel (or the project owner taking on the risk) named, dated.

## Files

### New
- `docs/legal/bagrut-corpus-display-delta.md` — 1-page memo
- (optional) `docs/legal/comparable-platforms-survey-2026-04.md` — IL market scan

## Definition of Done

- Memo filed, reviewed, signed off.
- ADR-0059 §Q1 resolution recorded with reference to the memo.
- If the memo recommends fallback (metadata-only display), ADR-0059 §1 (Reference<T> wrapper) is updated to reflect the constrained scope; PRR-245 implementation scope adjusted accordingly.

## Blocking

- None at start. Coordinator can draft the skeleton + research inputs while Shaker arranges legal review.

## Non-negotiable references

- ADR-0059 §Q1
- Memory "Bagrut reference-only" — the underlying constraint
- PRR-242's existing legal memo

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<memo sha + signed-off-by + ADR-0059 §Q1 resolution sha>"`
