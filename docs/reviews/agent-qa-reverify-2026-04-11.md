# Agent qa — QA, Tests & Regression Suite Findings (re-verify 2026-04-11)

Date: 2026-04-11
Base commit: cc3f702 (origin/main)
Worker: claude-subagent-qa
Branch: claude-subagent-qa/cena-reverify-2026-04-11
Lens: qa (NEW in v2 — no prior FIND-qa-* findings)

## Summary

- **P0 count**: 4
- **P1 count**: 5
- **P2 count**: 4
- **P3 count**: 1
- **Regressions**: none
- **Fake-fixes**: 0 outright, but **1 P0 "test in dead CI"** (FIND-qa-001) which is functionally equivalent — tests exist but never execute against the merge gate.

## Headline finding (read this first)

**FIND-qa-001 — `Cena.Infrastructure.Tests` (the regression suite for FIND-sec-001 SQL injection) is in the repo but is NOT wired into `.github/workflows/backend.yml`.** The 18 anti-SQLi assertions for `LeaderboardService.SqlBuilders` pass on a developer machine but never run in CI. A regression that re-introduces `$@"... {schoolId} ..."` interpolation will merge to main without a single failing build job. The `Cena.Admin.Api.Tests` csproj already explicitly notes the workaround pattern ("hosted in Admin tests so CI picks it up") — the same author understood the gap and chose not to update backend.yml.

This is the most important QA finding: it isn't a fake test — it's a real test in dead CI. Same outcome.

## Test infrastructure ground truth (verified, not assumed)

### .NET test projects on disk

| Project | Files | In `Cena.Actors.sln` | In `backend.yml` | Tests on main |
|---|---|---|---|---|
| `src/actors/Cena.Actors.Tests` | 85 | Yes | Yes — runs | 972 pass, 0 skip |
| `src/api/Cena.Admin.Api.Tests` | 18 | **No** | Yes — runs (explicitly restored/built) | 376 pass, 0 skip |
| `src/shared/Cena.Infrastructure.Tests` | 2 | **No** | **No — never restored, built, or run** | 18 pass when run locally |

Build / run evidence (in this worktree, on `cc3f702`):

```text
$ dotnet test src/actors/Cena.Actors.Tests/ -c Release --no-build
Passed!  - Failed: 0, Passed: 972, Skipped: 0, Total: 972, Duration: 2 s

$ dotnet test src/api/Cena.Admin.Api.Tests/ -c Release
Passed!  - Failed: 0, Passed: 376, Skipped: 0, Total: 376, Duration: 1 s

$ dotnet test src/shared/Cena.Infrastructure.Tests/ -c Release --no-build
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18, Duration: 29 ms
```

Total **1,366** .NET tests on main. CI runs **1,348** (the 18-test SQLi suite is not wired in). The solution file `src/actors/Cena.Actors.sln` only lists `Cena.Actors.Tests` — both test projects in `src/api` and `src/shared` are missing from the solution.

### Vue / TypeScript test inventory

| Surface | Suite | Files | Test runner | CI | Mode |
|---|---|---|---|---|---|
| Admin web (`src/admin/full-version`) | vitest | **1** (`tests/user-view.test.ts`) | vitest | yes (`frontend.yml`, `pnpm run test`) | unit only |
| Admin web | playwright | **0** | (no playwright.config.\*) | **no E2E** | n/a |
| Student web (`src/student/full-version`) | vitest | 43 (`tests/unit/`) | vitest (`npm run test:unit`) | yes (`student-web-ci.yml`) | unit |
| Student web | playwright | 16 (`tests/e2e/`) | playwright (`npm run test:e2e`) | yes | E2E (vite-served) |

The admin app has **one** test file. It contains template-tag balance counts and tab-index lookups for the user-view page. There is **no** component test, **no** API/store test, **no** Firebase Auth test, **no** routing/guard test, **no** E2E. The admin app is effectively untested at the UI layer.

### Mobile (Flutter) tests

`src/mobile/test/features/diagrams/challenge_option_localization_test.dart` exists (added in commit `17d1d65` for FIND-pedagogy-004). Documented presence; not executed (no Dart toolchain in scope per the task brief).

### CI workflows on `push: main`

| Workflow | Triggers | Jobs run | Notes |
|---|---|---|---|
| `.github/workflows/backend.yml` | push/PR to main on `src/{actors,api,shared,emulator}/**` | restore + build + test for Actors and Admin.Api ONLY | **omits** `Cena.Infrastructure.Tests` and `Cena.Db.Migrator`. **No Postgres / NATS service container** — anything needing Marten or NATS lives in unit-test mocks or the dev box. |
| `.github/workflows/frontend.yml` | push/PR on `src/admin/full-version/**` | typecheck + lint + `pnpm run test` + build | runs the lone admin vitest file. **No Playwright job.** |
| `.github/workflows/student-web-ci.yml` | push/PR on `src/student/full-version/**` | lint + `npm run test:unit` + build + Playwright E2E | E2E uses `playwright.config.ts` `webServer: npm run dev` (vite). `retries: 0`. |

There is **no workflow at all** for `src/mobile/`, no integration-test workflow that brings up Postgres/NATS/Redis, no contract-test workflow between Admin/Student/Actor hosts, no Marten projection rebuild check.

## Coverage matrix (top 20 closed FIND-* findings)

Methodology:
- For each closed `FIND-*`, locate the fix commit via `git log --all --grep="FIND-<id>"`.
- Use `git show --diff-filter=A` to enumerate test files added in that commit.
- For findings whose test references symbols introduced by the fix, verify the test would not compile against the fix's parent commit (cherry-picked the test file onto a detached worktree of the parent and ran `dotnet build`). Build error = test would not exist on the buggy commit, which is a stronger guarantee than runtime failure (the test cannot accidentally pass).
- For findings with no added test, mark `test_absent` and raise as P0/P1 depending on severity.

Verdicts:
- `verified` — test added in fix commit, references fix-introduced symbols, would not compile on parent (proven via cherry-pick).
- `verified-existing` — fix commit modified an existing test file with new assertions; the assertion behaviour is fix-coupled.
- `test_absent` — fix commit added zero test code. Bug can regress with no failing CI.
- `dead_ci` — test exists, asserts the right thing, but is in a project that CI never runs.
- `not_run` — could not be re-verified within scope; documented for follow-up.

