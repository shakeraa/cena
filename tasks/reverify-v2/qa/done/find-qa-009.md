---
id: FIND-QA-009
task_id: t_2205e5d2b53f
severity: P1 — High
lens: qa
tags: [reverify, qa, test, build]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-qa-009: Cena.Actors.sln missing 5 of 13 projects (incl. Infrastructure.Tests)

## Summary

Cena.Actors.sln missing 5 of 13 projects (incl. Infrastructure.Tests)

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

Goal: Add the missing test projects to the Cena solution file so IDE users see them and so a future CI self-check can compare disk vs solution.

Background:
`src/actors/Cena.Actors.sln` lists 8 projects. It does NOT include:
  - Cena.Infrastructure.Tests (the FIND-sec-001 SQLi suite — see also FIND-qa-001)
  - Cena.Admin.Api.Tests
  - Cena.Db.Migrator
  - Cena.Emulator
  - Cena.LlmAcl

Combined with FIND-qa-001 (workflow gap), this is how the SQLi regression suite became invisible to both CI and IDE.

Files to read first:
  - src/actors/Cena.Actors.sln
  - find src -name "*.csproj" | sort

Files to touch:
  - src/actors/Cena.Actors.sln

Commands to run:
  cd src/actors
  dotnet sln Cena.Actors.sln add ../shared/Cena.Infrastructure.Tests/Cena.Infrastructure.Tests.csproj
  dotnet sln Cena.Actors.sln add ../api/Cena.Admin.Api.Tests/Cena.Admin.Api.Tests.csproj
  dotnet sln Cena.Actors.sln add ../api/Cena.Db.Migrator/Cena.Db.Migrator.csproj
  dotnet sln Cena.Actors.sln add ../emulator/Cena.Emulator.csproj
  dotnet sln Cena.Actors.sln add ../llm-acl/Cena.LlmAcl/Cena.LlmAcl.csproj

Definition of Done:
  - [ ] `dotnet sln src/actors/Cena.Actors.sln list` shows 13 projects (was 8)
  - [ ] `dotnet build src/actors/Cena.Actors.sln` succeeds
  - [ ] `dotnet test src/actors/Cena.Actors.sln` runs all 1366 tests (972 + 376 + 18)
  - [ ] STRETCH (could be its own task): a CI bash sanity check that compares `find src -name "*.csproj"` against `dotnet sln list` and fails if any csproj is missing from the solution

Reference: FIND-qa-009 in docs/reviews/agent-qa-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_2205e5d2b53f`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
