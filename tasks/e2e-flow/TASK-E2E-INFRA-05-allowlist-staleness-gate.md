# TASK-E2E-INFRA-05: Allowlist-staleness CI gate — auto-decay known-broken entries

**Status**: Proposed
**Priority**: P2
**Epic**: Shared infra
**Tag**: `@infra @ci @hygiene @p2`
**Prereqs**: TASK-E2E-BG-01 through 05 (so the allowlist has real removable entries)

## Why this exists

The EPIC-G admin smoke matrix carries an explicit `KNOWN_BROKEN_ROUTES` map keyed on route, value = reason string. The map serves two purposes:

1. **Soft-fails** so CI doesn't block on backend gaps the SPA team can't fix
2. **Documents** what's broken with a reason future maintainers can read

The risk: someone fixes a backend, the smoke test starts passing on that route, **but the allowlist entry stays** (because the test passes regardless). The map quietly grows stale, hiding new regressions behind "oh that page was always broken".

The current spec already does a stale-entry annotation (it's a soft warning). This task hardens it.

## What to build

1. **Promote stale-entry detection from warning to soft-fail** in CI:
   - If a route in `KNOWN_BROKEN_ROUTES` no longer reproduces its console-error → emit a CI annotation that fails the build with a clear "remove this entry" message
   - Locally (non-CI), keep it as a warning so devs can iterate without churn
2. **Auto-decay timestamping**: each entry must include a `surfacedAt: '2026-04-27'` ISO date. After 30 days, the entry is auto-promoted to a hard fail unless someone refreshes the timestamp.
   ```ts
   const KNOWN_BROKEN_ROUTES: Record<string, { reason: string; surfacedAt: string; ticket?: string }> = {
     '/apps/system/ai-settings': {
       reason: 'admin-api: GET /api/admin/ai/settings 500',
       surfacedAt: '2026-04-27',
       ticket: 'TASK-E2E-BG-01',
     },
     // ...
   }
   ```
3. **Allowlist budget cap**: limit `Object.keys(KNOWN_BROKEN_ROUTES).length` to ≤ 15 by default. Adding the 16th entry requires explicitly raising the cap in the spec and noting why in the commit message — friction that makes the team triage instead of allowlist.
4. **Allowlist linter**: a small `scripts/check-allowlist-staleness.mjs` that grep-greps `KNOWN_BROKEN_*` across the workflows dir and reports counts + oldest entry. Wire it into pre-commit.

## Done when

- [ ] All current allowlist entries (admin smoke + responsive + future student smoke) carry the `{reason, surfacedAt, ticket}` shape
- [ ] Spec auto-fails on stale entries in CI mode (toggle via `process.env.CI`)
- [ ] Spec auto-fails when an allowlist exceeds 15 entries
- [ ] `scripts/check-allowlist-staleness.mjs` ships and is invoked from `npm run pretest`
- [ ] `tests/e2e-flow/README.md` documents the policy
