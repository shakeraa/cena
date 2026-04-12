---
id: FIND-DATA-022
task_id: t_7a7cb4849130
severity: P0 — Critical
lens: data
tags: [reverify, data, dead-query, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-data-022: AnalysisJobActor dead query - EventTypeName PascalCase regression

## Summary

AnalysisJobActor dead query - EventTypeName PascalCase regression

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

GOAL
Fix the dead query in AnalysisJobActor.LoadAttempts where `EventTypeName ==
"ConceptAttempted_V1"` (PascalCase) never matches Marten's snake_case alias
`concept_attempted_v1`. Add a project-wide guard test that catches any future
PascalCase event-name regression.

ROOT CAUSE
FIND-data-006 (prior fix for `nameof(T)` in ExperimentAdminService) swept
`src/api/Cena.Admin.Api/` but missed `src/actors/Cena.Actors/Services/`.
This actor powers the stagnation analysis pipeline and its six analyze
methods consume the always-empty attempt list and emit confidence scores
of zero for every stagnant student.

EVIDENCE
  $ rg 'EventTypeName == "[A-Z]' src/ --type cs
    src/actors/Cena.Actors/Services/AnalysisJobActor.cs:244
    .Where(e => e.StreamKey == studentId && e.EventTypeName == "ConceptAttempted_V1")

  Every other call site uses snake_case (the correct alias).
  Marten's NameToAlias for typeof(ConceptAttempted_V1).Name returns
  "concept_attempted_v1". String comparison against "ConceptAttempted_V1"
  matches zero rows on every call.

IMPACT
The admin "stagnation insights" UI built on AnalysisJobActor is showing
permanently-clean data even when the underlying student is stuck on a
concept for days — exactly the situation the pipeline was built to detect.

FILES TO TOUCH
  - src/actors/Cena.Actors/Services/AnalysisJobActor.cs:244
  - src/actors/Cena.Actors.Tests/Services/AnalysisJobActorTests.cs
    (add regression test that seeds a real ConceptAttempted_V1 and asserts
     AttemptsLoaded > 0)
  - tests/Cena.EventStore.Tests/EventTypeAliasGuardTests.cs (NEW)

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-022
  - src/api/Cena.Admin.Api/ExperimentAdminService.cs (the v1 fix pattern
    for the same bug class — confirm parity)

DEFINITION OF DONE
  - Line 244 uses snake_case alias
    (`"concept_attempted_v1"`) OR switches to typed event query
    (`.QueryRawEventDataOnly<ConceptAttempted_V1>()`).
  - Regression test asserts the actor loads >0 attempts when a real
    ConceptAttempted_V1 is in the stream (pre-fix the count is 0;
    post-fix the count matches the seeded count).
  - Static guard test (EventTypeAliasGuardTests) scans `src/actors/` and
    `src/api/` with regex `EventTypeName == "[A-Z][A-Za-z0-9]+_V\d+"`
    and FAILS the build if ANY match is found. Prove it catches a
    regression: commit a deliberate PascalCase predicate, run the
    guard, revert.
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with snake_case fix diff, test path, paste of pre-fix
  test failure output, paste of post-fix test success output, plus proof
  the guard test rejects a deliberate regression.

TAGS: reverify, data, dead-query, regression
RELATED PRIOR FINDING: FIND-data-006 (prior fix swept Cena.Admin.Api only;
  the actor tree still has the same bug class)
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_7a7cb4849130`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
