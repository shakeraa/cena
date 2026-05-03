# TASK-PRR-255: Full test-suite run after PRR-247 contract change + regression fix

**Priority**: P0 — production-readiness gate; "no stubs" memory rule
**Effort**: S (1 day; mostly verification)
**Source docs**: claude-code self-audit 2026-04-28, [PRR-247](done/TASK-PRR-247-adr-0060-session-mode-wiring.md)
**Assignee hint**: claude-1, claude-2, claude-3, kimi-coder — anyone with `dotnet test` infra
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-f,priority=p0,test,regression,production-gate
**Status**: Ready
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

PRR-247 was merged at sha `8eadb079` after `dotnet build Cena.Actors.sln` returned 0 errors. The test suite was **not** run. The contract change touched `SessionStartRequest` (a high-traffic DTO consumed by SignalR session-summary, replay export, history list, and ~20 endpoint integration tests). Regressions from JSON deserialization changes, validator changes, or new field defaults are likely silent until tests catch them.

This task closes the gap: run the full test suite against `main` HEAD, document any regressions, and fix them.

## Scope

1. From a clean checkout of `main` HEAD, run `dotnet test Cena.Actors.sln --no-build` after a clean `dotnet build`. Capture full output.
2. Compare failure list against the same command on the commit immediately before `8eadb079` (which is `40164c17`) — any test that regressed in the PRR-247 merge is a regression.
3. Fix every regression in a follow-up branch `claude-{worker}/PRR-255-test-suite-recovery`. Push + merge.
4. If a test was already broken before `8eadb079`, document in `tasks/pre-release-review/reviews/PRR-255-pre-existing-failures.md` with the failure cause + an opinion on whether it should be fixed in this task or filed separately.
5. Add a CI gate (or document the manual procedure) that PRR-247-style contract changes can never merge again without a `dotnet test` step.

## Definition of Done

- `dotnet test Cena.Actors.sln` returns 0 failures on `main` HEAD.
- Pre-existing failures (if any) documented + filed.
- CI / manual procedure update merged.
- Result reported via queue with per-test-project counts.

## Blocking

- None.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<dotnet test summary + regression-fix branch sha>"`
