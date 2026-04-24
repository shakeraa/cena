---
id: FIND-QA-007
task_id: t_07b82353c70d
severity: P1 — High
lens: qa
tags: [reverify, qa, test, flake]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-qa-007: Wall-clock flakiness in Cena.Actors.Tests — introduce TimeProvider

## Summary

Wall-clock flakiness in Cena.Actors.Tests — introduce TimeProvider

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

Goal: Eliminate wall-clock flakiness in Cena.Actors.Tests by introducing a TimeProvider abstraction in production code and using FakeTimeProvider in tests.

Background:
A 1-in-19 flake was observed in `dotnet test src/actors/Cena.Actors.Tests/` on a bare-metal run with no other change. 36 test files reference `DateTime.UtcNow` / `DateTime.Now` directly. Production code in `src/actors/Cena.Actors/` has zero references to `IClock` / `ISystemClock` / `TimeProvider` — there is no clock abstraction at all, so no test can substitute time deterministically.

This is also the duplicate root cause for FIND-qa-010.

Files to read first:
  - src/actors/Cena.Actors.Tests/ (any of the 36 files using DateTime.UtcNow)
  - src/actors/Cena.Actors/ (audit for current DateTime.UtcNow usage)
  - .NET 8+ TimeProvider docs: https://learn.microsoft.com/dotnet/api/system.timeprovider

Files to touch:
  - src/actors/Cena.Actors/Infrastructure/Clock.cs (NEW — wraps TimeProvider for DI)
  - All services in src/actors/Cena.Actors that read DateTime.UtcNow (audit and migrate)
  - src/actors/Cena.Actors.Tests/ — migrate tests to use Microsoft.Extensions.TimeProvider.Testing's FakeTimeProvider

Definition of Done:
  - [ ] Zero hits for `DateTime.UtcNow` / `DateTime.Now` in `src/actors/Cena.Actors` (production code only — tests are ok)
  - [ ] All 972 Actors tests pass 50/50 runs in a tight CI loop (`for i in $(seq 50); do dotnet test ... || break; done`)
  - [ ] Tests use FakeTimeProvider where they previously read wall clock
  - [ ] CI logs the flake rate over the 50 runs and asserts 0/50

Reporting requirements:
  - Show 50/50 pass log in result string
  - List the migrated services + their callers

Reference: FIND-qa-007 + FIND-qa-010 in docs/reviews/agent-qa-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_07b82353c70d`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
