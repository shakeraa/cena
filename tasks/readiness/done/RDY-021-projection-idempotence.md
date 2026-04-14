# RDY-021: Projection Idempotence Tests

- **Priority**: Medium — incorrect dashboards and analytics
- **Complexity**: Mid engineer
- **Source**: Expert panel audit — Oren (API), Dina (Architecture)
- **Tier**: 3
- **Effort**: 2-3 days

## Problem

Marten projections use mutable `Apply()` methods (e.g., `stats.TotalAttempts++`). If Marten delivers the same event twice (transient failure retry), projections could double-count. No tests verify duplicate-event safety.

Example: `StudentLifetimeStatsProjection.Apply(ConceptAttempted_V1 e, stats)` increments counters. Called twice = wrong count.

## Scope

### 1. Idempotence tests for all projections

For each projection:
- Deliver event once → assert state
- Deliver same event again → assert state unchanged (or safely re-applied)

### 2. Idempotence guard pattern

If Marten doesn't guarantee exactly-once:
- Add event sequence tracking to projection state
- Skip events with sequence <= last processed sequence
- Document Marten's delivery guarantee

### 3. Projection rebuild test

- Delete projection state, replay events from stream
- Assert final state matches expected

## Files to Create

- New: `tests/Cena.Actors.Tests/Projections/ProjectionIdempotenceTests.cs`
- New: `tests/Cena.Actors.Tests/Projections/ProjectionRebuildTests.cs`

## Acceptance Criteria

- [ ] Every projection has a duplicate-event test
- [ ] No projection double-counts on duplicate delivery
- [ ] Idempotence guard implemented if Marten is at-least-once
- [ ] Projection rebuild from events produces correct state
- [ ] Marten delivery guarantee documented
