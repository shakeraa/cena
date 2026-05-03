---
id: FIND-SEC-009
task_id: t_7d460d39c14f
severity: P0 — Critical
lens: sec
tags: [reverify, sec, security, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-sec-009: Actor host NATS dev-password fallback (partial regression of FIND-sec-003)

## Summary

Actor host NATS dev-password fallback (partial regression of FIND-sec-003)

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

# FIND-sec-009 (P0, partial regression of FIND-sec-003): Actor host NATS dev-password fallback

**Severity**: P0 (partial regression of FIND-sec-003 — Actor host NATS connection still falls back to a hard-coded password without environment gating)

**related_prior_finding**: FIND-sec-003

**Files**:
- src/actors/Cena.Actors.Host/Program.cs (line 119-122)
- src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs (add GetActorAuth)
- src/shared/Cena.Infrastructure.Tests/Configuration/CenaNatsOptionsTests.cs (add tests)

**Goal**: extend the FIND-sec-003 fix to the Actor host. The Actor host must NOT ship to non-Development with a hard-coded NATS password.

**Background**: The Admin and Student API hosts both call `CenaNatsOptions.GetApiAuth(builder.Configuration, builder.Environment)` which throws fail-fast in non-Dev when NATS credentials are missing. The Actor host inlines a different fallback at line 122 — `?? "dev_actor_pass"` — with no IsDevelopment gate. A non-Development deployment of the Actor host that forgets to set Nats:Password will silently ship using the hard-coded literal. This is the exact regression class FIND-sec-003 was supposed to close. Filed as a partial regression because the helper exists but the third host bypasses it.

**Scope**:
1. Extract Actor host NATS auth into `CenaNatsOptions.GetActorAuth` (mirror GetApiAuth exactly: dev fallback inside IsDevelopment, throw in non-dev). Default user: "actor-host". Env vars: NATS_ACTOR_USERNAME / NATS_ACTOR_PASSWORD. Config keys: Nats:User / Nats:Password (legacy) and NATS:ActorUsername / NATS:ActorPassword (preferred).
2. Replace the inline fallback at `src/actors/Cena.Actors.Host/Program.cs:119-122`.
3. Add CenaNatsOptionsTests for the new helper.
4. Recommend (not blocking) a CI gate that greps every Program.cs under src/** for the raw literals dev_actor_pass / dev_api_pass.

**Definition of Done**:
- [ ] `rg -n 'dev_actor_pass' src/actors/` returns no results
- [ ] `rg -n 'CenaNatsOptions.GetActorAuth' src/actors/` shows the call site
- [ ] CenaNatsOptionsTests includes 2 new tests for GetActorAuth (Dev returns defaults, non-Dev throws)
- [ ] `dotnet test src/shared/Cena.Infrastructure.Tests` green
- [ ] Branch: `<worker>/<task-id>-sec-009-actor-host-nats-dev-fallback`

**Files to read first**:
- src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs
- src/shared/Cena.Infrastructure.Tests/Configuration/CenaNatsOptionsTests.cs
- src/actors/Cena.Actors.Host/Program.cs (especially the NATS section ~110-135)


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_7d460d39c14f`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
