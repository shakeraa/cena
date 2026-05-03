# RDY-006: ML Training Exclusion Tag (ADR-0003 Enforcement)

- **Priority**: Critical — ADR-0003 compliance requirement
- **Complexity**: Mid engineer — tagging + test
- **Source**: Expert panel audit — Ran (Security)
- **Tier**: 1
- **Effort**: 2 days

## Problem

ADR-0003 Decision 3 mandates: "Misconception data MUST be excluded from any corpus used for LLM fine-tuning, RLHF, embedding model training, or recommendation model training." Implementation specified: a filter tag `[ml-excluded]` on misconception event types, enforced by a test that scans any training data pipeline for events with this tag.

No `[ml-excluded]` tag exists in the codebase. No test enforces exclusion.

## Scope

### 1. Add `[ml-excluded]` attribute to misconception events

Create a `[MlExcluded]` attribute and apply it to all misconception-related event types:
- `MisconceptionDetected`
- `MisconceptionRemediated`
- `ErrorClassified` (when classification is misconception-based)

### 2. Add enforcement test

Create a test that:
- Scans all event types used in any training data export/pipeline
- Asserts none carry the `[MlExcluded]` attribute
- Fails if a misconception event is found in training data scope

### 3. Add structured log marker

Every misconception event should log: `[MISCONCEPTION][ML-EXCLUDED] session={id} topic={id} rule={id}`

## Files to Modify

- New: `src/shared/Cena.Infrastructure/Compliance/MlExcludedAttribute.cs`
- `src/actors/Cena.Actors/Events/PedagogyEvents.cs` — apply attribute to misconception events
- New: `tests/Cena.Infrastructure.Tests/Compliance/MlExclusionTests.cs`
- `src/actors/Cena.Actors/Services/ErrorClassificationService.cs` — add log marker

## Acceptance Criteria

- [ ] `[MlExcluded]` attribute exists and is applied to all misconception event types
- [ ] Test scans event type registry and fails if `[MlExcluded]` events appear in any training pipeline scope
- [ ] Structured log marker `[ML-EXCLUDED]` emitted on every misconception event
- [ ] ADR-0003 Decision 3 is enforceable via CI
- [ ] Runtime egress enforcement: middleware blocks `[MlExcluded]` events from leaving the system boundary
- [ ] Analytics CSV exports exclude `[MlExcluded]` events
- [ ] DLQ replay (RDY-017) filters out `[MlExcluded]` events from external export
- [ ] Prometheus/metrics do not leak misconception event type names

> **Cross-review (Ran)**: Attribute + CI test is necessary but not sufficient. Misconception data can leak via analytics exports, debug logging to external aggregators, metrics, or DLQ replay. Runtime egress middleware added as acceptance criterion.
