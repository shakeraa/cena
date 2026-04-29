# TASK-PRR-274: PRR-247 legacy SessionStartRequest shape contract test (G4)

**Priority**: P2 — defensive regression prevention; back-compat claim verification
**Effort**: XS (1-2 hours; one xUnit test in existing project)
**Lens consensus**: claude-5 self-audit 2026-04-29 G4 + super-architect framing item #24 ("validate back-compat in tests, not assertions")
**Source docs**: claude-5 self-audit 2026-04-29 G4; PRR-247 (sha 8eadb079); [src/actors/Cena.Actors.Tests/Session/SessionStartRequestContractTests.cs](../../src/actors/Cena.Actors.Tests/Session/SessionStartRequestContractTests.cs) (existing tests cover only new shape)
**Assignee hint**: anyone with `dotnet test` infra; trivial scope
**Tags**: source=claude-5-audit-2026-04-29,epic=epic-prr-f,priority=p2,test,back-compat
**Status**: Ready
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

PRR-247 shipped `SessionStartRequest` extension at sha `8eadb079` with claim "legacy clients keep working unchanged." The 4 contract round-trip tests added in `SessionStartRequestContractTests.cs` cover only the new shape. The legacy shape `{Subjects, DurationMinutes, Mode}` was asserted as still-supported but never tested.

Add 1 contract test asserting the legacy shape deserializes correctly and the validator at `/api/sessions/start` accepts it.

## Scope

1. Add a new test method `Legacy_shape_with_only_three_required_fields_deserialises_and_passes_validator` to `SessionStartRequestContractTests.cs`. The test:
   - Deserializes the legacy JSON shape `{"subjects":["math"],"durationMinutes":15,"mode":"practice"}` into `SessionStartRequest`.
   - Confirms `Subjects`, `DurationMinutes`, `Mode` are populated; `ExamScope` is null; `ActiveExamTargetId` is null.
   - Sends the legacy shape to the test server's `/api/sessions/start` endpoint and confirms 200 + `SessionStartResponse` body.
2. Optional second test: legacy shape with no `ExamScope` field at all should NOT be rejected by the validator's "scope-required when target-id present" rule.
3. Mark all such tests with `[Trait("source", "PRR-274-back-compat")]` so they're easy to filter in CI.

## Files

### Modified
- `src/actors/Cena.Actors.Tests/Session/SessionStartRequestContractTests.cs` — add 1-2 test methods.

### New
- (none expected — test extends existing file)

## Definition of Done

- Test added; runs green.
- Test reproducibly passes against PRR-247 contract.
- Adds the trait for CI filtering.
- Full Cena.Actors.Tests project builds + runs green.

## Blocking

- None.

## Non-negotiable references

- PRR-247 contract change (sha 8eadb079)
- claude-5 super-architect framing item #24

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + test sha + run output 'Passed!'>"`
