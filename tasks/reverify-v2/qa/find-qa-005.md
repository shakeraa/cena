---
id: FIND-QA-005
task_id: t_25b60d65bbb2
severity: P1 — High
lens: qa
tags: [reverify, qa, test, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-qa-005: ClassFeedItemProjection determinism not asserted (FIND-data-001)

## Summary

ClassFeedItemProjection determinism not asserted (FIND-data-001)

## Severity

**P1 — High** — REGRESSION

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

Goal: Lock the ClassFeedItemProjection determinism so a regression that re-adds DateTime.UtcNow fails the build.

Background:
FIND-data-001 (ClassFeedItemProjection wall-clock dependency) was fixed in commit abff269 by removing DateTime.UtcNow from the projection. The fix did not add a regression test. The existing ClassFeedItemProjectionTests.cs uses `DateTimeOffset.Parse` for fixtures but does not assert that calling `Project()` twice with the same input produces the same output, nor does it ban DateTime.UtcNow at the projection class level.

Files to read first:
  - src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs (note the comment "// FIND-data-001: Never use DateTime.UtcNow")
  - src/actors/Cena.Actors.Tests/Social/ClassFeedItemProjectionTests.cs (existing tests)

Files to touch:
  - src/actors/Cena.Actors.Tests/Social/ClassFeedItemProjectionTests.cs (extend)

Definition of Done:
  - [ ] New test asserts double-Project on the same event produces a bit-identical ClassFeedItemDocument
  - [ ] New test asserts the projection source file (read via assembly metadata or file string check) contains no `DateTime.UtcNow` or `DateTime.Now`
  - [ ] Test fails on a synthetic regression that re-introduces wall-clock reads
  - [ ] Test passes on cc3f702

Reference: FIND-qa-005 in docs/reviews/agent-qa-reverify-2026-04-11.md
Related prior finding: FIND-data-001


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_25b60d65bbb2`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
