---
id: FIND-SEC-013
task_id: t_18c6b8a10695
severity: P1 — High
lens: sec
tags: [reverify, sec, security]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-sec-013: /api/content/questions/{id}/explanation leaks LLM prompt + serves draft questions

## Summary

/api/content/questions/{id}/explanation leaks LLM prompt + serves draft questions

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

# FIND-sec-013 (P1): Student content endpoint leaks LLM system prompt + serves draft questions

**Severity**: P1 (any authenticated student can read system prompts for unpublished/draft questions; prompt injection enablement + content leakage)

**Files**:
- src/api/Cena.Admin.Api.Host/Endpoints/ContentEndpoints.cs (61-86)
- src/api/Cena.Admin.Api.Tests/ContentEndpointsExplanationTests.cs (new)

**Goal**: stop the student-facing /api/content/questions/{id}/explanation route from returning unpublished questions or the LLM prompt that generated them.

**Background**: `GET /api/content/questions/{id}/explanation` returns `aiPrompt = question.AiProvenance?.PromptText` (the system prompt the LLM used). Two problems: (1) the LINQ at line 67 has no Status filter (compare to line 31 of the same file which does), so draft / pending-review / rejected questions are served; (2) exposing the system prompt to learners enables prompt-injection experiments. Reachable by any student token because the group is `RequireAuthorization()` with no role policy. Cena learners are minors, so this is a children's-data exposure as well as an IP leak.

**Scope**:
1. Add `&& q.Status == QuestionLifecycleStatus.Published` to the LINQ filter at line 67.
2. Remove `aiPrompt = question.AiProvenance?.PromptText` from the student-facing response. Keep it on the admin `/api/admin/questions/{id}` route.
3. Add a regression test in `src/api/Cena.Admin.Api.Tests/ContentEndpointsExplanationTests.cs` seeding draft + published questions and asserting:
   - draft id -> 404
   - published id -> 200 with no aiPrompt key
   - missing id -> 404
   - deprecated id -> 404

**Definition of Done**:
- [ ] `rg -n 'aiPrompt' src/api/Cena.Admin.Api.Host/Endpoints/ContentEndpoints.cs` returns no matches
- [ ] LINQ filter on the explanation route includes Published gate
- [ ] New test file passes
- [ ] Branch: `<worker>/<task-id>-sec-013-content-explanation-leak`

**Files to read first**:
- src/api/Cena.Admin.Api.Host/Endpoints/ContentEndpoints.cs (especially compare lines 27-58 to lines 61-86)
- src/actors/Cena.Actors/Questions/QuestionState.cs (Status enum)


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_18c6b8a10695`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
