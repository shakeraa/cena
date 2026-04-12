---
id: FIND-QA-001
task_id: t_e1485b61506e
severity: P0 — Critical
lens: qa
tags: [reverify, qa, test, ci, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-qa-001: Cena.Infrastructure.Tests (FIND-sec-001 SQLi suite) not wired into backend.yml

## Summary

Cena.Infrastructure.Tests (FIND-sec-001 SQLi suite) not wired into backend.yml

## Severity

**P0 — Critical** — REGRESSION

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

Goal: Ensure the LeaderboardService SQL injection regression suite (FIND-sec-001) actually runs in CI, and prevent future test projects from being silently excluded.

Background:
The 18-test SQLi regression suite at `src/shared/Cena.Infrastructure.Tests/Gamification/LeaderboardServiceSqliSafetyTests.cs` (added in fix commit 50d23ff/367a9c1) is in the repo but `.github/workflows/backend.yml` never restores, builds, or runs it. The workflow restores test projects by explicit per-csproj path. `Cena.Infrastructure.Tests` was not added to the list. A regression that re-introduces `$@"... {schoolId} ..."` interpolation will pass CI.

Files to read first:
  - .github/workflows/backend.yml
  - src/shared/Cena.Infrastructure.Tests/Cena.Infrastructure.Tests.csproj
  - src/actors/Cena.Actors.sln
  - src/api/Cena.Admin.Api.Tests/Cena.Admin.Api.Tests.csproj (note the cita: "FIND-ux-006b: cover the Student host's AuthEndpoints.PasswordReset handler from the existing admin test project so CI picks it up without needing a new test csproj wired into backend.yml")
  - docs/reviews/agent-qa-reverify-2026-04-11.md (FIND-qa-001 section)

Files to touch:
  - .github/workflows/backend.yml — add restore + build + test steps for `src/shared/Cena.Infrastructure.Tests/`; remove the dead `dotnet restore src/api/Cena.Api.Host/Cena.Api.Host.csproj` line (Cena.Api.Host was deleted by FIND-arch-001 — currently the line silently no-ops on macOS but will fail on linux runners with older dotnet behaviour)
  - src/actors/Cena.Actors.sln — add Cena.Infrastructure.Tests project entry so IDE users see the suite

Definition of Done:
  - [ ] backend.yml runs `dotnet test src/shared/Cena.Infrastructure.Tests/ -c Release --no-build --logger "trx;LogFileName=infrastructure.trx"` on every push to main
  - [ ] backend.yml does NOT reference `src/api/Cena.Api.Host/Cena.Api.Host.csproj`
  - [ ] `src/actors/Cena.Actors.sln` includes Cena.Infrastructure.Tests
  - [ ] `dotnet sln src/actors/Cena.Actors.sln list` shows the new project
  - [ ] A CI run on a feature branch shows 18 additional tests (1366 total) in the test summary
  - [ ] STRETCH: a self-check job in backend.yml that diffs `find src -name "*.Tests.csproj"` against the workflow file's restored project list and fails the build if any *.Tests.csproj is on disk but not in the workflow. This prevents the next test project from quietly missing the gate.

Reporting requirements:
  - Branch: `<worker>/<task-id>-find-qa-001-cena-infra-tests-in-ci`
  - Result string MUST include:
    - The new test count CI reports for backend.yml after the change
    - The file:line of the added restore/build/test commands
    - Confirmation that 50d23ff's test would actually fail in CI on a hypothetical re-introduction of `$@`-interpolated SQL (re-run dotnet test against a synthetic regression branch)

Reference: FIND-qa-001 in docs/reviews/agent-qa-reverify-2026-04-11.md
Related prior finding: FIND-sec-001 (SQL injection in LeaderboardService)


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_e1485b61506e`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
