# TASK-PRR-361: Canonicalization layer (equivalent-form normalization)

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #7 ML safety, #3 teacher
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + math-SME
**Tags**: epic=epic-prr-j, cas, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Normalize equivalent expressions so `(x-2)(x+3) ≡ x²+x-6` doesn't flag as "different step" when student expanded to a valid form.

## Scope

- Canonical form per expression class: polynomial expanded + simplified; rational simplified; radical simplified; trig reduced.
- Preserve original form for display (student sees their work, not normalized form).
- Used before step-to-step comparison in `StepChainVerifier`.

## Files

- `src/backend/Cena.Diagnostic/CAS/Canonicalizer.cs`
- Tests: equivalence pairs.

## Definition of Done

- Equivalent forms match as equal.
- Non-equivalent forms remain distinct.
- Display form preserved.

## Non-negotiable references

- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-360](TASK-PRR-360-step-chain-verifier.md)
