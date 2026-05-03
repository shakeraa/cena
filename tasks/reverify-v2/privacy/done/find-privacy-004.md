---
id: FIND-PRIVACY-004
task_id: t_a852a034cd2a
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, COPPA, GDPR, FERPA, ICO-Children, retention]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-004: Retention policy declared but never enforced — 'retained indefinitely'

## Summary

Retention policy declared but never enforced — 'retained indefinitely'

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

framework: COPPA (§312.10), GDPR (Art 5(1)(e)), ICO-Children (Std 8), FERPA (§99.7)
severity: P0 (critical)
lens: privacy
related_prior_finding: none

## Goal

Implement the data retention worker that the documented retention policy
relies on. Today every retention window is published via
`/api/admin/compliance/data-retention` and enforced... nowhere. The endpoint
itself admits "Currently all data is retained indefinitely in the event store."

## Background

`src/api/Cena.Admin.Api/ComplianceEndpoints.cs:131-172` declares 7y/5y/2y/1y
retention via constants in `DataRetentionPolicy.cs`, returns them via GET, and
ends with:

```csharp
note = "Retention periods are enforced after archival job is deployed. " +
       "Currently all data is retained indefinitely in the event store."
```

`grep -rn 'IHostedService\|BackgroundService' src/ | grep -i 'retention\|archive\|purge'`
returns zero matches.

This violates:
- GDPR Art 5(1)(e) storage limitation
- COPPA §312.10 (retain only as long as reasonably necessary)
- ICO Children's Code Standard 8 (data minimisation)
- FERPA's implied expectation that records have a defined retention

## Files

- `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs` (NEW
  IHostedService — daily cadence)
- `src/shared/Cena.Infrastructure/Compliance/RetentionRunHistory.cs` (NEW —
  per-run audit document)
- `src/api/Cena.Admin.Api.Host/Program.cs` + `src/api/Cena.Student.Api.Host/Program.cs`
  + `src/actors/Cena.Actors.Host/Program.cs` (register the worker — pick ONE
  host to own it; suggest Actor host since it already has the long-running
  hosted-services pattern)
- `src/api/Cena.Admin.Api/ComplianceEndpoints.cs` (replace the lying status
  text with real lastRunAt / nextRunAt / rowsPurgedLastRun / failures)
- `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` (add
  per-document soft-delete vs hard-delete strategy)

## Definition of Done

1. RetentionWorker runs daily (configurable via Cena:Compliance:RetentionCron).
2. On each run it queries every store covered by DataRetentionPolicy and
   either soft-deletes (Marten built-in) or hard-deletes per category.
3. Per-tenant override knob in tenant config (some districts require longer
   retention).
4. Each run emits a structured log event RetentionRunCompleted_V1 (or stores
   a RetentionRunHistory document) so the SIEM can verify the worker actually
   runs.
5. Per-category counts surfaced via the existing
   /api/admin/compliance/data-retention endpoint instead of the
   "indefinitely" lie.
6. Regression test that creates a row, fast-forwards an injected IClock past
   the retention window, runs the worker, and asserts the row is gone.
7. AuditLogRetention bumped to >= StudentRecordRetention before the worker
   runs (closes FIND-privacy-013).
8. Erasure-aware: if a row has an in-flight ErasureRequest, the worker
   accelerates its removal regardless of retention window (cooperates with
   FIND-privacy-005).

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-004-retention-worker`. Result must include:

- the worker registration host
- the cron schedule
- the per-store DELETE strategy table
- regression test paths
- a sample RetentionRunCompleted log line

## Out of scope

- The erasure pipeline itself (FIND-privacy-005)
- Schema-level retention enforcement at the Postgres level (defer)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_a852a034cd2a`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
