# TASK-PRR-260: Cohort single-flight write lock on variant generation (R11 split-out)

**Priority**: P1 — gates ADR-0059 cost ceiling at classroom scale
**Effort**: S (2-3 days; Redis lock + integration test)
**Source docs**: ADR-0059 §15.5 + §14.4 R11, RDY-081 single-writer postmortem, claude-code self-audit
**Assignee hint**: kimi-coder (Redis + Marten infra context)
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-n,priority=p1,backend,redis,concurrency
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

A 30-student classroom assigned the same Bagrut question can submit 30 simultaneous variant requests. Without a write lock, all 30 hit Tier-3 LLM in parallel — 30× the cost. Persona-finops flagged this as the #1 cost lever (30× variance per classroom cohort). RDY-081 already established the single-writer pattern in this codebase; reuse it.

## Scope

1. Redis-backed single-flight lock keyed on the variant dedup key from ADR-0059 §15.5 (`{sourceShailonCode, questionIndex, variationKind, track, stream, localeHint, parametricSeed?, payloadHashSafetyV1}`).
2. Lock semantics: 1 writer holds the lock for ≤60s; readers wait + retrieve the cached result. After 60s, lock releases; if writer hasn't completed, next caller becomes the new writer.
3. Telemetry: `variant_singleflight{outcome="writer|reader|timeout"}` counter.
4. Integration test: 30 simulated concurrent requests for the same variant produce 1 write + 29 reads.

## Files

- `src/actors/Cena.Actors/Persistence/VariantSingleFlightLock.cs` (new)
- `src/actors/Cena.Actors.Tests/Persistence/VariantSingleFlightLockTests.cs` (new — concurrent-request integration test)
- `src/api/Cena.Student.Api.Host/Endpoints/ReferenceVariantEndpoints.cs` (PRR-245 file; coordinate)

## Definition of Done

- 30-concurrent test passes (1 write + 29 reads).
- Telemetry emitted.
- Non-test environments verified to actually use Redis (not in-memory fallback).

## Blocking

- PRR-245 endpoints exist (or coordinate handoff).

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + concurrent-test result>"`
