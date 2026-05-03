# TASK-PRR-219: Migration safety net (feature-flagged staged upcast + retry + DLQ)

**Priority**: P0 — hard blocker per persona-sre
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-sre (blocker), persona-redteam
**Source docs**: persona-sre findings (section 7 one-shot migration is mass-lockout surface), brief §7
**Assignee hint**: kimi-coder + SRE review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, blocker, sre
**Status**: Blocked on PRR-218
**Source**: persona-sre review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Replace the brief's "first login triggers one-shot upcast" migration with a staged, feature-flagged, retry-able, DLQ-backed migration that cannot lock out pre-multi-target users if upcast throws mid-execution.

## Why this is a blocker

Per persona-sre: an unguarded one-shot upcast on first post-deploy login has a 100% blast radius. If the upcast throws for any reason (legacy row with unexpected shape, Firebase claim missing, transient Marten error), the user's first login 500s and they can't retry — they're effectively locked out until manual remediation. Per memory "No stubs — production grade", this can't ship.

## Scope

### Migration strategy

1. **Pre-compute phase (offline, pre-deploy)**: batch job walks all existing `AdminUser` + Marten aggregates with legacy `StudentPlanConfig`, produces a migration manifest `{studentId → upcastPayload}`.
2. **Feature flag**: `multi_target_plan_migration_enabled` (per-tenant, per-student overrides). Default off.
3. **Staged rollout**: flag enabled for internal tenants first, then 1%, 10%, 100%.
4. **Runtime upcast**: first login with flag ON → checks manifest → applies `ExamTargetAdded` event with `source=Student, assignedById=studentId, reasonTag=null, examCode=inferred, sittingCode=inferred`.
5. **Retry**: upcast failure logs + enqueues to DLQ + returns `LoginSucceeded(planState=pending)`; user reaches home with a "your plan is being upgraded" banner (not blocking).
6. **DLQ worker**: retries every 15 min with exponential backoff; alerts on-call after 3 failed attempts.
7. **Idempotency**: migration events carry `migration_source_id` so double-application is a no-op.

### Inference rules

- `examCode`: from Firebase `grade`+`track` claim (11/12 + 5U → `BAGRUT_MATH` + Track 5U). If claim absent, default `BAGRUT_MATH + 4U`.
- `sittingCode`: legacy `DeadlineUtc` → nearest valid moed tuple via catalog (PRR-220). If none fits, archived state on creation.
- `weeklyHours`: legacy `WeeklyTimeBudget` directly.

### Observability

- Metric `migration.exam_target_upcast.{attempted,succeeded,failed,dlq}` per tenant.
- Structured log with `student_id`, `tenant_id`, `legacy_plan_id`, `error` on each failure.
- Dashboard with migration progress percentage.

## Files

- `src/api/Cena.Student.Api.Host/Migration/ExamTargetUpcast.cs` (new)
- `src/api/Cena.Student.Api.Host/Migration/UpcastDlqWorker.cs` (new)
- `src/api/Cena.Student.Api.Host/Migration/UpcastManifestLoader.cs` (new)
- Feature flag wiring in config + tenant admin UI.
- Pre-deploy batch job: `scripts/migration/generate-upcast-manifest.ts` or equivalent.
- Integration tests: happy path, inference fallback, 500-error retry, DLQ alerting, idempotency.

## Definition of Done

- Feature flag default OFF — deploy safe.
- Pre-deploy manifest generation documented + tested against real legacy data in staging.
- DLQ worker retries with exponential backoff; on-call alert wired.
- Post-migration audit: every legacy `StudentPlanConfig` has a corresponding `ExamTargetAdded` event with non-null `migration_source_id`.
- Runbook entry for Bagrut-morning migration failure: how to disable flag, reprocess DLQ, unblock affected students.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- Memory "Check container state before build".
- Memory "No stubs — production grade".
- ADR-0001 (tenancy isolation — manifest scoped per tenant).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sha + migration runbook doc URL>"`

## Related

- PRR-218 (aggregate), PRR-220 (catalog — sittingCode inference dep).
- persona-sre findings §7.
