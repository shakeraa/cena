# TASK-PRR-371: Initial Bagrut-Math-4 misconception taxonomy (40+ templates)

**Priority**: P0 — launch-blocker (no ship without the taxonomy)
**Effort**: XL (4-6 weeks math-education SME FTE equivalent)
**Lens consensus**: persona #7 ML safety (closed-set is mandatory)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: math-education SME (contracted or in-house) + content-eng
**Tags**: epic=epic-prr-j, content, sme-required, priority=p0, sme-gate, launch-blocker
**Status**: **SME gate** — resource needs approval (§5 decision #1)
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Author the initial Bagrut Math 4-unit misconception taxonomy: minimum 40 templates covering 80% of common student errors. HE + AR + EN student-facing explanations. Each template CAS-pattern-triggered, no freeform.

## Scope

Template categories (representative):

- Sign-flip distributive errors
- Minus-as-subtraction confusions
- Premature cancellation
- Factoring errors (incomplete, sign, like-terms, common factor missed)
- Quadratic formula sign errors
- FOIL mistakes
- Fraction-over-fraction errors
- Exponent-rule slips (e.g., `(a+b)² ≠ a² + b²`)
- Radical simplification errors
- Sine/cosine sign slips in unit-circle reasoning
- Logarithm rule misapplications
- Function composition direction errors

Each template:
- Mathematical trigger condition (CAS break signature)
- Student-facing explanation in HE/AR/EN (grade-11 reading level)
- Concrete counter-example showing the correct move
- Suggested next-step hint
- SME signoff recorded

## Files

- Taxonomy content stored as DB seed + version-controlled YAML/JSON under `content/misconception-taxonomy/bagrut-math-4/`.
- SME signoff log.

## Definition of Done

- ≥40 templates authored, peer-reviewed, and SME-approved.
- All three locale variants.
- Trigger conditions machine-readable and unit-tested.
- At least 2 SMEs sign off on content.

## Non-negotiable references

- Memory "No stubs — production grade" — real templates, real explanations.
- Memory "Honest not complimentary" — explanations diagnose, don't flatter.
- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-370](TASK-PRR-370-taxonomy-structure-definition.md), [PRR-372](TASK-PRR-372-bagrut-math-5-taxonomy.md)
