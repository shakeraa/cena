# RDY-050: `QuestionIngested_V1` vs `V2` — Reconcile

- **Priority**: Medium — event-schema hygiene
- **Complexity**: Low
- **Effort**: 1-2 hours

## Problem

`IngestionOrchestrator` emits `QuestionIngested_V1`. `QuestionBankService` and ADR-0032 use `QuestionIngested_V2`. The arch-test regex and seed-loader block-list must cover both.

## Scope

Decide + execute:
- Migrate `IngestionOrchestrator` to `QuestionIngested_V2` (if V2 exists)
- OR explicitly document that V1 is the ingestion shape + V2 is the authoring shape, and ensure both appear in every relevant block-list / event-type alias
- Either way: arch-test regex must match both

## Acceptance

- [ ] One canonical event story documented in ADR-0032
- [ ] Arch-test regex covers both versions
