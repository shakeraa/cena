# RDY-036: CAS Gate Production Hardening + Pre-Pilot Question Wipe

- **Priority**: **Critical / ship-blocker** — companion to RDY-034
- **Complexity**: Senior engineer + DevOps familiarity
- **Source**: Coordinator follow-up after RDY-034 scope review (2026-04-15)
- **Tier**: 1
- **Effort**: 3-4 days
- **Dependencies**: RDY-034 (CAS gate must land first; this task hardens it for production)

## Problem

RDY-034 wires `ICasRouterService.VerifyAsync` into question creation, approval, and AI-batch generation. That closes the correctness-oracle gap at the code level, but it is **not production-ready on its own**:

- No rollout strategy — a hard gate flip on day one risks rejecting legitimate questions if the CAS layer has false-positives we have not measured.
- No observability — we can't alert on CAS failure rate, circuit-breaker trips, or sidecar latency.
- No idempotency — client retries during CAS timeouts could double-verify and double-log.
- No first-boot safety — Admin API could start with unverified questions in the DB and silently ship them.
- No clean-slate migration — we are pre-pilot with zero real student data; rather than build a backfill pipeline for questions authored under the old no-gate regime, we should wipe the question DB and re-seed everything through the gated path.
- No baseline measurement for `CasConformanceSuite` — gating CI on 99% agreement is sound in theory but MathNet vs SymPy may disagree more than that in practice; we need the actual number before the gate goes on.
- Admin API deployment artifacts (compose, k8s) do not yet include the new NATS + Redis + SymPy sidecar dependencies that CAS requires.
- Emergency bypass path is undefined — if CAS is broken and we need to ship a hotfix question, there is no auditable override.

Leaving these open means RDY-034 ships a gate that is correct-in-principle but brittle-in-practice.

## Scope

### 1. Clean-slate question DB wipe tool

Since we are pre-pilot and no real student attempts reference production questions, we should wipe and re-seed through the gated path rather than backfilling.

- New CLI command: `dotnet run --project src/tools/Cena.Tools.DbAdmin -- wipe-questions`
- Gated by env var `CENA_ALLOW_QUESTION_WIPE=true` + interactive `yes/no` confirmation prompt
- Truncates: `mt_events` rows for question streams (`QuestionAuthored_V2`, `QuestionAiGenerated_V2`, `QuestionIngested_V2`, `QuestionApproved_V1`, `QuestionRetired_V1`), `mt_doc_question_state`, `mt_doc_question_cas_binding`, AI generation batch records, quality-gate cached results
- **Refuses to run** if any `StudentAttempt_V*` event references a question stream (protects us if pilot starts partial)
- Emits `[QUESTION_WIPE] operator={user} db={name} events_deleted={n} docs_deleted={m} timestamp={iso}` SIEM log
- Not exposed as an HTTP endpoint — CLI only, run by a human operator with DB credentials
- Scope of wipe: dev, staging, and pre-pilot prod (all three currently have zero real attempts)

### 2. Shadow-mode rollout

Add `CENA_CAS_GATE_MODE` env var with three values:

- `off` — no CAS call (emergency kill switch; logs warning on every question-create)
- `shadow` — CAS runs, result logged + metric emitted, but **does not block** question creation
- `enforce` — CAS runs and blocks (the RDY-034 behavior)

Deploy path: ship in `shadow` for 48h, measure false-positive rate via metrics, flip to `enforce` via config change (no redeploy).

### 3. Observability

Add Prometheus metrics (via existing `IMeterFactory` / OpenTelemetry stack):

- `cena_cas_verification_total{result="verified|failed|unverifiable|error",engine="mathnet|sympy|none"}`
- `cena_cas_verification_duration_seconds{engine}` (histogram)
- `cena_cas_circuit_breaker_state{engine}` (gauge: 0=closed, 1=half-open, 2=open)
- `cena_questions_rejected_cas_total{reason,subject}`
- `cena_cas_gate_mode` (gauge, labels: `mode="off|shadow|enforce"`)

Add alerts (doc in `docs/ops/alerts/cas-gate.md`):

- `CasRejectionRateHigh` — rejection rate > 10% over 10 min (catches runaway false positives)
- `CasCircuitBreakerOpen` — breaker open for > 5 min (catches sidecar outage)
- `CasLatencyP99High` — p99 latency > 2s (catches degraded sidecar)

Add a Grafana dashboard JSON at `ops/grafana/cas-gate.json`.

### 4. Idempotency on `QuestionCasBinding`

- Add composite unique index `(QuestionId, CorrectAnswerHash)` where `CorrectAnswerHash = SHA256(CorrectAnswer.Normalize(NFC))`
- On CAS verify: look up existing binding by `(QuestionId, CorrectAnswerHash)` before calling CAS; return cached result if found (hit increments `cena_cas_cache_hit_total` counter)
- Client retries during CAS timeout no longer double-verify
- Cache TTL: indefinite (bindings are append-only; a new answer version creates a new hash)