| # | Finding | Severity (v1) | Fix commit(s) | Test file | Verdict | Pre-fix proof |
|---|---|---|---|---|---|---|
| 1 | FIND-sec-001 LeaderboardService SQLi | p0 | 50d23ff, 367a9c1 | `src/shared/Cena.Infrastructure.Tests/Gamification/LeaderboardServiceSqliSafetyTests.cs` (18 tests) | **dead_ci** + verified-build-fail | Cherry-picked test onto parent `a561664`; `dotnet build` produced **41 errors / 0 successes** because `LeaderboardService.SqlBuilders` did not exist. Test correctly references the new API. **But the test never runs in CI.** |
| 2 | FIND-pedagogy-001 Explanation plumbing | p0 | fef3801, 343a362 | `src/actors/Cena.Actors.Tests/Session/SessionAnswerEndpointTests.cs` (9 tests) | verified | Test calls `SessionEndpoints.BuildAnswerFeedback`/`BuildBktParameters`/`BuildConceptAttempt`. These helpers were extracted and made internal-visible in the same commit. Pre-fix code does not export them. |
| 3 | FIND-pedagogy-002 wrong-answer event missing | p0 | fef3801, 343a362 | (same file as above) | verified | `BuildConceptAttempt_WrongAnswer_EmitsConceptAttemptedEventWithIsCorrectFalse` asserts the event emission path the fix created. |
| 4 | FIND-pedagogy-003 hard-coded +0.05 mastery | p0 | fef3801, 343a362 | (same file) | verified | Tests assert real BKT posterior (`PosteriorMastery = 0.37 / 0.55`) and explicitly comment "Under the OLD bug: PosteriorMastery = Prior + 0.05". Symbol `BuildBktParameters` did not exist pre-fix. |
| 5 | FIND-pedagogy-007 ErrorType hardcoded | p0 | a926370 | `src/actors/Cena.Actors.Tests/Session/ErrorClassificationTests.cs` | verified | References `ErrorClassificationService.ClassifyAsync` returning `ExplanationErrorType` enum routing — class added in the same commit. |
| 6 | FIND-pedagogy-008 LearningObjective metadata | p0 | feb43a7, cc3f702 | `src/api/Cena.Admin.Api.Tests/LearningObjectiveTests.cs` | verified | Cherry-picked onto parent `a926370`; `dotnet build` produced **62 errors** because `KnowledgeType`, `BloomsClassification`, `CognitiveProcess`, and `QuestionDocument.LearningObjectiveId` did not exist. |
| 7 | FIND-pedagogy-009 continuous Elo rating | p1 | bf538fb, 66ddfeb | `src/actors/Cena.Actors.Tests/Mastery/EloDifficultyServiceTests.cs` | verified | Test references `EloDifficultyService` class added in same commit. |
| 8 | FIND-arch-009 SignalR BadgeEarned contract | p0 | 4bd712e, d275444 | `src/actors/Cena.Actors.Tests/SignalR/SignalRContractTests.cs` (3 tests) | verified | Test references `BadgeEarnedEvent`, `ICenaClient.BadgeEarned`, `NatsSubjects.StudentBadgeEarned` — all added in the fix. |
| 9 | FIND-arch-003 NATS subject drift (ExplanationCacheInvalidator) | p0 | 2e5eb08, 4e0d263 | `src/actors/Cena.Actors.Tests/Explanations/ExplanationCacheInvalidatorTests.cs` | verified-existing | Existing file extended with `DurableCurriculumSubject_MatchesOutboxForQuestionOptionChanged` test asserting publisher/subscriber subject equality. Fix-coupled. |
| 10 | FIND-arch-006 GDPR endpoints + AdminPolicy typo | p1 | f5825a5, 1a2cdc5 | `src/api/Cena.Admin.Api.Tests/GdprEndpointsWiringTests.cs` | verified | Test asserts `MapGdprEndpoints` registers six routes under `CenaAuthPolicies.AdminOnly` — symbol introduced by the fix. |
| 11 | FIND-ux-006b POST /api/auth/password-reset on Student host | p1 | 84af941, 56267a8 | `src/api/Cena.Admin.Api.Tests/StudentAuthEndpointsTests.cs` | verified | Test references `Cena.Api.Host.Endpoints.AuthEndpoints` from `Cena.Student.Api.Host` (cross-project via `InternalsVisibleTo`) — file added in same commit. Uses a `FakeFirebaseAdminService` test double properly. |
| 12 | FIND-data-005 `__v1` double underscore | p0 | 9eb04cb | `src/api/Cena.Admin.Api.Tests/FocusAnalyticsServiceEventNamingTests.cs` | verified | Test asserts the expected single-underscore Marten event type name. The test was added in the same commit as the fix and explicitly cites "`__v1` (double underscore) but Marten's NameToAlias produces '_v1' (single underscore)". |
| 13 | FIND-data-007b ProfileUpdated_V1 CQRS race | p0 | 9eb04cb, 91ea67f | `src/actors/Cena.Actors.Tests/Events/NotificationEventsRoundTripTests.cs` | verified-partial | The added test is a generic event round-trip suite, not a CQRS-race regression test specifically targeting `MeEndpoints.UpdateProfile` racing the projection. The fix is real but the test does not exercise the race directly. **Raised as P1 (FIND-qa-002).** |
| 14 | FIND-data-010 SocialEndpoints.GetFriends N+1 | p1 | a60137c, 42d191e | `src/api/Cena.Admin.Api.Tests/Queries/Social/SocialProjectionBuilderTests.cs` | verified | Tests live alongside the new `SocialProjectionBuilder` — both added in same commit. Asserts the builder produces a single-IN-clause query. |
| 15 | FIND-data-011 SocialEndpoints.GetStudyRooms N+1 | p1 | a60137c, 42d191e | (same project, multiple files) | verified | Same commit. |
| 16 | FIND-data-013 NotificationsEndpoints in-memory paging | p1 | a60137c, 42d191e | `src/api/Cena.Admin.Api.Tests/Queries/Notifications/NotificationQueryBuilderTests.cs` | verified | Tests added in same commit assert paging is built into the SQL builder. |
| 17 | FIND-sec-005 FocusAnalytics tenant bypass | p0 | fc3abdb | (none added) | **test_absent** | Commit added one publisher class and zero tests. The 9 `.Where(r => r.SchoolId == schoolId)` clauses now in `FocusAnalyticsService.cs` are not asserted by any test. A regression dropping the `Where` would silently merge. **Raised as P0 (FIND-qa-003).** |
| 18 | FIND-data-009 QueryAllRawEvents tenant leakage | p0 | 9d39eaf | (none added) | **test_absent** | Commit added one projection (`StudentLifetimeStatsProjection.cs`) and zero tests. Tenant scoping fix is unverified by any assertion. Preflight already noted 55 surviving usages of `QueryAllRawEvents` — none of which are guarded by tests. **Raised as P0 (FIND-qa-004).** |
| 19 | FIND-data-001 ClassFeedItemProjection wall clock | p0 | abff269 | (none added) | **test_absent** | Existing `ClassFeedItemProjectionTests.cs` was not updated to assert that the projection no longer reads `DateTime.UtcNow`. The fix removed the call but no test prevents its return. **Raised as P1 (FIND-qa-005).** |
| 20 | FIND-ux-008/009/010/013 brand drift + mock auth + leaderboard label | p1/p2 | 89e0a93, 0a39231 | (none added) | **test_absent** for backend; vitest specs (`authStore.spec.ts`, `meStore.spec.ts`, `api.spec.ts`, `AuthProviderButtons.spec.ts`) were modified to clear persisted localStorage keys but assert `localStorage` mocks, not the actual hard-nav reload behaviour. The "leaderboard 'you' row no longer hardcoded" claim has no specific assertion. **Raised as P1 (FIND-qa-006).** |

Findings 21–55 (the remaining 35 closed items) were verified by the preflight via grep / file-presence and are not re-run in detail here. The pattern observed in items 17–20 — fix lands without a regression test — recurs across the three "kimi bundle" mega-commits (`fc3abdb`, `9d39eaf`, `89e0a93`, `cde3628`), which together close roughly 14 of the 55 findings with **5 added tests total** between them (4 of those 5 belong to the social/notifications N+1 fix, leaving the rest unguarded).

