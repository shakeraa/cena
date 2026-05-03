---
id: FIND-QA-002
task_id: t_bdf505599872
severity: P1 — High
lens: qa
tags: [reverify, qa, test, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-qa-002: MeEndpoints CQRS race regression test missing

## Summary

MeEndpoints CQRS race regression test missing

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

Goal: Add a regression test that proves MeEndpoints.UpdateProfile and SubmitOnboarding emit ProfileUpdated_V1 events instead of writing StudentProfileSnapshot directly.

Background:
FIND-data-007b was fixed in commits 9eb04cb / 91ea67f by switching MeEndpoints from `session.Store<StudentProfileSnapshot>()` to emitting `ProfileUpdated_V1` events. The fix added a generic `NotificationEventsRoundTripTests.cs` event-serialization test, but no regression test exercises the projection race condition the fix addresses. A regression that re-introduces a direct snapshot write would not be caught.

Files to read first:
  - src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs (lines around the UpdateProfile and SubmitOnboarding handlers — note line 134 emits ProfileUpdated_V1)
  - src/actors/Cena.Actors/Events/ProfileUpdated_V1.cs (or wherever the event is defined)
  - src/api/Cena.Admin.Api.Tests/StudentAuthEndpointsTests.cs (pattern for hosting Student.Api.Host tests in Admin.Api.Tests)

Files to touch:
  - src/api/Cena.Admin.Api.Tests/MeEndpointsCqrsRaceTests.cs (NEW)

Definition of Done:
  - [ ] Test calls MeEndpoints.UpdateProfile with a known payload
  - [ ] Test asserts only ProfileUpdated_V1 events are appended (use a spy IDocumentOperations or capture the events written)
  - [ ] Test asserts NO direct Store<StudentProfileSnapshot> call occurred
  - [ ] Test passes on cc3f702
  - [ ] Test fails on a synthetic regression that re-adds session.Store<StudentProfileSnapshot> to MeEndpoints.UpdateProfile
  - [ ] Same coverage for SubmitOnboarding
  - [ ] Wired into backend.yml (Cena.Admin.Api.Tests is already in CI — no workflow change needed)

Reference: FIND-qa-002 in docs/reviews/agent-qa-reverify-2026-04-11.md
Related prior finding: FIND-data-007b


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_bdf505599872`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
