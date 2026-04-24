---
id: FIND-ARCH-022
task_id: t_7f3d9fcf1b56
severity: P1 — High
lens: arch
tags: [reverify, arch, contract, cost]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-022: NatsOutboxPublisher publishes core NATS not JetStream + 5 orphan durable categories

## Summary

NatsOutboxPublisher publishes core NATS not JetStream + 5 orphan durable categories

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

**Goal**: Either wire NatsOutboxPublisher to JetStream for real OR
rename it. The "durable" promise must hold or be removed.

Two coupled problems:
  1. Five of six `cena.durable.{category}.*` namespaces (learner,
     pedagogy, engagement, outreach, system) have ZERO subscribers;
     only `curriculum` is consumed by ExplanationCacheInvalidator.
  2. NatsOutboxPublisher.cs:226 calls `_nats.PublishAsync` (CORE NATS),
     not `js.PublishAsync` (JetStream). The class name and comments
     claim "JetStream durability" but the wire is fire-and-forget.

**Files to read first**:
  - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
  - src/actors/Cena.Actors/Bus/NatsSubjects.cs
  - src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs

**Files to touch**:
  - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
    (use IJetStreamContext / NATS.Client.JetStream)
  - src/actors/Cena.Actors.Host/Program.cs (stream creation at startup)
  - src/actors/Cena.Actors.Tests/Infrastructure/NatsOutboxPublisherJetStreamTests.cs (new)

**Definition of Done**:
  - [ ] Lines 304-319 still produce the same subject names but the
        publish call uses JetStream
  - [ ] Six JetStream streams exist with documented retention policies
  - [ ] Each of the five non-curriculum categories has a real
        consumer OR is removed from GetDurableSubject's routing
  - [ ] Integration test proves messages survive broker bounce
  - [ ] Class header comment matches reality

**Reporting requirements**:
  - Paste the new stream config at startup.
  - Paste the integration test that bounces NATS and proves durability.
  - List which categories are now consumed by which service.

**Reference**: FIND-arch-022 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_7f3d9fcf1b56`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
