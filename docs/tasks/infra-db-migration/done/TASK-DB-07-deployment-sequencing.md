# TASK-DB-07: Deployment Sequencing — Migrator → Student → Admin

**Priority**: MEDIUM
**Effort**: 2-3 days
**Depends on**: DB-02, DB-06
**Track**: C
**Status**: Not Started

---

## You Are

A release engineer who has debugged one too many "which host booted first" production incidents. You make the deploy order explicit, automated, and impossible to mess up. You add health gates, not prayers.

## The Problem

After DB-06 ships, there are **three deployable artifacts** instead of one:

1. `Cena.Db.Migrator` (one-shot Kubernetes Job / init container)
2. `Cena.Student.Api.Host` (long-running service)
3. `Cena.Admin.Api.Host` (long-running service)

Both hosts boot with `AutoCreate.None` and will crash if the DB isn't where they expect. So the order matters:

```
migrator (to success) → student host → admin host
```

Nothing in the current CD pipeline enforces this. A naive "deploy all three in parallel" will intermittently fail depending on scheduler luck. A manual "remember to run the migrator first" is not a deploy strategy.

## Your Task

### 1. Convert the pipeline to a sequenced deploy

Inspect the existing CD config (GitHub Actions, Argo, Helm, Flux — whichever the project uses) and add an explicit sequence:

**Step 1 — Migrator job**
- Pull the migrator image for the release tag.
- Run as a Kubernetes Job (or equivalent).
- Wait for completion (success required).
- On failure: abort the pipeline, do not touch the hosts.

**Step 2 — Student host rollout**
- Kubernetes rolling deploy with `maxSurge=25%`, `maxUnavailable=0`.
- Readiness probe hits `/health/ready` — must pass `AssertDatabaseMatchesConfigurationAsync` + Marten connectivity.
- Rollout blocks until all new pods are ready.
- On failure: automatic rollback to previous image, fail the pipeline.

**Step 3 — Admin host rollout**
- Same pattern as student.
- Starts only after the student rollout is fully healthy for at least 60 seconds (soak window).

### 2. Helm / Kustomize chart structure

If the project uses Helm:

```
deploy/helm/cena/
├── templates/
│   ├── migrator-job.yaml        (pre-install / pre-upgrade hook)
│   ├── student-deployment.yaml
│   ├── student-service.yaml
│   ├── student-hpa.yaml         (separate autoscaler)
│   ├── admin-deployment.yaml
│   ├── admin-service.yaml
│   └── admin-hpa.yaml
└── values.yaml
```

Migrator job uses the Helm hook annotation:

```yaml
metadata:
  annotations:
    "helm.sh/hook": pre-install,pre-upgrade
    "helm.sh/hook-weight": "-10"
    "helm.sh/hook-delete-policy": before-hook-creation,hook-succeeded
```

This guarantees the migrator runs before Helm proceeds to render the deployments.

### 3. Blue/green safety

Document (and automate where possible) the rollback recipe:

- Redeploy the previous release (Helm rollback or ArgoCD revert).
- The migrator hook will run again but is idempotent — no state change.
- Student and admin hosts roll back to the previous image and pick up the stable schema.
- If the schema change was backwards-incompatible (should never happen because migrations are append-only, but just in case), document the manual `db/migrations/Vxxxx__revert_*.sql` compensating migration process.

### 4. Observability additions

Add Grafana dashboard panels for:

- Migrator job success/failure rate and duration.
- Time between migrator success and first host healthy (should be seconds, not minutes).
- Per-host readiness probe pass/fail rate.
- Per-host schema-assert error rate (should be zero after a clean migrator run).

Wire alerts:

- Migrator job failure → page on-call.
- Schema-assert error on any host start → page on-call.
- Host stuck in `CrashLoopBackOff` for > 5 min → page on-call.

### 5. Runbook

Write `docs/operations/deploy-runbook.md` covering:

- Normal deploy flow (how to trigger, what to watch).
- Emergency rollback.
- How to run the migrator manually against any environment (with connection string examples).
- How to interpret `AssertDatabaseMatchesConfiguration` failures.
- How to add a new migration (link to `db/migrations/README.md` from DB-01).
- What to do if the migrator succeeds but a host refuses to start.

## Files You Must Create

- `deploy/helm/cena/templates/migrator-job.yaml` (or equivalent)
- `deploy/helm/cena/templates/student-deployment.yaml`
- `deploy/helm/cena/templates/admin-deployment.yaml`
- `deploy/helm/cena/values.yaml`
- `docs/operations/deploy-runbook.md`
- Grafana dashboard JSON under `deploy/observability/dashboards/`

## Files You Must Modify

- Existing CD workflow (GitHub Actions or equivalent)
- Alerting config (wherever the project manages alerts)

## Files You Must Read First

- Existing deploy config in the repo — **inspect first**, don't assume Helm/Argo/Flux
- Existing `Dockerfile` patterns from [DB-02](TASK-DB-02-migrator-project.md) and [DB-06](TASK-DB-06-split-hosts.md)
- `docs/operations.md` if it exists

## Acceptance Criteria

- [ ] CD pipeline runs migrator → student → admin in strict order.
- [ ] Migrator failure aborts the pipeline before any host is touched.
- [ ] Helm (or equivalent) chart exists for all three artifacts.
- [ ] Migrator is a pre-install/pre-upgrade hook that blocks chart rendering until it succeeds.
- [ ] Student and admin have independent HPAs (student scales aggressively, admin scales modestly).
- [ ] Readiness probes hit `/health/ready` and fail the pod if the schema assert fails.
- [ ] Rollback recipe is documented and has been tested in staging.
- [ ] Grafana dashboard panels for migrator and host health exist.
- [ ] Alerts wired for migrator failure, schema-assert error, and host crash loops.
- [ ] `docs/operations/deploy-runbook.md` exists and is linked from the main operations doc.
- [ ] A full end-to-end staging deploy has been walked through by two engineers (buddy test).

## Out of Scope

- Multi-region deploy orchestration — single-region for v1.
- Canary deploys / traffic splitting — future work.
- Automated schema rollback — schema migrations are append-only by policy.
- Fully automated rollback triggering (still manual for now).