### 5. First-boot safety guard

Admin API startup hook:

- Query: count math/physics questions in `Published` state, count `Verified` `QuestionCasBinding` rows
- If `published_math_count > verified_binding_count` → **refuse to start**, log `[STARTUP_ABORT] reason=cas_binding_mismatch published={n} verified={m}`, exit non-zero
- Bypass: `CENA_CAS_STARTUP_CHECK=skip` for dev/test only (logs warning)

### 6. Seed data must route through the gate

- Audit every `session.Events.StartStream<QuestionState>` call site in the repo
- Seed loaders (`src/tools/Cena.Seed/*`, test fixtures under `Cena.Actors.Tests/Fixtures/*`) must call `QuestionBankService.CreateQuestionAsync` instead of appending events directly
- Add architectural guardrail test: `SeedLoaderMustUseQuestionBankServiceTest` — scans IL for direct `StartStream<QuestionState>` calls in `Cena.Seed.*` assemblies and fails the test if any are found (whitelist exception: the service itself)

### 7. CasConformanceSuite baseline measurement

Before wiring CI gate at 99%, run the suite once and publish results.

- Add `dotnet test --filter CasConformanceSuiteRunner` to a nightly workflow (`.github/workflows/cas-nightly.yml`)
- Publish `ops/reports/cas-conformance-baseline.md` with the measured MathNet vs SymPy agreement percentage
- If measured agreement < 99%, either tune the suite (drop pairs where disagreement is expected) or adjust the threshold — **do not silently lower the CI gate**
- Decision + reasoning captured as an addendum to ADR-0032

### 8. Deployment manifest updates

Admin API now requires NATS, Redis, and the SymPy sidecar. Update:

- `deploy/docker-compose.admin.yml` — add `nats`, `redis`, `sympy-sidecar` services + health checks + dependency ordering
- `deploy/k8s/admin-api.yaml` — add sidecar container, service accounts, network policies
- `deploy/k8s/sympy-sidecar.yaml` — new manifest (if not already there from Student API deployment)
- Liveness probe on SymPy sidecar: `GET /health` expects `{"status":"ok"}` within 500ms
- Update `docs/ops/runbook-admin-api.md` with sidecar troubleshooting

### 9. Emergency bypass with audit trail

For true emergencies (SymPy is down, a critical question must ship):

- Endpoint `POST /api/admin/questions/{id}/cas-override`
- Body: `{ "reason": "string (min 20 chars)", "approver_ticket": "string" }`
- Requires `super-admin` role (new — add to IdentityRoleSeed)
- Requires `CENA_CAS_OVERRIDE_ENABLED=true` env var (disabled by default)
- Emits `[CAS_OVERRIDE] operator={user} question_id={id} reason={reason} ticket={ticket}` SIEM log
- Appends `QuestionCasBindingOverridden_V1` event to the question stream
- Marks binding `Status = OverriddenByOperator` — surfaces in admin UI as a warning badge
- Every override triggers a Slack/email notification to the security team

### 10. Load + chaos tests

- `tests/Cena.Load/CasGateLoadTests.cs` — k6 script that fires 100 concurrent question creates, asserts p95 < 3s, failure rate < 1%
- `tests/Cena.Chaos/SymPyKillTest.cs` — integration test that SIGKILLs the SymPy sidecar mid-batch, asserts circuit breaker opens within 10s and `NeedsReview=true` fallback kicks in
- Wire both into `.github/workflows/backend-nightly.yml`

### 11. Word-problem math detection

Non-math subjects bypass CAS, but word problems ("If Sara has 2x + 3 apples...") contain math and must still be gated.

- Add `IMathContentDetector` service:
  - Input: `QuestionBody` (LaTeX + prose) + `Subject` string
  - Output: `HasMathContent: bool` + `ExtractedExpressions: string[]`
  - Implementation: regex for `$...$`, `\(...\)`, `\[...\]`, common equation patterns (`=`, `≠`, `≥`, digits + operators)
- Wire into `QuestionBankService.CreateQuestionAsync` before the subject-based bypass
- Language/history questions with embedded math still gate on CAS for the extracted expressions

## Files to Create / Modify

### Create

