# Post-Install Task Runner

Ops sequence executed against a freshly-deployed Cena stack (dev, staging, or pre-pilot prod) *after* the Admin API container is serving and Marten migrations have run. Each task is idempotent, fail-loud, and records its outcome.

## When to run

- Immediately after a fresh `docker compose up` / `helm install`
- After a `wipe-questions` CLI run (see RDY-036 §1)
- Before flipping `CENA_CAS_GATE_MODE=Enforce` in a new environment

## How to run

```bash
# All tasks, default env file at ./.env
./scripts/post-install/run.sh

# A specific task (by leading number or slug)
./scripts/post-install/run.sh 02
./scripts/post-install/run.sh cas-backfill

# Dry-run: print what would happen, do not execute
DRY_RUN=1 ./scripts/post-install/run.sh

# Override the admin base URL + token (otherwise pulled from env file)
CENA_ADMIN_URL=https://admin.staging.cena CENA_ADMIN_TOKEN=… \
  ./scripts/post-install/run.sh
```

Every task emits a structured log line on completion:

```
[POST_INSTALL] task=<slug> status=<ok|fail|skip> duration_ms=<n>
```

Failures exit non-zero and stop the runner — fix the cause and re-run; idempotent tasks will skip already-completed work.

## Tasks

Numbered in the order they must run; gaps reserved for future tasks.

| # | Slug | Purpose | Ticket |
|---|------|---------|--------|
| 01 | `cas-engine-probe` | `x+1` probe against SymPy sidecar. Must pass before any gated write. | RDY-036 §15 |
| 02 | `cas-binding-coverage` | Assert `published_math ≤ verified_bindings`; refuse to continue otherwise. | RDY-040 |
| 03 | `cas-backfill` | Invoke `POST /api/admin/questions/cas-backfill` to upgrade `Unverifiable` bindings produced pre-gate. | ADR-0032 §14 |
| 04 | `conformance-baseline` | Trigger the CAS conformance nightly once so `ops/reports/cas-conformance-baseline.md` has a measured number before Enforce. | RDY-044 |
| 05 | `load-baseline` | Trigger the k6 load nightly once against this env; upload the summary. | RDY-051 |
| 06 | `gate-mode-enforce` | Flip `CENA_CAS_GATE_MODE=Enforce` via config reload (no redeploy). | RDY-036 rollout |

## Adding a task

1. Create `scripts/post-install/tasks/NN-slug.sh` (make it executable).
2. Source `../lib/common.sh` at the top; use `post_install_begin`, `post_install_ok`, `post_install_skip`, `post_install_fail`.
3. Fail loud on any precondition miss.
4. Append a row to the table above with the relevant RDY ticket.
