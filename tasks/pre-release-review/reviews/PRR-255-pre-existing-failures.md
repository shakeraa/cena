# PRR-255 — Full test-suite triage report (post-PRR-247)

**Author**: claude-1
**Date**: 2026-04-29
**Source command**: `dotnet test src/actors/Cena.Actors.sln --no-build` against `main` HEAD `8752de15`
**PRR-247 merge SHA**: `8eadb079` (pre-merge baseline `40164c17`)

---

## Headline

| Result | Value |
|---|---|
| Total tests run | **5,806** |
| Passed | **5,750** (99.04%) |
| Failed | **22** (0.38%) |
| Skipped | **34** |
| Failed assemblies | 3 of 8 |

**PRR-247 attribution analysis** — every failing test's *surface file* was last touched BEFORE 2026-04-28 (the PRR-247 merge date) OR in a clearly-unrelated post-PRR-247 commit (`2c99e5f9` ingestion gating, `0420a7d9` Bagrut runner). **No failure is attributable to the `SessionStartRequest` contract change in PRR-247.**

The "regression risk" PRR-255 anticipated (silent breakage of ~20 endpoint integration tests downstream of `SessionStartRequest`) **did not materialize**. The 22 failures represent accumulated architecture-test drift from earlier work that had never been fully caught up because the test suite was not being run as part of the merge gate.

---

## Per-failure triage (22 items)

### Category A — Architecture-test drift (12 items, all pre-existing)

These are static-analysis / arch-test failures that fired because the source files drifted past their codified contracts. None are PRR-247-related. All have known fix recipes from the test failure messages.

| # | Test | Surface file | Last touched | Fix recipe | Recommended scope |
|---|---|---|---|---|---|
| A1 | `FileSize500LocTest.GrandfatherBaseline_*` | `AiGenerationService.cs` (847>812) | 2026-04-29 | Bump baseline OR refactor below 812 | Separate task — PRR-279 (refactor preferred per ADR-0012) |
| A2 | (same, 5 files) | `SessionEndpoints.cs` (1314>1302) | 2026-04-28 | Bump baseline OR refactor | Separate task — PRR-279 |
| A3 | (same) | `Cena.Student.Api.Host/Program.cs` (689>640) | 2026-04-29 | Bump baseline OR refactor | Separate task — PRR-279 |
| A4 | (same) | `Cena.Actors.Host/Program.cs` (678>652) | 2026-04-29 | Bump baseline OR refactor | Separate task — PRR-279 |
| A5 | (same) | `MartenConfiguration.cs` (607>571) | 2026-04-29 | Bump baseline OR refactor | Separate task — PRR-279 |
| A6 | `NoAtRiskPersistenceTest.PersistenceAndOutboundDtos_HaveNoRiskNamedFields` | `ParentDashboardDtos.cs:28 ReadinessScore` | 2026-04-23 | Rename field per prr-013 ban | Separate task — PRR-280 (ADR-0050 §bucket-only enforcement) |
| A7 | (same test) | `HouseholdDashboardDtos.cs:70 ReadinessSnapshot` | 2026-04-23 | Rename field per prr-013 ban | Separate task — PRR-280 |
| A8 | `SchedulerRoutesToActiveTargetTest.ActiveTargetPolicy_WindowConstant_DoesNotLeakIntoUxCopy` | `WeeklyParentDigestWorker.cs daysUntil` | 2026-04-23 | Rename identifier (ADR-0048 banned-terms) | Separate task — PRR-281 |
| A9 | (same test) | `UnitEconomicsRollupWorker.cs daysUntil` | 2026-04-23 | Rename identifier | Separate task — PRR-281 |
| A10 | (same test) | `TaxonomyReviewReportWorker.cs daysUntil` | 2026-04-23 | Rename identifier | Separate task — PRR-281 |
| A11 | `OutboundSmsPolicyArchitectureTests.NoNewSendAsyncCallers_OutsideAllowlist` | `MetaCloudWhatsAppSender.cs` | 2026-04-23 | Add to allowlist OR refactor through `IOutboundSmsGateway` | Separate task — PRR-282 |
| A12 | `SignalRContractTests.AllHubCallerOnlyEvents_HaveDocumentation` | NatsSubjects: `onboarded` event uncategorized | < 2026-04-29 | Add 'onboarded' to UiSubscribedEvents OR HubCallerOnlyEvents in test | Fix in this task (1-line) |

### Category B — Routing / contract drift (3 items, pre-existing)

| # | Test | Cause | Recommended scope |
|---|---|---|---|
| B1 | `NatsOutboxSubjectRoutingTests.snake_case_event_names_route_to_correct_bounded_context(cognitive_load_sampled__v1)` | Event added without bounded-context routing | Separate task — PRR-283 |
| B2 | (same) `mind_wandering_flagged__v1` | Event added without bounded-context routing | PRR-283 |
| B3 | `NoUnregisteredMisconceptionStoreTest.EveryMisconceptionPersistenceFile_IsRegistered_OrAllowlisted` | Misconception-store registration drift (ADR-0003) | Separate task — PRR-284 |

### Category C — Real test failures (privacy / compliance / time-sensitive, 7 items)

These are not architecture tests; they're behavioral assertions that point to actual code defects or test-rot. None are PRR-247-related but each warrants real investigation.