- `src/tools/Cena.Tools.DbAdmin/Program.cs` + `WipeQuestionsCommand.cs`
- `src/api/Cena.Admin.Api/Services/CasGateMode.cs` — enum + resolver from env
- `src/api/Cena.Admin.Api/Startup/CasBindingStartupCheck.cs`
- `src/api/Cena.Admin.Api/Endpoints/CasOverrideEndpoint.cs`
- `src/api/Cena.Admin.Api/Services/IMathContentDetector.cs` + `MathContentDetector.cs`
- `src/api/Cena.Admin.Api.Tests/CasGateModeTests.cs`
- `src/api/Cena.Admin.Api.Tests/CasIdempotencyTests.cs`
- `src/api/Cena.Admin.Api.Tests/CasOverrideAuditTests.cs`
- `src/api/Cena.Admin.Api.Tests/MathContentDetectorTests.cs`
- `src/actors/Cena.Actors.Tests/Architecture/SeedLoaderMustUseQuestionBankServiceTest.cs`
- `tests/Cena.Load/CasGateLoadTests.cs`
- `tests/Cena.Chaos/SymPyKillTest.cs`
- `.github/workflows/cas-nightly.yml`
- `ops/grafana/cas-gate.json`
- `ops/reports/cas-conformance-baseline.md` (populated by first nightly run)
- `docs/ops/alerts/cas-gate.md`
- `deploy/k8s/sympy-sidecar.yaml` (if missing)

### Modify

- `docs/adr/0032-cas-gated-question-ingestion.md` — add addenda: §12 (shadow mode), §13 (clean-slate migration policy), §14 (override audit rules), §15 (conformance baseline)
- `src/shared/Cena.Infrastructure/Documents/QuestionCasBinding.cs` — add `CorrectAnswerHash`, `OverriddenByOperator` status value
- `src/api/Cena.Admin.Api/QuestionBankService.cs` — respect `CasGateMode`, use `IMathContentDetector`
- `src/api/Cena.Admin.Api.Host/Program.cs` — register startup check, override endpoint, math detector
- `deploy/docker-compose.admin.yml`
- `deploy/k8s/admin-api.yaml`
- `docs/ops/runbook-admin-api.md`
- All seed loaders under `src/tools/Cena.Seed/*` and `tests/**/Fixtures/*`
- `.github/workflows/backend-nightly.yml`
- `src/auth/Cena.Auth/IdentityRoleSeed.cs` — add `super-admin` role

## Acceptance Criteria

- [ ] `wipe-questions` CLI exists, is env-gated, refuses to run with live student attempts, emits SIEM log
- [ ] `CENA_CAS_GATE_MODE` toggles off/shadow/enforce without redeploy; shadow mode logs + meters but does not block
- [ ] All CAS metrics emitted + scraped; Grafana dashboard renders; 3 alerts wired
- [ ] `QuestionCasBinding` has `(QuestionId, CorrectAnswerHash)` unique index; duplicate CAS calls hit the cache (verified by idempotency test)
- [ ] Admin API refuses to start if published math questions outnumber verified bindings (verified by integration test)
- [ ] Zero `StartStream<QuestionState>` calls outside `QuestionBankService` (verified by architectural guardrail test)
- [ ] CasConformanceSuite nightly runs; baseline report is populated; CI threshold decision documented in ADR-0032
- [ ] Admin deployment (compose + k8s) brings up NATS + Redis + SymPy sidecar with correct probes and ordering
- [ ] CAS override endpoint exists, requires super-admin, env-gated off by default, emits audit event + SIEM log + Slack notification
- [ ] Load test passes at 100 concurrent creates (p95 < 3s, failure < 1%)
- [ ] Chaos test passes: killing SymPy sidecar mid-batch opens circuit breaker within 10s, remaining questions flow to `NeedsReview=true`
- [ ] Word problems with embedded math are gated on CAS even when `Subject` is non-math
- [ ] Full `Cena.Actors.sln` builds with 0 errors; new tests pass; no regressions in existing tests

## Out of Scope

- Migrating real student data (there is none pre-pilot; this is the whole point of §1)
- Admin UI changes to surface CAS status badges (separate UX task)
- Retroactive CAS verification of historical analytics data
- Multi-sidecar load balancing (single SymPy sidecar is fine for pilot scale)

## Rollout Plan

1. Merge RDY-034 (CAS gate exists, mode defaults to `enforce` only in integration tests)
2. Merge this task with `CENA_CAS_GATE_MODE=shadow` in staging config
3. Run `wipe-questions` on staging, re-seed through gated path
4. Observe metrics for 48h: confirm rejection rate < 5%, p99 latency < 1s, no circuit breaker trips
5. Flip staging to `enforce`, hold for 24h, re-verify
6. Run `wipe-questions` on pre-pilot prod, re-seed
7. Deploy to prod in `shadow` mode; observe for 24h
8. Flip prod to `enforce` via config change; pilot is ship-clear

> **Why this is ship-blocking**: The CAS gate landing in RDY-034 is necessary but insufficient. Without shadow rollout we risk day-one false-positive floods; without metrics we cannot detect degradation; without the clean-slate wipe we ship pre-gate questions indistinguishable from post-gate questions. All three must land before pilot.