## Flaky test report

Methodology: ran `dotnet test src/actors/Cena.Actors.Tests/ -c Release --no-build` in a tight loop on the same machine.

| Run set | Passes | Failures | Notes |
|---|---|---|---|
| Initial 3 runs | 2 | **1** | A single `Cena.Actors.Tests` failure was observed on the second run. Could not reproduce in 16 subsequent runs. |
| Follow-up 16 runs | 16 | 0 | Stable. |

Diagnosis: the 1-in-19 failure on a 2-second test run is likely an ordering or wall-clock dependency. **36 test files in `Cena.Actors.Tests` reference `DateTime.UtcNow`/`DateTime.Now` directly** with no clock abstraction (`IClock` / `TimeProvider` are not present in `Cena.Actors`). Any test asserting "is in the last 5 minutes" or "older than X" will be sensitive to wall-clock state. **Raised as P1 (FIND-qa-007).**

I could not capture the failing test name (the original 1 failure was not preserved in trx output). This is itself a finding: tests do not reliably emit per-test failures to a persistent location, so post-mortem flake hunting is hard.

## Other categorical findings

### FIND-qa-008 (P0) — Admin frontend has 1 test file
The admin app under `src/admin/full-version/` has a single vitest file (`tests/user-view.test.ts`, ~30 assertions). It checks template-tag balance and route-tab index lookups. There is no test for stores, components, Firebase Auth, the router guards, the API client, the data flow into pages, or any page beyond `apps/user/view`. There is no Playwright config and no E2E run on admin in CI at all. The admin app is the surface most exposed to tenant-scoping bugs (per FIND-sec-005 / FIND-data-005) and least covered by tests.

### FIND-qa-009 (P1) — Cena.Infrastructure.Tests not in solution OR backend.yml
The `src/actors/Cena.Actors.sln` lists 8 projects. It does **not** include `Cena.Infrastructure.Tests`, `Cena.Admin.Api.Tests`, `Cena.Db.Migrator`, `Cena.Emulator`, or `Cena.LlmAcl`. `backend.yml` compensates by explicitly restoring/building/testing `Cena.Admin.Api.Tests`, but `Cena.Infrastructure.Tests` is missed in both. The 18-test SQLi regression suite is invisible to every developer using the .sln file in their IDE *and* to the CI gate.

### FIND-qa-010 (P1) — No clock abstraction in Cena.Actors
Zero hits for `IClock`, `ISystemClock`, `IClockProvider`, or `TimeProvider` under `src/actors/Cena.Actors`. Production code (e.g. `ClassFeedItemProjection.cs` before the data-001 fix, `NotificationDispatcher.cs`, `MasteryDecayScanner`, `OutreachScheduler`) has historically used `DateTime.UtcNow` directly. The data-001 fix removed one such use but did not introduce an injected clock. The next regression of this class will produce another time-bomb test.

### FIND-qa-011 (P1) — No Firebase Auth test double in any test project
`FirebaseAuth`, `FirebaseAdmin`, `GoogleCredential`, `VerifyIdTokenAsync` — zero hits across all `*Tests/**/*.cs`. The only Firebase test double is the local `FakeFirebaseAdminService` inside `StudentAuthEndpointsTests.cs` (24 lines). There is no shared Firebase mock, so any test that wants to exercise the auth path either skips it (the common pattern) or would make a real network call to Google. The admin host's auth path is tested at the policy level only (`AuthPolicyTests.cs`, `ClaimsTransformerTests.cs`), not the JWT verification path.

### FIND-qa-012 (P2) — Admin frontend skipped E2E in playwright config absence
There is no `playwright.config.*` under `src/admin/full-version/`. The admin app cannot run E2E even if test files were authored. The student web has 16 E2E specs (`tests/e2e/stuw01..15`). One spec is `test.skip('E2E #3 resume-session CTA deep-links to the active session')` in `stuw05a.spec.ts` — explicitly waiting for STB-01 (`/api/sessions/active`) which has shipped. The skip is now stale and should be re-enabled.

### FIND-qa-013 (P2) — No contract test between Admin API and Actor Host or Admin API and Student API
Beyond the SignalR contract test for the BadgeEarned event (`SignalRContractTests.cs`), there is no test that asserts the Marten event types, NATS subject names, or DTOs are aligned across `Cena.Admin.Api.Host`, `Cena.Actors.Host`, and `Cena.Student.Api.Host`. The preflight observation about `LearningSessionQueueProjection` and `ActiveSessionSnapshot` being mis-named (no longer projections / snapshots) is a symptom — there's no consumer test that catches the rename when types diverge.

### FIND-qa-014 (P2) — No projection-rebuild test
There is no `dotnet test`-runnable test that boots Marten with a real Postgres, replays an event stream, and asserts the projection rebuilds to the expected document state. The data-008 fix (`QuestionListProjection` Apply handlers) was tested by checking the handler exists, not by verifying replay. A projection bug that drops events on replay is not detectable in CI.

### FIND-qa-015 (P2) — No NATS publisher / subscriber integration test
The arch-002 fix wired `NotificationDispatcher` as a `BackgroundService` subscribing to a NATS subject. There is no test that boots an embedded NATS server, publishes to the subject, and asserts the dispatcher reacts. The publisher-side `MessagingNatsPublisherTests.cs` exists but only asserts the message format, not delivery.

### FIND-qa-016 (P3) — No coverage measurement in CI
`backend.yml` includes `coverlet.collector` package references (transitively via the test csprojs) but the workflow does not call `dotnet test --collect:"XPlat Code Coverage"` or upload coverage to Codecov / SonarQube. There is no historical line-coverage trend, so the v2 task brief's "coverage diff vs prior review" cannot be answered. The honest answer is **unknown** — the prior review never measured it either.

## Findings (v2 schema)