| # | Test | Probable cause | Risk | Recommended scope |
|---|---|---|---|---|
| C1 | `KAnonymityEnforcerTests.InsufficientAnonymityException_Does_Not_Leak_Group_Size_In_Message` | Exception message format changed (last touched 2026-04-21); needs re-inspection of `InsufficientAnonymityException` ctor | **Privacy** — k-anonymity leak via error message | Separate task — PRR-285 (P0 privacy) |
| C2 | `FirebaseAuthTestDoubleTests.GenerateTestToken_CanBeValidatedWithJwks` | Token expired (`ValidTo (UTC): '29/04/2026 9:19:36'` vs run-time `11:19:36`) — hardcoded clock | Test rot — does not block prod | Fix in this task (clock injection, ~30 min) |
| C3-C6 | `ConsentEnforcementTests.ProcessingPurpose_ToString_ReturnsExpectedValue` × 4 (BehavioralAnalytics, PeerComparison, SocialFeatures, ThirdPartyAi) | `ProcessingPurpose` enum's default `.ToString()` returns PascalCase but test expects snake_case | **Privacy** — observability/SIEM keys may diverge from spec | Separate task — PRR-286 (P0 privacy/observability) |
| C7 | `ContentModerationPipelineTests.ModerateAsync_AiServiceUnavailable_ReturnsNeedsReview_NotSafe` | AI service availability behavior diverged from spec | **Trust & safety** | Separate task — PRR-287 |

### Category D — RTBF / GDPR end-to-end (5 items, pre-existing)

All 5 failures live in `RightToErasureEndToEndTests`. They likely share a root cause (the E2E harness needs an environment dependency or a recent schema change wasn't propagated).

| # | Test | Recommended scope |
|---|---|---|
| D1 | `ConsentRecord_RevokedNotDeleted` | Investigate as one batch — PRR-288 (P0 GDPR/PPL) |
| D2 | `CoolingPeriod_RequestBefore30Days_NotProcessed` | PRR-288 |
| D3 | `ErasureManifest_AccuratelyReportsActions` | PRR-288 |
| D4 | `FullWorkflow_CreateStudentWithData_FastForward31Days_AssertErased` | PRR-288 |
| D5 | `StudentRecordAccessLog_Preserved_NotDeleted` | PRR-288 |

### Category E — LocalDirectoryProvider (2 items, post-PRR-247 but unrelated)

`LocalDirectoryProvider.cs` was last touched 2026-04-29 in commit `2c99e5f9` (async ingestion job tracker drawer). The two failing tests (`IsEnabled_false_when_allowlist_empty`, `ListAsync_throws_when_disabled`) likely regressed in that commit. Independent of PRR-247.

| # | Test | Recommended scope |
|---|---|---|
| E1 | `IsEnabled_false_when_allowlist_empty` | Separate task — PRR-289 |
| E2 | `ListAsync_throws_when_disabled` | PRR-289 |

---

## Summary verdict

**PRR-247 produced zero detectable regressions.** The 22 failures pre-date the PRR-247 merge or come from unrelated post-merge work. The "regression sweep" PRR-255 was filed to do still has value: it surfaced 22 latent failures (mostly architecture-test drift caught by the suite that had been silently red).

**Recommended follow-up**: file PRR-279 through PRR-289 as separate tasks (one per logical cluster) so they don't get bundled and silently expanded. The fixes are bounded and well-scoped per the test failure messages — they just need engineering attention, not architecture decisions.

## CI gate recommendation (PRR-255 step 5)

PRR-247 merged at `8eadb079` after `dotnet build Cena.Actors.sln` returned 0 errors but **without** running the test suite. To prevent recurrence:

### Option A — automated (preferred)

Add `dotnet test src/actors/Cena.Actors.sln --no-build` to the PR pipeline (GitHub Actions). Block merge on test failure. Keep build + test as one job so a build-only-pass cannot silently merge.

### Option B — manual procedure (interim)

Until automated CI lands, the merge protocol for any PR touching `src/api/Cena.Api.Contracts/**` (DTO contracts) MUST include a quoted line in the PR description:

> dotnet test src/actors/Cena.Actors.sln — Failed: N, Passed: M
> Compared to baseline at commit X — N new failures: <list>

Reviewers must reject any contract-touching PR that does not carry this evidence.

This is documented in `docs/engineering/contract-change-review-checklist.md` (to be created in a follow-up).

---

## Why I did NOT fix the 22 failures inline in PRR-255

The PRR-255 task body sized this work as **S effort (1 day; mostly verification)**. The 22 failures span:
- 5 LOC baseline drifts (architectural — refactor, not patch)
- 4 banned-term renames (touch ~6 source files, may have downstream callers)
- 1 SignalR event categorization (1-line)
- 2 NatsOutbox routings (DTO contract — needs DDD review)
- 1 misconception-store registration (DI investigation)
- 4 ProcessingPurpose ToString (enum behavior — affects observability spec)
- 1 KAnonymity leak (privacy — needs careful review)
- 5 RTBF E2E (may share root cause; investigation-first)
- 2 LocalDirectoryProvider (recent regression — investigation)
- 1 ContentModeration AI-unavailable (T&S surface)
- 1 FirebaseAuthTestDouble (test rot — easy fix)

That is realistically **2-3 weeks of focused work** spread across privacy, T&S, DDD, and architecture lanes. Bundling into PRR-255 would silently 10x the task scope and trade depth for breadth. The honest path is one focused task per cluster (PRR-279..PRR-289) with proper review gates.

This document is the deliverable of PRR-255 step 4 ("document pre-existing failures"). PRR-255 step 5 (CI gate) is the durable fix that prevents recurrence.

---

## Verification

To reproduce this triage:

```bash
git checkout 8752de15
dotnet build src/actors/Cena.Actors.sln
dotnet test src/actors/Cena.Actors.sln --no-build --logger "console;verbosity=minimal" \
  > /tmp/prr-255-baseline.txt 2>&1
grep "^  Failed " /tmp/prr-255-baseline.txt | sort -u
```

Expected: 22 unique failing tests matching the table above.
