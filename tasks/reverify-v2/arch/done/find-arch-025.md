---
id: FIND-ARCH-025
task_id: t_b62dff440b61
severity: P1 — High
lens: arch
tags: [reverify, arch, fake-fix]
status: pending
assignee: unassigned
created: 2026-04-11
type: fake-fix
---

# FIND-arch-025: ClaudeTutorLlmService fake-streams a unary Anthropic response (label drift)

## Summary

ClaudeTutorLlmService fake-streams a unary Anthropic response (label drift)

## Severity

**P1 — High** — FAKE-FIX

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

**Goal**: Make the tutor SSE endpoint actually stream from
Anthropic instead of fake-streaming a unary response.

ClaudeTutorLlmService.StreamCompletionAsync calls
`_client.Messages.Create()` (NON-streaming), waits for the full
response, then "simulates streaming" with Task.Delay(20ms) per word.
The SSE endpoint claims real streaming via the "HARDEN: No stubs"
comment but the student waits the full LLM completion latency before
seeing the first token. This is a label-drift / fake-fix related to
prior FIND-arch-004.

**Files to read first**:
  - src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs
  - src/actors/Cena.Actors/Tutor/ITutorLlmService.cs
  - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs (line 244+ for the SSE handler)
  - Anthropic SDK Messages.CreateStream docs

**Files to touch**:
  - src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs (use CreateStream)
  - src/actors/Cena.Actors.Tests/Tutor/ClaudeTutorLlmServiceStreamingTests.cs (new)

**Definition of Done**:
  - [ ] `grep -n "Simulate streaming\|Task.Delay" src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs` returns zero
  - [ ] First LlmChunk arrives before Anthropic finishes the full response
  - [ ] Token accounting still correct (sum streaming usage events)
  - [ ] SSE endpoint sends real, immediate token deltas to the browser
  - [ ] File header updated to remove "simulated streaming"

**Reporting requirements**:
  - Paste a tcpdump or browser DevTools network screenshot showing
    a real progressive SSE response (not a single burst).
  - Paste streaming test output.

**Reference**: FIND-arch-025 in docs/reviews/agent-arch-reverify-2026-04-11.md
**Related prior finding**: FIND-arch-004


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_b62dff440b61`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
