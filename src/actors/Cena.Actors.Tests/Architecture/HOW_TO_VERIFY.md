# HOW TO VERIFY — EPIC-PRR-A Architecture Gates

Two gates enforce [ADR-0012](../../../../docs/adr/0012-aggregate-decomposition.md) effective 2026-04-27:

- `FileSize500LocTest.cs` — 500-LOC rule with grandfather whitelist
- `NoNewStudentActorStateTest.cs` — event-handler count inside `StudentActor*.cs` capped at 2026-04-20 baseline (18)

Both tests run as part of `dotnet test src/actors/Cena.Actors.Tests/` in the `backend.yml` CI workflow. They use direct file I/O, not reflection, so a full run completes in ~1 second.

The recipes below are local smoke tests. They trip each gate, print the real failure message, then revert. Run them before trusting the gate in CI.

## Recipe 1 — `FileSize500LocTest` catches a file-size regression

This simulates a PR that grew `StudentActor.cs` past its grandfather baseline. Rather than actually editing `StudentActor.cs` (forbidden by EPIC-PRR-A scope), we lower the baseline to force the same assertion path.

```bash
cd "$(git rev-parse --show-toplevel)"

# 1. Back up and tighten the grandfather baseline for StudentActor.cs
#    from its real value (519) to 100. This simulates the ratchet being at
#    a lower number than the current file — the exact failure mode that
#    catches "someone added code to an already-grandfathered file".
cp src/actors/Cena.Actors.Tests/Architecture/FileSize500LocBaseline.yml \
   /tmp/baseline.bak.yml
sed -i '' 's/baseline_loc: 519/baseline_loc: 100/' \
   src/actors/Cena.Actors.Tests/Architecture/FileSize500LocBaseline.yml

# 2. Run the test — it should FAIL with a clear pointer to the file
dotnet test src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj \
  -c Debug --nologo --no-build \
  --filter "FullyQualifiedName~FileSize500LocTest" 2>&1 | tail -20

# Expected output (abridged):
#   Failed FileSize500LocTest.EveryCsFile_UnderSrc_IsEitherAtMost500Loc_OrGrandfathered
#   Error Message:
#     File-size 500-LOC rule violated (ADR-0012 Schedule Lock, effective 2026-04-27):
#     src/actors/Cena.Actors/Students/StudentActor.cs: 519 LOC exceeds grandfather
#     baseline of 100. Either refactor the file below the baseline OR ...
#     the baseline is a ratchet, it only goes down.
#     If the file is a StudentActor successor, move new code into that
#     successor (LearningSessionActor post Sprint 1, 2026-05-03).

# 3. Restore the baseline — the test should pass again
cp /tmp/baseline.bak.yml \
   src/actors/Cena.Actors.Tests/Architecture/FileSize500LocBaseline.yml
dotnet test src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj \
  -c Debug --nologo --no-build \
  --filter "FullyQualifiedName~FileSize500LocTest" 2>&1 | tail -4
# Expected: Passed! Failed: 0, Passed: 3
```

## Recipe 2 — `NoNewStudentActorStateTest` catches a new event handler

This simulates a PR that adds a new `case SomeNewEvent_V1:` line to the StudentActor event-dispatch switch. Rather than actually editing `StudentActor.Queries.cs` (forbidden by EPIC-PRR-A scope), we lower the baseline count to force the same assertion path — the test's counting logic is symmetric.

```bash
cd "$(git rev-parse --show-toplevel)"

# 1. Back up and lower the baseline count from 18 to 10, simulating the state
#    where 8 new handlers have been added since the 2026-04-20 snapshot.
cp src/actors/Cena.Actors.Tests/Architecture/NoNewStudentActorStateBaseline.yml \
   /tmp/state-baseline.bak.yml
sed -i '' 's/baseline_count: 18/baseline_count: 10/' \
   src/actors/Cena.Actors.Tests/Architecture/NoNewStudentActorStateBaseline.yml

# 2. Run the test — it should FAIL
dotnet test src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj \
  -c Debug --nologo --no-build \
  --filter "FullyQualifiedName~NoNewStudentActorStateTest" 2>&1 | tail -20

# Expected output (abridged):
#   Failed NoNewStudentActorStateTest.StudentActor_EventHandlerCount_IsAtOrBelowBaseline
#   Error Message:
#     StudentActor*.cs now contains 18 event-handler case lines, above the
#     2026-04-20 baseline of 10.
#     Per-file breakdown:
#       StudentActor.Commands.cs: 0
#       StudentActor.Mastery.cs: 0
#       StudentActor.Methodology.cs: 0
#       StudentActor.Queries.cs: 18
#       StudentActor.cs: 0
#     Adding event handlers to StudentActor is blocked per ADR-0012. New
#     handlers must route to LearningSessionActor (available post Sprint 1,
#     2026-05-03) or to a documented `StudentActor.Pending` seam noted in
#     the PR description.

# 3. Restore the baseline
cp /tmp/state-baseline.bak.yml \
   src/actors/Cena.Actors.Tests/Architecture/NoNewStudentActorStateBaseline.yml
dotnet test src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj \
  -c Debug --nologo --no-build \
  --filter "FullyQualifiedName~NoNewStudentActorStateTest" 2>&1 | tail -4
# Expected: Passed! Failed: 0, Passed: 2
```

## Alternate Recipe 1b — exercising the test against a non-StudentActor file

To prove the rule fires for *new* files >500 LOC (not just grandfathered ones), copy an existing large file to a new path and run the test. This does not modify StudentActor and does not add persistent state.

```bash
cd "$(git rev-parse --show-toplevel)"

# Create a throwaway oversize .cs file under src/
cat src/actors/Cena.Actors/Sessions/LearningSessionActor.cs \
  > src/actors/Cena.Actors/Tmp_OversizeScratch.cs

dotnet build src/actors/Cena.Actors.sln -c Debug --nologo \
  /p:SkipOpenApiGeneration=true 2>&1 | tail -3
dotnet test src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj \
  -c Debug --nologo --no-build \
  --filter "FullyQualifiedName~FileSize500LocTest" 2>&1 | tail -10

# Expected: test FAILS with
#   src/actors/Cena.Actors/Tmp_OversizeScratch.cs: 699 LOC exceeds the 500-LOC
#   rule and is not grandfathered. Refactor into smaller modules ...

# Clean up
rm src/actors/Cena.Actors/Tmp_OversizeScratch.cs
```

## CI wiring

These tests are automatically run by the existing `backend.yml` workflow step `Run Actor Tests` (`dotnet test src/actors/Cena.Actors.Tests/`). No new workflow file was added. A PR that trips either gate will fail the existing Backend CI check.

## When a legitimate downward ratchet happens

After Sprint 1 extracts LearningSession from StudentActor, the following must happen in the same PR:

1. Update `FileSize500LocBaseline.yml`: lower the `baseline_loc` for `StudentActor.cs` / `StudentActor.Commands.cs` to the new (smaller) LOC; remove entries entirely if the file drops below 500.
2. Update `NoNewStudentActorStateBaseline.yml`: lower `baseline_count` to the new total, and move the migrated event names out of `events_at_baseline`.
3. If a grandfathered file drops below 500 LOC, remove its entry — the `GrandfatherBaseline_DoesNotContainFilesUnder500Loc` test fails otherwise, which is the signal to clean up.

The two stale-entry tests (`GrandfatherBaseline_DoesNotContainFilesUnder500Loc` and `StudentActorStateBaseline_IsNotStaleAboveCurrent`) exist specifically to stop the ratchet from loosening silently.
