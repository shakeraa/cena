---
id: FIND-SEC-012
task_id: t_417c37caf3fb
severity: P1 — High
lens: sec
tags: [reverify, sec, security]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-sec-012: Actor host /api/actors/stats is anonymous and leaks studentIds

## Summary

Actor host /api/actors/stats is anonymous and leaks studentIds

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

# FIND-sec-012 (P1): Actor host /api/actors/stats anonymous + leaks studentId

**Severity**: P1 (PII leak — studentIds and per-student activity counters returned to anonymous callers; minors-data sensitivity)

**Files**:
- src/actors/Cena.Actors.Host/Program.cs (419-458)
- src/actors/Cena.Actors.Tests/Host/ActorHostEndpointAuthTests.cs (new)

**Goal**: gate `/api/actors/stats` behind SuperAdminOnly and stop embedding raw studentId values in the response payload. Add CORS to the Actor host so it cannot be reached cross-origin.

**Background**: `GET /api/actors/stats` on the Actor host has NO `.RequireAuthorization()`. Its response includes every active actor's studentId and sessionId plus per-student activity counters and the last 20 errors WITH `e.StudentId`. The two sibling endpoints `/api/actors/diag` and `/api/actors/warmup` both call `RequireAuthorization(SuperAdminOnly)`, so the omission is asymmetric. The Actor host also has NO CORS configuration at all (no AddCors, no UseCors), so the only defence is network firewall. studentId is identifying for minors.

**Scope**:
1. Append `.RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)` to the MapGet at line 419-458.
2. Replace `studentId = a.StudentId` and `e.StudentId` with `studentIdHash = EmailHasher.Hash(a.StudentId)`. Same for the recentErrors block.
3. Add `builder.Services.AddCors(...)` configuring a narrow allow-list (configurable via Cors:AllowedOrigins like the Admin/Student hosts) and call `app.UseCors()` before `UseAuthentication()`.
4. New ActorHostEndpointAuthTests covering anonymous + 3 roles against `/api/actors/stats`.

**Definition of Done**:
- [ ] `/api/actors/stats` requires SuperAdminOnly
- [ ] Response contains no raw studentId or sessionId; hashed values only
- [ ] CORS configured on Actor host
- [ ] Tests assert anonymous -> 401, MODERATOR/ADMIN -> 403, SUPER_ADMIN -> 200
- [ ] Branch: `<worker>/<task-id>-sec-012-actor-stats-auth`

**Files to read first**:
- src/actors/Cena.Actors.Host/Program.cs (especially lines 410-505)
- src/api/Cena.Admin.Api.Host/Program.cs (CORS pattern at line 96-109)
- src/shared/Cena.Infrastructure/Compliance/EmailHasher.cs


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_417c37caf3fb`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
