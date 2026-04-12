---
id: FIND-ARCH-024
task_id: t_a2aef6aa1112
severity: P1 — High
lens: arch
tags: [reverify, arch, observability]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-024: FeatureFlagActor in-memory only — no persistence, no replica sync, no audit

## Summary

FeatureFlagActor in-memory only — no persistence, no replica sync, no audit

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

**Goal**: Make the FeatureFlagActor production-grade as a kill
switch — persistent, distributed, and audited.

Three failure modes today:
  1. No persistence (in-memory dictionary; restart loses overrides).
  2. No replica distribution (each replica has its own dict).
  3. No audit trail (logger.LogInformation only; no event).

There is also a SECOND parallel feature-flag system in
SystemMonitoringService (PlatformSettingsDocument.Features) that is
not synchronised with FeatureFlagActor — collapse them.

**Files to read first**:
  - src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs
  - src/api/Cena.Admin.Api/SystemMonitoringService.cs
  - src/shared/Cena.Infrastructure/Documents/PlatformSettingsDocument.cs

**Files to touch**:
  - src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs (Marten + event emission)
  - src/shared/Cena.Infrastructure/Documents/FeatureFlagDocument.cs (new)
  - src/actors/Cena.Actors/Events/FeatureFlagEvents.cs (new — FeatureFlagChanged_V1)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register)
  - src/api/Cena.Admin.Api/SystemMonitoringService.cs (delegate to FeatureFlagActor)
  - src/actors/Cena.Actors.Tests/Infrastructure/FeatureFlagActorPersistenceTests.cs (new)

**Definition of Done**:
  - [ ] FeatureFlagActor hydrates from Marten on Started
  - [ ] Every SetFlag persists + emits an event
  - [ ] Two-replica integration test passes
  - [ ] Audit log shows every flag change with actor identity
  - [ ] PlatformSettings.Features delegates to FeatureFlagActor

**Reporting requirements**:
  - Paste the persistence test output.
  - Paste the audit query showing a flag change with the actor identity.

**Reference**: FIND-arch-024 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_a2aef6aa1112`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
