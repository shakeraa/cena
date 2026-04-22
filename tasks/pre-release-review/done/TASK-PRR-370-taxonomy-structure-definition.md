# TASK-PRR-370: Misconception taxonomy structure definition

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #7 ML safety (closed-set mapping is the hallucination defense)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + math-education SME
**Tags**: epic=epic-prr-j, domain-model, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Define the data structure for the misconception taxonomy before SME content authoring ([PRR-371](TASK-PRR-371-bagrut-math-4-taxonomy.md)) begins.

## Scope

- `BreakType` enum (sign-flip-distributive, minus-as-subtraction, premature-cancellation, etc.).
- `MisconceptionTemplate { templateId, breakType, triggerConditions, studentFacingExplanation: {he, ar, en}, exampleCounterCase, suggestedNextStep }`.
- `TriggerConditions` = regex-like pattern on `VerificationResult` (e.g., "expected polynomial with sign X, detected sign Y").
- Versioned storage with audit trail.
- Governance workflow ([PRR-375](TASK-PRR-375-taxonomy-governance.md)).

## Files

- `src/backend/Cena.Domain/Diagnostic/Misconception/MisconceptionTemplate.cs`
- `src/backend/Cena.Domain/Diagnostic/Misconception/BreakType.cs`
- Tests.

## Definition of Done

- Structure supports all v1 Bagrut-Math-4 template shapes.
- Versioning works.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — data retention rules.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-371](TASK-PRR-371-bagrut-math-4-taxonomy.md), [PRR-374](TASK-PRR-374-template-matching-scorer.md), [PRR-375](TASK-PRR-375-taxonomy-governance.md)
