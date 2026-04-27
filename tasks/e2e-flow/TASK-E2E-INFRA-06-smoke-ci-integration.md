# TASK-E2E-INFRA-06: CI integration of the e2e-flow smoke + responsive + perf matrices

**Status**: Proposed
**Priority**: P0 (we shipped 7 specs in commit 7e334fdd that nobody runs in CI today)
**Epic**: Shared infra
**Tag**: `@infra @ci @p0`
**Prereqs**: TASK-E2E-INFRA-04 (prod-build perf harness) recommended but not blocking

## Why this exists

The new EPIC-G admin smoke matrix, EPIC-X student smoke matrix, responsiveness sweeps, cross-page nav, and perf budgets all run **only when a developer manually invokes `npx playwright test`**. CI doesn't run them. So a PR that breaks one of those specs lands in main today.

This task wires everything into GitHub Actions.

## What to build

### Workflow file: `.github/workflows/e2e-flow.yml`

Triggered on:
- `push` to any branch (smoke matrix only — fast)
- `pull_request` to `main` (full matrix — perf + responsive + smoke + cross-page)
- Nightly cron (full matrix + production-build perf)

Steps:
1. Boot the dev stack via `docker compose -f docker-compose.yml -f docker-compose.app.yml up -d --wait`
2. `docker exec cena-firebase-emulator /seed/seed-dev-users.sh`
3. `./scripts/seed-marten-from-firebase.sh`
4. Verify env vars: `docker exec cena-student-api env | grep -E "CENA_E2E_TRUSTED_REGISTRATION|CENA_TEST_PROBE_TOKEN"`
5. `cd src/student/full-version && npx playwright install --with-deps chromium`
6. Run the appropriate spec set (selected by trigger):
   - PR: `--grep "EPIC_(A|B|C|D|E|F|G|H|I|J|K|L|X)_"`
   - Push: `--grep "@admin-smoke|@student-smoke|@auth"`
   - Cron: full suite + `EPIC-X-performance-prod-build`
7. Upload artifacts: `test-results/e2e-flow/report/` (Playwright HTML), `test-results/e2e-flow/artifacts/` (per-test screenshots + traces), and the JSON attachments from each spec
8. On failure: post a comment to the PR summarizing which specs failed with links to the HTML report

### Branch-protection rules

- Block merge to `main` if `e2e-flow / pr-check` failed
- Allow override via `e2e-flow-skip` label (with audit log) for cases where the dev stack is genuinely broken

### Stack-warming optimization

Cold-boot of the docker stack adds ~3 minutes. Mitigations:
- Cache the firebase-emulator volume between runs (it's only the user table)
- Cache the postgres data volume — Marten replays projections in seconds; Postgres startup is the long pole
- Pre-build the docker images on a separate workflow that pushes to GHCR, then pull in the test workflow

## Done when

- [ ] `.github/workflows/e2e-flow.yml` exists + green on a PR
- [ ] Branch protection on `main` requires `e2e-flow / pr-check`
- [ ] On failure, the PR gets an automated comment with HTML report link
- [ ] Wall-clock for the PR check ≤ 8 minutes (currently the full suite is ~4 min wall + ~3 min stack boot)
- [ ] Cron job posts a daily summary to the `coordination` topic on the agent bus