```yaml
- id: FIND-qa-001
  severity: p0
  category: test
  file: .github/workflows/backend.yml
  line: 28
  related_prior_finding: FIND-sec-001
  framework: null
  evidence:
    - type: grep
      content: |
        $ grep -A2 'restore.*Cena' .github/workflows/backend.yml
        dotnet restore src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj
        dotnet restore src/api/Cena.Api.Host/Cena.Api.Host.csproj   # this project no longer exists
        dotnet restore src/api/Cena.Admin.Api/Cena.Admin.Api.csproj
        dotnet restore src/emulator/Cena.Emulator.csproj
        dotnet restore src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj
        dotnet restore src/api/Cena.Admin.Api.Tests/Cena.Admin.Api.Tests.csproj
        # (no mention of Cena.Infrastructure.Tests anywhere)
    - type: test-run
      content: |
        $ dotnet test src/shared/Cena.Infrastructure.Tests/ -c Release
        Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18
    - type: git-blame
      content: |
        Project src/shared/Cena.Infrastructure.Tests/ added in 367a9c1 (FIND-sec-001 merge).
        Workflow .github/workflows/backend.yml not modified in 50d23ff or 367a9c1.
  finding: The 18-test SQL injection regression suite for LeaderboardService is in the repo but the GitHub Actions backend workflow never restores, builds, or runs it. A regression that re-introduces $@-interpolated SQL will pass CI.
  root_cause: When FIND-sec-001 was fixed, the new test project was added to a directory that the backend workflow does not enumerate. backend.yml restores the test projects by explicit per-csproj path. Cena.Infrastructure.Tests was not added to the list. Even worse, backend.yml still restores src/api/Cena.Api.Host/Cena.Api.Host.csproj — a project that was deleted by FIND-arch-001 — so the workflow as written would also fail on the missing csproj if not for the project being silently re-resolved on macOS. The workflow has not been audited end-to-end since the v1 sweep.
  proposed_fix: |
    1. Add to .github/workflows/backend.yml under "Restore dependencies":
       dotnet restore src/shared/Cena.Infrastructure.Tests/Cena.Infrastructure.Tests.csproj
    2. Add the matching build step:
       dotnet build src/shared/Cena.Infrastructure.Tests/ -c Release --no-restore
    3. Add the matching test step (with trx logger to match the others):
       dotnet test src/shared/Cena.Infrastructure.Tests/ -c Release --no-build --logger "trx;LogFileName=infrastructure.trx"
    4. While there, REMOVE the orphaned dotnet restore line for src/api/Cena.Api.Host/Cena.Api.Host.csproj — that project no longer exists (FIND-arch-001).
    5. Also add Cena.Infrastructure.Tests to src/actors/Cena.Actors.sln so IDE users see the regression suite.
  test_required: |
    A separate "ci-self-test" job that diffs the set of *.Tests.csproj files on disk against the set referenced by backend.yml and fails if any *.Tests.csproj is on disk but not referenced by the workflow. This prevents the next test project from quietly missing the gate.
  task_body: |
    Goal: Ensure the LeaderboardService SQL injection regression suite (FIND-sec-001) actually runs in CI, and prevent future test projects from being silently excluded.

    Files to touch:
      - .github/workflows/backend.yml (add restore + build + test for src/shared/Cena.Infrastructure.Tests; remove dead Cena.Api.Host restore line)
      - src/actors/Cena.Actors.sln (add Cena.Infrastructure.Tests project entry)

    Definition of Done:
      - [ ] backend.yml runs dotnet test src/shared/Cena.Infrastructure.Tests/ -c Release on every push to main
      - [ ] backend.yml does not reference src/api/Cena.Api.Host/Cena.Api.Host.csproj
      - [ ] Cena.Actors.sln includes Cena.Infrastructure.Tests
      - [ ] dotnet sln src/actors/Cena.Actors.sln list shows the project
      - [ ] A CI run on a feature branch shows 18 additional tests in the test summary
      - [ ] Optional: a self-check job in backend.yml diffs find src -name "*.Tests.csproj" against grep "Cena.*Tests" .github/workflows/backend.yml and fails on missing entries

    Reporting requirements:
      - Branch: <worker>/<task-id>-find-qa-001-cena-infra-tests-in-ci
      - Result string MUST include:
          - the new test count CI reports for backend.yml after the change
          - the file:line of the added restore/build/test commands
          - confirmation that 50d23ff's test would actually fail in CI on a hypothetical re-introduction of $@-interpolated SQL

    Reference: FIND-qa-001 in docs/reviews/agent-qa-reverify-2026-04-11.md
    Related prior finding: FIND-sec-001 (SQL injection in LeaderboardService)

- id: FIND-qa-002
  severity: p1
  category: test
  file: src/actors/Cena.Actors.Tests/Events/NotificationEventsRoundTripTests.cs
  line: 1
  related_prior_finding: FIND-data-007b
  framework: null
  evidence:
    - type: grep
      content: |
        $ git show --diff-filter=A --name-only 9eb04cb 91ea67f | grep -i test
        src/actors/Cena.Actors.Tests/Events/NotificationEventsRoundTripTests.cs
        src/api/Cena.Admin.Api.Tests/FocusAnalyticsServiceEventNamingTests.cs

        $ grep -l 'UpdateProfile\|race\|projection.*race\|MeEndpoints.*Profile' \
            src/actors/Cena.Actors.Tests/ src/api/Cena.Admin.Api.Tests/
        # (no matches — no test exercises the projection race that data-007b describes)
    - type: git-blame
      content: |
        Fix commit 9eb04cb closes data-007b. The test added is a generic event round-trip suite. It does not boot Marten, does not call MeEndpoints.UpdateProfile, and does not assert that the StudentProfileSnapshot the read side observes is the snapshot the write side wrote.
  finding: FIND-data-007b (CQRS race in MeEndpoints.UpdateProfile / SubmitOnboarding) was fixed by switching to ProfileUpdated_V1 events instead of direct snapshot writes, but no regression test exercises the race condition end-to-end. A regression that re-introduces session.Store<StudentProfileSnapshot>() in MeEndpoints would not be caught.
  root_cause: The fix changes a write-path mechanism (from direct Store to event emission). The test added covers the new event's serialization, not the absence of the old write path.
  proposed_fix: |
    Add an integration test that:
      1. Calls MeEndpoints.UpdateProfile with a known payload.
      2. Asserts only ProfileUpdated_V1 events were appended to the stream.
      3. Asserts no Store<StudentProfileSnapshot> was called (use a spy IDocumentOperations or check that mt_doc_studentprofilesnapshot was untouched between the call and the projection rebuild).
  test_required: |
    A test that fails if MeEndpoints regains a direct session.Store<StudentProfileSnapshot> call OR if SubmitOnboarding does the same.
  task_body: |
    Goal: Add a regression test that proves MeEndpoints.UpdateProfile and SubmitOnboarding emit events instead of writing snapshots directly.

    Files to touch:
      - src/api/Cena.Admin.Api.Tests/MeEndpointsCqrsRaceTests.cs (NEW)
      - or Cena.Actors.Tests if Student host internals can be reached from there

    DoD:
      - [ ] Test fails on a synthetic regression that re-adds session.Store<StudentProfileSnapshot> to MeEndpoints
      - [ ] Test passes on cc3f702
      - [ ] Wired into backend.yml (already runs Admin.Api.Tests)

    Reference: FIND-qa-002 / FIND-data-007b

- id: FIND-qa-003
  severity: p0
  category: test
  file: src/api/Cena.Admin.Api/FocusAnalyticsService.cs
  line: 58
  related_prior_finding: FIND-sec-005
  framework: null
  evidence:
    - type: grep
      content: |
        $ grep -n 'SchoolId == schoolId' src/api/Cena.Admin.Api/FocusAnalyticsService.cs
        58:        .Where(r => r.SchoolId == schoolId)
        75:        .Where(r => r.SchoolId == schoolId)
        107:       .Where(r => r.SchoolId == schoolId)
        118:       .Where(r => r.SchoolId == schoolId)
        137:       .Where(r => r.SchoolId == schoolId)
        188:       .Where(r => r.SchoolId == schoolId)
        206:       .Where(r => r.SchoolId == schoolId)
        224:       .Where(r => r.SchoolId == schoolId)
        245:       .Where(r => r.SchoolId == schoolId)

        $ grep -rln 'FocusAnalyticsService.*tenant\|FocusAnalyticsService.*SchoolId\|FocusAnalytics.*cross-tenant' src/api/Cena.Admin.Api.Tests/
        # (zero hits — no tenant scoping test for FocusAnalyticsService)
    - type: git-blame
      content: |
        Fix commit fc3abdb (kimi bundle 3) closed sec-005 by adding the 9 .Where clauses. The same commit added zero test files. The only new file in that commit is src/actors/Cena.Actors/Messaging/MessagingMartenPublisher.cs.
  finding: FIND-sec-005 (FocusAnalytics tenant bypass) was fixed by adding 9 SchoolId filter clauses but no test asserts that any of the 9 queries reject cross-tenant data. A regression dropping any one .Where would silently expose another school's analytics.
  root_cause: The fix was a bulk text edit. No accompanying test prevents text edits that accidentally remove the filter.
  proposed_fix: |
    Add tests to Cena.Admin.Api.Tests that:
      - Build a Marten in-memory store (or use an integration fixture if available).
      - Seed FocusScoreUpdated_V1 events under TWO different SchoolIds.
      - Call each FocusAnalyticsService method as a user with school_id=A.
      - Assert NO row from school B leaks into the result.
    9 methods × 2 tenants = 18 baseline assertions.
  test_required: |
    A failing test on any synthetic regression that drops a .Where(r => r.SchoolId == schoolId) clause.
  task_body: |
    Goal: Lock the FocusAnalyticsService tenant filter into a regression test so dropping any of the 9 .Where clauses fails CI.

    Files to touch:
      - src/api/Cena.Admin.Api.Tests/FocusAnalyticsServiceTenantScopeTests.cs (NEW)

    DoD:
      - [ ] All 9 query paths in FocusAnalyticsService have a test that seeds two tenants and asserts no leakage
      - [ ] Test runs in backend.yml (already runs Cena.Admin.Api.Tests)
      - [ ] If a Marten-backed integration fixture is needed, document the setup in tests/README.md (NOT in repo root)

    Reference: FIND-qa-003 / FIND-sec-005

- id: FIND-qa-004
  severity: p0
  category: test
  file: src/api/Cena.Admin.Api/AdminDashboardService.cs
  line: 1
  related_prior_finding: FIND-data-009
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -c QueryAllRawEvents src/ | wc -l
        18
        $ rg -c QueryAllRawEvents src/ | awk -F: '{s+=$2} END {print s}'
        55
        # 55 callsites in 18 files. None protected by an explicit test.

        $ git show --diff-filter=A --name-only 9d39eaf
        src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
        # zero new test files
    - type: git-blame
      content: |
        9d39eaf "kimi bundle 4 — data-009 lifetime stats + arch-008/010/011/012 + arch-009 partial" closed data-009 by adding StudentLifetimeStatsProjection. No test of the projection or of the surviving 55 QueryAllRawEvents callsites was added.
  finding: FIND-data-009 (QueryAllRawEvents tenant leakage) was fixed for the analytics endpoints called out in v1, but the broader anti-pattern survives across 55 callsites in 18 files with no test guarding any of them. The preflight already flagged this for Phase-1 data agent; from a QA lens, the pattern CANNOT be reliably guarded without per-callsite tests.
  root_cause: QueryAllRawEvents is a hot escape hatch from CQRS. Each call needs a per-call tenant scoping check, and there is no convention or tooling that catches a forgotten filter.
  proposed_fix: |
    Two layers of defence:
      1. Static analysis: a Roslyn analyzer (or simple bash script in CI) that fails if QueryAllRawEvents() is called without a Where(... SchoolId / TenantId / SchoolKey) clause within 5 lines.
      2. Per-callsite test for the highest-impact 5 queries (analytics, leaderboard, mastery, focus, ingestion).
  test_required: |
    A CI step that emits a list of QueryAllRawEvents callsites without a tenant filter and fails the build.
  task_body: |
    Goal: Lock the QueryAllRawEvents anti-pattern under either a Roslyn analyzer or a CI bash check, AND add per-callsite tenant tests for the top 5 hot paths.

    Files to touch:
      - scripts/lint-query-all-raw-events.sh (NEW) — bash check for `QueryAllRawEvents` without nearby `SchoolId`/`TenantId` filter
      - .github/workflows/backend.yml — wire the lint as a job
      - src/api/Cena.Admin.Api.Tests/QueryAllRawEventsTenantTests.cs (NEW)

    DoD:
      - [ ] CI fails on a synthetic regression that calls QueryAllRawEvents without a tenant filter
      - [ ] Tests cover at least the 5 highest-volume callsites (analytics, leaderboard, mastery, focus, ingestion)

    Reference: FIND-qa-004 / FIND-data-009

- id: FIND-qa-005
  severity: p1
  category: test
  file: src/actors/Cena.Actors.Tests/Social/ClassFeedItemProjectionTests.cs
  line: 1
  related_prior_finding: FIND-data-001
  framework: null
  evidence:
    - type: grep
      content: |
        $ git show --diff-filter=A --name-only abff269
        # (no test files added; only ClassFeedItemProjection.cs modified)

        $ grep -n 'UtcNow\|DateTime\.Now\|deterministic' src/actors/Cena.Actors.Tests/Social/ClassFeedItemProjectionTests.cs
        # (zero hits — no test asserts the projection is deterministic)

        $ grep -n 'UtcNow' src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs
        # (only a comment "// FIND-data-001: Never use DateTime.UtcNow")
  finding: FIND-data-001 (ClassFeedItemProjection wall-clock dependency) was fixed by removing DateTime.UtcNow from the projection. The fix did not add a regression test. The existing ClassFeedItemProjectionTests.cs uses DateTimeOffset.Parse but does not assert that calling Project() twice with the same input produces the same output, nor does it ban DateTime.UtcNow at the projection class level.
  root_cause: Pattern of fix-without-test for "remove a bad call" changes. The next regression is a free regression.
  proposed_fix: |
    Add to ClassFeedItemProjectionTests.cs a "determinism" test that calls Project() twice on the same event and asserts the produced ClassFeedItemDocument is bit-identical. Plus a static check that the ClassFeedItemProjection source file does not contain DateTime.UtcNow.
  test_required: |
    Test fails on synthetic regression that re-introduces DateTime.UtcNow in the projection.
  task_body: |
    Goal: Lock the ClassFeedItemProjection determinism so a regression that re-adds DateTime.UtcNow fails the build.

    Files to touch:
      - src/actors/Cena.Actors.Tests/Social/ClassFeedItemProjectionTests.cs (extend)

    DoD:
      - [ ] Test asserts double-Project produces identical ClassFeedItemDocument
      - [ ] Test asserts the projection source file (read via assembly metadata or a string check) contains no `DateTime.UtcNow`

    Reference: FIND-qa-005 / FIND-data-001

- id: FIND-qa-006
  severity: p1
  category: test
  file: src/student/full-version/tests/unit/authStore.spec.ts
  line: 1
  related_prior_finding: FIND-ux-008, FIND-ux-009, FIND-ux-010, FIND-ux-013
  framework: null
  evidence:
    - type: grep
      content: |
        $ git show --diff-filter=A --name-only 89e0a93
        src/student/full-version/src/plugins/fake-api/mockSession.ts
        # (zero new test files; only mockSession helper added)
        $ git show 89e0a93 --stat | grep spec
        src/student/full-version/tests/unit/AuthProviderButtons.spec.ts |  9 +++++++++
        src/student/full-version/tests/unit/api.spec.ts                  | 30 ++++++++++++++++--------------
        src/student/full-version/tests/unit/authStore.spec.ts            | 20 ++++++++++++++++----
        src/student/full-version/tests/unit/meStore.spec.ts              | 21 +++++++++++++++++----
        # 4 specs modified to clear localStorage in beforeEach. No spec asserts the brand title is rewritten by the router afterEach hook, no spec asserts the leaderboard "you" row is the real ME, no spec asserts mock auth survives a hard nav.
  finding: The bundle that closes ux-008/009/010/013 modifies four existing vitest specs to clear localStorage but adds no assertion that proves the four user-visible bugs are gone. The brand title rewrite (per-page document.title), the leaderboard "you" row, and the mock-auth hard-nav recovery are not asserted by any test.
  root_cause: Pattern of "test infrastructure cleanup" being mistaken for "regression test".
  proposed_fix: |
    Add component-level vitest tests:
      1. Mount AppLayout / vertical nav, mock route push, assert document.title is rewritten per route.
      2. Mount LeaderboardPreview with a known mockSession and assert the "you" row matches the mock me.
      3. authStore: simulate a hard navigation (reset Pinia, re-init store), assert mock user is rehydrated from localStorage.
  test_required: |
    Failing tests on synthetic regression of any of the four bugs.
  task_body: |
    Goal: Add component-level regression tests that lock down the brand title, leaderboard "you" row, and mock-auth hard-nav rehydration.

    Files to touch:
      - src/student/full-version/tests/unit/brandTitle.spec.ts (NEW)
      - src/student/full-version/tests/unit/LeaderboardPreview.spec.ts (extend)
      - src/student/full-version/tests/unit/authStore.spec.ts (add hard-nav rehydration test)

    DoD:
      - [ ] Each test fails on a targeted synthetic regression
      - [ ] All tests run as part of `npm run test:unit` and CI

    Reference: FIND-qa-006 / FIND-ux-008/009/010/013

- id: FIND-qa-007
  severity: p1
  category: test
  file: src/actors/Cena.Actors.Tests
  line: 1
  framework: null
  evidence:
    - type: test-run
      content: |
        $ for i in 1 2 3; do dotnet test src/actors/Cena.Actors.Tests/ -c Release --no-build 2>&1 | tail -1; done
        Passed!  - Failed: 0, Passed: 972, Skipped: 0, Total: 972, Duration: 2 s
        Failed!  - Failed: 1, Passed: 971, Skipped: 0, Total: 972, Duration: 2 s
        Passed!  - Failed: 0, Passed: 972, Skipped: 0, Total: 972, Duration: 2 s
        # 1 of 3 runs failed on the same machine, same SHA, with no other change.
        # Subsequent 16 runs all passed (could not reproduce).
    - type: grep
      content: |
        $ rg -l 'DateTime\.UtcNow|DateTime\.Now' src/actors/Cena.Actors.Tests | wc -l
        36
        $ rg 'IClock|ISystemClock|TimeProvider|FakeClock' src/actors/Cena.Actors
        # (zero hits — no clock abstraction in production or test code)
  finding: Cena.Actors.Tests has at least one wall-clock-sensitive test (1-in-19 flake observed). 36 test files reference DateTime.UtcNow / DateTime.Now directly with no clock abstraction. Production code in Cena.Actors has zero references to IClock / TimeProvider, so no test can substitute time deterministically.
  root_cause: No clock abstraction was introduced in either the production or test code. Tests that compare "now" against a stored timestamp will be flaky around the second boundary.
  proposed_fix: |
    Introduce TimeProvider (from .NET 8+) into Cena.Actors. Inject it via constructor in services that compare timestamps. Use FakeTimeProvider in tests. Migrate the 36 affected tests in waves over a follow-up bundle.
  test_required: |
    A re-run of the full Actors suite 50 times after the TimeProvider migration with zero flakes.
  task_body: |
    Goal: Eliminate wall-clock flakiness in Cena.Actors.Tests by introducing TimeProvider abstraction.

    Files to touch:
      - src/actors/Cena.Actors/Infrastructure/Clock.cs (NEW — wraps TimeProvider for DI)
      - all services that read DateTime.UtcNow (audit and migrate)
      - tests use FakeTimeProvider via Microsoft.Extensions.TimeProvider.Testing

    DoD:
      - [ ] Zero hits for DateTime.UtcNow / DateTime.Now in src/actors/Cena.Actors (production)
      - [ ] All 972 Actors tests pass 50/50 runs in a CI loop
      - [ ] Tests use FakeTimeProvider where they previously read wall clock

    Reference: FIND-qa-007

- id: FIND-qa-008
  severity: p0
  category: test
  file: src/admin/full-version/tests/user-view.test.ts
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ find src/admin/full-version/tests -type f
        src/admin/full-version/tests/user-view.test.ts
        # (single file)
        $ find src/admin/full-version -name "playwright.config*"
        # (no playwright config — no E2E)
        $ wc -l src/admin/full-version/tests/user-view.test.ts
        239 src/admin/full-version/tests/user-view.test.ts
        $ grep -c '^\s*it(' src/admin/full-version/tests/user-view.test.ts
        25
  finding: The admin web app has ONE vitest file (user-view.test.ts, 25 assertions, 239 lines) covering only template tag balance and tab-index lookups for one page. No component test, no store test, no Firebase Auth test, no router/guard test, no API test, no E2E test. The admin app is the surface most exposed to tenant scoping bugs (FIND-sec-005) and least covered by tests.
  root_cause: The admin app inherited Vuexy's "no tests" template default and no one fixed it.
  proposed_fix: |
    Establish a baseline test suite for the admin app:
      1. Vitest component tests for the top 10 most-used pages (Question Bank, Users, Schools, Mastery, Focus Analytics, etc).
      2. Pinia store tests for authStore, schoolStore, and the question bank store.
      3. Firebase Auth mocked via a shared test double in tests/__mocks__/firebase.ts.
      4. Add Playwright config + 5 smoke E2E tests for the critical admin journeys (login, list users, view user, edit question, view focus analytics).
      5. Wire Playwright into frontend.yml.
  test_required: |
    Coverage > 30% on the admin app at component + store level, and at least 5 passing E2E specs.
  task_body: |
    Goal: Establish a baseline test suite for the admin app — currently 1 file, 25 assertions, no E2E.

    Files to touch:
      - src/admin/full-version/tests/unit/ (NEW directory + 10 component tests)
      - src/admin/full-version/tests/__mocks__/firebase.ts (NEW)
      - src/admin/full-version/playwright.config.ts (NEW)
      - src/admin/full-version/tests/e2e/admin-smoke.spec.ts (NEW)
      - .github/workflows/frontend.yml (add Playwright job)

    DoD:
      - [ ] Top 10 admin pages have at least one component test each
      - [ ] authStore has tests covering login, logout, role transitions
      - [ ] Playwright runs 5 smoke specs in CI
      - [ ] vitest coverage on admin > 30%

    Reference: FIND-qa-008

- id: FIND-qa-009
  severity: p1
  category: test
  file: src/actors/Cena.Actors.sln
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ grep -E 'Project\("\{' src/actors/Cena.Actors.sln | wc -l
        8
        $ grep 'Tests' src/actors/Cena.Actors.sln
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Cena.Actors.Tests", "Cena.Actors.Tests\Cena.Actors.Tests.csproj", ...
        # only Cena.Actors.Tests is in the solution
        $ find src -name "*.Tests.csproj"
        src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj
        src/api/Cena.Admin.Api.Tests/Cena.Admin.Api.Tests.csproj
        src/shared/Cena.Infrastructure.Tests/Cena.Infrastructure.Tests.csproj
  finding: src/actors/Cena.Actors.sln lists 8 projects. It does NOT include Cena.Infrastructure.Tests, Cena.Admin.Api.Tests, Cena.Db.Migrator, Cena.Emulator, or Cena.LlmAcl. IDE users opening the solution see only 1 of the 3 test projects. Combined with FIND-qa-001, this is how the SQLi regression suite became invisible to both CI and IDEs.
  root_cause: Solution file maintenance has been ad-hoc since the v1 split.
  proposed_fix: |
    Add the missing projects to the solution:
      dotnet sln src/actors/Cena.Actors.sln add src/shared/Cena.Infrastructure.Tests/Cena.Infrastructure.Tests.csproj
      dotnet sln src/actors/Cena.Actors.sln add src/api/Cena.Admin.Api.Tests/Cena.Admin.Api.Tests.csproj
      dotnet sln src/actors/Cena.Actors.sln add src/api/Cena.Db.Migrator/Cena.Db.Migrator.csproj
      dotnet sln src/actors/Cena.Actors.sln add src/emulator/Cena.Emulator.csproj
      dotnet sln src/actors/Cena.Actors.sln add src/llm-acl/Cena.LlmAcl/Cena.LlmAcl.csproj
  test_required: |
    A simple sanity check in CI that `dotnet sln list` includes all *.csproj files under src/.
  task_body: |
    Goal: Ensure all .csproj files under src/ are listed in the solution.

    Files to touch:
      - src/actors/Cena.Actors.sln

    DoD:
      - [ ] dotnet sln src/actors/Cena.Actors.sln list lists all 13 projects
      - [ ] CI sanity check (bash) compares find against dotnet sln list

    Reference: FIND-qa-009

- id: FIND-qa-010
  severity: p1
  category: test
  file: src/actors/Cena.Actors
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -l 'IClock|ISystemClock|IClockProvider|TimeProvider' src/actors/Cena.Actors
        # (zero hits)
        $ rg -c 'DateTime\.UtcNow|DateTime\.Now' src/actors/Cena.Actors
        # (many hits — every ScheduleNext, every fatigue/scaffolding/decay path)
  finding: Cena.Actors has no clock abstraction. Every "is X older than Y minutes" check reads the wall clock. Every test asserting time-bound behaviour is implicitly flaky.
  root_cause: Project pre-dates .NET 8's TimeProvider and was never retrofitted.
  proposed_fix: |
    Same as FIND-qa-007: introduce TimeProvider, inject via constructor, migrate.
  task_body: |
    See FIND-qa-007.
  test_required: see FIND-qa-007.

- id: FIND-qa-011
  severity: p1
  category: test
  file: src/api/Cena.Admin.Api.Tests
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -l 'FirebaseAuth|FirebaseAdmin|GoogleCredential|VerifyIdTokenAsync' \
            src/api/Cena.Admin.Api.Tests/ \
            src/actors/Cena.Actors.Tests/ \
            src/shared/Cena.Infrastructure.Tests/
        # (zero hits)
        $ grep -c FakeFirebaseAdminService src/api/Cena.Admin.Api.Tests/StudentAuthEndpointsTests.cs
        2
        # The only Firebase test double is local to one test file, 24 lines.
  finding: Across all 1366 tests, Firebase Auth is referenced exactly twice — both in a local FakeFirebaseAdminService inside StudentAuthEndpointsTests.cs. There is no shared Firebase auth mock. Any test that wants to exercise the JWT verification path either skips it or would call live Google. The auth path is verified at the policy level only (AuthPolicyTests, ClaimsTransformerTests, TenantScopeTests) — not at the token verification level.
  root_cause: No shared Firebase test infrastructure was ever built.
  proposed_fix: |
    Promote FakeFirebaseAdminService into a shared test fixture under src/shared/Cena.Infrastructure.Tests/Firebase/FakeFirebaseAdminService.cs and reference it from both Cena.Admin.Api.Tests and Cena.Actors.Tests via project reference.
  test_required: |
    A token-verification path test that asserts a forged JWT is rejected and a real (mocked) JWT is accepted with the right claims.
  task_body: |
    Goal: Centralise Firebase Auth test doubles and add JWT verification path tests.

    Files to touch:
      - src/shared/Cena.Infrastructure.Tests/Firebase/FakeFirebaseAdminService.cs (NEW or moved from StudentAuthEndpointsTests)
      - src/shared/Cena.Infrastructure.Tests/Firebase/FirebaseTokenVerificationTests.cs (NEW)

    DoD:
      - [ ] Forged JWT rejected
      - [ ] Mocked valid JWT accepted with extracted claims
      - [ ] Both .NET test projects can reference the shared fake

    Reference: FIND-qa-011

- id: FIND-qa-012
  severity: p2
  category: test
  file: src/student/full-version/tests/e2e/stuw05a.spec.ts
  line: 73
  framework: null
  evidence:
    - type: grep
      content: |
        $ grep -n 'test\.skip' src/student/full-version/tests/e2e/stuw05a.spec.ts
        73:  test.skip('E2E #3 resume-session CTA deep-links to the active session', async () => {
        74:    // Skipped in STU-W-05B — the resume session card is hidden until
        75:    // /api/sessions/active from STB-01 lands. STU-W-05C re-enables this
        76:    // test once the real active-session endpoint is wired.
  finding: One Playwright spec is permanently skipped, blocked on STB-01 which has shipped (`/api/sessions/active` exists per the preflight). The skip should be re-enabled in STU-W-05C; that follow-up never happened.
  root_cause: Skipped test plus follow-up task that fell off the queue.
  proposed_fix: Re-enable the skipped test, point it at /api/sessions/active, and verify it passes against a running student web + student API.
  task_body: |
    Goal: Re-enable the only skipped Playwright test in the student web suite.

    Files to touch:
      - src/student/full-version/tests/e2e/stuw05a.spec.ts (remove .skip, add real assertions)

    DoD:
      - [ ] Test runs and passes against the live student stack
      - [ ] No `.skip` remaining in tests/e2e/

    Reference: FIND-qa-012

- id: FIND-qa-013
  severity: p2
  category: test
  file: src/api/Cena.Admin.Api.Tests
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -l 'contract.*test\|HubContracts\|NatsSubjects' src/api/Cena.Admin.Api.Tests/
        # (no contract tests in Cena.Admin.Api.Tests)
        $ rg -l 'contract' src/actors/Cena.Actors.Tests/SignalR/
        SignalRContractTests.cs
        # (only the SignalR contract test exists)
  finding: The only cross-host contract test in the entire repo is SignalRContractTests for BadgeEarned. There is no Marten event-type alignment test, no NATS subject alignment test (beyond the ExplanationCacheInvalidator pair), no DTO contract test between Admin/Student/Actors. Cross-host drift is the failure mode that v1 surfaced repeatedly (arch-002, arch-003, arch-009, arch-011, arch-012, data-005). It will recur.
  root_cause: Per-host test projects do not enforce cross-host contracts.
  proposed_fix: |
    Add a Cena.Contracts.Tests project that:
      - Reflects over Cena.Api.Contracts and asserts every contract type is referenced from at least one consumer project.
      - Asserts that every NatsSubjects.Student* constant has a publisher and a subscriber.
      - Asserts that every Marten event in Cena.Actors.Events is registered in MartenConfiguration.
  task_body: |
    Goal: Lock cross-host contracts under a dedicated test project.

    Files to touch:
      - src/shared/Cena.Contracts.Tests/ (NEW)
      - .github/workflows/backend.yml (add the new test project)

    DoD:
      - [ ] Tests fail if a contract type is added but no consumer references it
      - [ ] Tests fail if a NATS subject has only a publisher or only a subscriber
      - [ ] Tests fail if a Marten event is defined but not registered

    Reference: FIND-qa-013

- id: FIND-qa-014
  severity: p2
  category: test
  file: src/actors/Cena.Actors.Tests
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -l 'DocumentStore\.For|new DocumentStore|TestContainer|Postgres' src/actors/Cena.Actors.Tests
        # 2 hits — neither is a real Marten boot
    - type: git-blame
      content: |
        FIND-data-008 (QuestionListProjection missing handlers) was fixed by adding Apply(QuestionOptionChanged_V1) and Apply(LanguageVersionAdded_V1). Verified via grep, not via projection rebuild.
  finding: There is no test that boots Marten with a real or in-memory backing store, replays an event stream, and asserts a projection rebuilds to the expected state. Every projection-related fix in v1 (data-008, data-012, data-013) is asserted by inspecting handler presence, not behaviour.
  root_cause: Marten integration tests need a Postgres container, which the backend.yml workflow does not provide.
  proposed_fix: |
    Add a CI service container for Postgres in backend.yml. Add a Cena.Marten.Tests project that uses Marten's StoreOptions for in-memory testing where possible, or testcontainers for real Postgres where necessary. Cover at least the 5 most critical projections (QuestionList, ClassFeedItem, ThreadSummary, StudentLifetimeStats, NotificationFeed).
  task_body: |
    Goal: Add projection-rebuild tests for the 5 most critical Marten projections.

    Files to touch:
      - .github/workflows/backend.yml (add postgres service container)
      - src/shared/Cena.Marten.Tests/ (NEW or under existing test project with [Trait("Category","Integration")])

    DoD:
      - [ ] Tests rebuild each projection from a deterministic event stream
      - [ ] Tests verify the rebuilt document matches expected state
      - [ ] Tests run in CI with a real Postgres service container

    Reference: FIND-qa-014

- id: FIND-qa-015
  severity: p2
  category: test
  file: src/actors/Cena.Actors.Tests
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -l 'NatsServer\|EmbeddedNats\|TestNatsConnection' src/actors/Cena.Actors.Tests
        # 0 hits — no embedded NATS for testing
        $ ls src/actors/Cena.Actors.Tests/Messaging/MessagingNatsPublisherTests.cs
        # exists, but only asserts message format, not delivery
  finding: There is no end-to-end test that publishes a NATS message from one component and asserts another component receives it. Pubs and subs are tested in isolation. The arch-002 fix (NotificationDispatcher background service) is verified by file presence + comments, not by message delivery.
  root_cause: Same as FIND-qa-014 — no service container infrastructure for the message bus in CI.
  proposed_fix: |
    Use an embedded NATS server (NATS.Client.JetStream tests use a TestNatsServer pattern) for integration tests. Cover at least 3 publisher/subscriber pairs that closed v1 findings.
  task_body: |
    Goal: Add integration tests that prove NATS publisher/subscriber pairs are wired correctly.

    Files to touch:
      - src/actors/Cena.Actors.Tests/Bus/NatsIntegrationTests.cs (NEW)
      - .github/workflows/backend.yml (add NATS service container)

    DoD:
      - [ ] At least 3 publisher/subscriber pairs verified via end-to-end message delivery
      - [ ] Tests run in CI

    Reference: FIND-qa-015

- id: FIND-qa-016
  severity: p3
  category: test
  file: .github/workflows/backend.yml
  line: 1
  framework: null
  evidence:
    - type: grep
      content: |
        $ grep -n 'collect.*coverage\|XPlat.*Coverage\|Codecov\|Sonar' .github/workflows/*.yml
        # (zero hits)
        $ grep coverlet src/**/*.csproj
        coverlet.collector
        # The package is referenced but never invoked.
  finding: Every test csproj references coverlet.collector but no workflow invokes coverage collection or uploads to a service. Coverage delta vs prior review (which v2 explicitly asks for) cannot be answered — neither v1 nor v2 has measured it.
  root_cause: Coverage was never wired up.
  proposed_fix: |
    Add `--collect:"XPlat Code Coverage"` to each dotnet test invocation, upload the cobertura XML as an artifact, and optionally push to Codecov.
  task_body: |
    Goal: Wire .NET coverage measurement into backend.yml so future reviews can answer coverage delta questions.

    Files to touch:
      - .github/workflows/backend.yml (add --collect arg + artifact upload)

    DoD:
      - [ ] Coverage XML uploaded as a CI artifact
      - [ ] A baseline number is recorded for cc3f702

    Reference: FIND-qa-016
```

