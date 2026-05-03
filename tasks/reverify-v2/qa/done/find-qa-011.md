---
id: FIND-QA-011
task_id: t_d356ef113d23
severity: P1 — High
lens: qa
tags: [reverify, qa, test, security, auth]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-qa-011: No shared Firebase Auth test double; JWT verify path untested

## Summary

No shared Firebase Auth test double; JWT verify path untested

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

Goal: Centralise Firebase Auth test doubles into a shared fixture and add JWT verification path tests.

Background:
Across all 1366 tests, Firebase Auth is referenced exactly twice — both in a local `FakeFirebaseAdminService` inside `StudentAuthEndpointsTests.cs` (24 lines). There is no shared Firebase Auth mock. The auth path is verified at the policy level only (`AuthPolicyTests.cs`, `ClaimsTransformerTests.cs`, `TenantScopeTests.cs`) — not at the JWT verification path. Any test that wants to exercise the token-verification path either skips it or would call live Google.

Files to read first:
  - src/api/Cena.Admin.Api.Tests/StudentAuthEndpointsTests.cs (the FakeFirebaseAdminService double)
  - src/shared/Cena.Infrastructure/Firebase/FirebaseAdminService.cs (the production class — IFirebaseAdminService surface)
  - src/api/Cena.Admin.Api.Tests/AuthPolicyTests.cs (existing policy-level tests)
  - src/api/Cena.Admin.Api.Tests/ClaimsTransformerTests.cs

Files to touch:
  - src/shared/Cena.Infrastructure.Tests/Firebase/FakeFirebaseAdminService.cs (NEW — promote from local class)
  - src/shared/Cena.Infrastructure.Tests/Firebase/FirebaseTokenVerificationTests.cs (NEW)
  - src/api/Cena.Admin.Api.Tests/StudentAuthEndpointsTests.cs (refactor to use the shared fake)
  - src/api/Cena.Admin.Api.Tests/Cena.Admin.Api.Tests.csproj (add ProjectReference to Cena.Infrastructure.Tests if needed, OR move the fake to a non-test infra project)

NOTE: This depends on FIND-qa-001 being merged first so Cena.Infrastructure.Tests is actually in CI. If qa-001 is not yet merged, place the shared fake inside Cena.Admin.Api.Tests instead and accept the duplication for now.

Definition of Done:
  - [ ] FakeFirebaseAdminService is reachable from both Cena.Admin.Api.Tests and Cena.Actors.Tests (or, fallback: deduped within one project if cross-project reference is awkward)
  - [ ] At least one test asserts a forged JWT is rejected
  - [ ] At least one test asserts a properly-mocked valid JWT is accepted with the expected claims (uid, role, school_id)
  - [ ] No test makes a real network call to googleapis.com / firebase.google.com (verify with a network sandbox or by inspecting test output for any 200/401 from Google)

Reference: FIND-qa-011 in docs/reviews/agent-qa-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_d356ef113d23`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
