# TASK-PRR-374: Template-matching scorer — pick best misconception template

**Priority**: P0
**Effort**: L (2-3 weeks)
**Lens consensus**: persona #7 ML safety (closed-set matching, not freeform)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: ML-engineer + backend
**Tags**: epic=epic-prr-j, ml, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Given a CAS `VerificationResult` + extracted step sequence, score each candidate misconception template and return the best-fit one (if confidence >threshold) or conservative "let me check with your teacher" message (if not).

## Scope

- Template trigger conditions evaluated against the CAS break signature.
- Secondary LLM pass (Haiku-tier first, Sonnet-fallback) to pick between candidates when multiple match.
- Confidence score per template; threshold for "surface template" vs. "conservative fallback" (initial threshold: 0.70).
- Track per-template match rate and dispute rate for iteration.

## Files

- `src/backend/Cena.Diagnostic/Misconception/TemplateMatcher.cs`
- Tests with representative CAS breaks.

## Definition of Done

- Given a known break signature, correct template surfaces.
- Ambiguous cases → conservative fallback.
- Confidence threshold configurable.
- Full sln green.

## Non-negotiable references

- Memory "No stubs".
- Memory "Honest not complimentary" — when unsure, say so.
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) — Haiku-first, Sonnet-fallback.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-370](TASK-PRR-370-taxonomy-structure-definition.md), [PRR-371](TASK-PRR-371-bagrut-math-4-taxonomy.md), [PRR-421](TASK-PRR-421-template-selection-confidence-tracking.md)
