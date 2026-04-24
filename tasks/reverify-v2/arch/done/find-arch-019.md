---
id: FIND-ARCH-019
task_id: t_60bf2c15d4cc
severity: P0 — Critical
lens: arch
tags: [reverify, arch, stub]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-019: DLQ retry/bulk-retry endpoints return success without retrying

## Summary

DLQ retry/bulk-retry endpoints return success without retrying

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

**Goal**: Make admin DLQ retry actually retry, not lie.

**Files to read first**:
  - src/api/Cena.Admin.Api/EventStreamService.cs
  - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
  - src/api/Cena.Admin.Api/AdminApiEndpoints.cs (the
    MapEventStreamEndpoints group)
  - src/admin/full-version/src/stores/useDeadLettersStore.ts

**Files to touch**:
  - src/api/Cena.Admin.Api/EventStreamService.cs (real retry logic)
  - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
    (expose `PublishOneAsync(long sequence, ...)`)
  - src/api/Cena.Admin.Api.Tests/EventStreamServiceRetryTests.cs (new)

**Definition of Done**:
  - [ ] `grep -n "Real retry would" src/api/Cena.Admin.Api/EventStreamService.cs` returns zero
  - [ ] RetryMessageAsync deletes the DLQ row only on successful republish
  - [ ] BulkRetryAsync per-id error tracking; partial failures reported
  - [ ] Integration test against a Marten + fake NATS asserts the row
        is gone after a successful retry
  - [ ] Admin UI sees the DLQ depth drop after a successful bulk retry
        (verify in admin live monitor)

**Reporting requirements**:
  - Paste the integration test run output.
  - Paste the new public PublishOneAsync signature.

**Reference**: FIND-arch-019 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_60bf2c15d4cc`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