## What was NOT verified in this lens

To stay within the worker budget, the following were NOT exhaustively re-verified:

- **Findings 21–55** of the closed FIND-* set. The pattern (test_absent on the kimi bundles, verified-build-fail on the targeted single-fix commits) holds for the spot-checks. A future qa run should walk the remaining 35 with the same cherry-pick technique.
- **Marten projection rebuilds** — no Postgres container in the CI scope, no test infrastructure in the worktree to rebuild a real projection. Documented as `not_run` and raised as FIND-qa-014.
- **NATS publisher/subscriber delivery** — same. Raised as FIND-qa-015.
- **Student web E2E run** — would require booting the student vite dev server + a backend stack. Out of scope for this lens; covered by `student-web-ci.yml` already.
- **Lighthouse / axe coverage on admin** — that is the `ux` lens's mandate, not `qa`.
- **A coverage number** — `coverlet.collector` is referenced in test csprojs but never invoked. There is no historical baseline. Honest answer: unknown. Raised as FIND-qa-016.
- **Mobile / Flutter test execution** — per task brief, presence-only.

## Coverage matrix totals

| Bucket | Count |
|---|---|
| `verified` (build-fail or fix-coupled) | 13 |
| `verified-existing` | 1 |
| `verified-partial` | 1 |
| `dead_ci` | 1 |
| `test_absent` | 4 |
| `not_run` | 35 (findings 21–55, deferred) |

## Cross-references for the merge step

- **FIND-qa-001** is the most important — it interlocks with FIND-sec-001. Merge step should add a `regression` tag to qa-001.
- **FIND-qa-003** interlocks with FIND-sec-005.
- **FIND-qa-004** interlocks with FIND-data-009 and the data lens's surviving QueryAllRawEvents observation.
- **FIND-qa-008** is independent but P0 because the admin app drives every tenant-scoping bug surface in v1.
- **FIND-qa-007** + **FIND-qa-010** are duplicate root cause (no clock abstraction); merge step may dedupe.
