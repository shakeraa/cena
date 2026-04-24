# RDY-040: Admin API — Binding-Coverage Startup Check (Fix #5)

- **Priority**: **Critical / ship-blocker** — boots into unsafe state today
- **Complexity**: Mid-senior engineer
- **Source**: Senior-architect review of `claude-code/cas-gate-residuals` (2026-04-15)
- **Tier**: 1
- **Effort**: 3-5 hours
- **Dependencies**: RDY-036 §5 (was under-implemented)

## Problem

`CasBindingStartupCheck` ([src/api/Cena.Admin.Api/Startup/CasBindingStartupCheck.cs](../../src/api/Cena.Admin.Api/Startup/CasBindingStartupCheck.cs)) probes the **engine** with `x + 1` and calls that "startup safety".

RDY-036 §5 required the opposite: count `Published` math/physics questions vs `Verified` `QuestionCasBinding` rows and refuse to start if `published > verified`. The current check catches sidecar outages; it does not catch data corruption. It lets the Admin API boot happily with thousands of unverified `Published` questions in the DB.

The `CENA_CAS_STARTUP_CHECK=skip` bypass env-var is also missing — only `CasGateMode` degrades.

## Scope

### 1. Add binding-coverage hosted service

New `CasBindingCoverageStartupCheck : IHostedService`:

```csharp
// Query: 
//   SELECT COUNT(*) FROM mt_doc_question_state
//     WHERE data->>'Subject' IN ('math','physics','chemistry','mathematics','maths')
//     AND data->>'Status' = 'Published';
// and
//   SELECT COUNT(*) FROM mt_doc_question_cas_binding
//     WHERE data->>'Status' = 'Verified';
```

Behavior:
- `publishedMath > verifiedBindings` + mode == Enforce → log `[STARTUP_ABORT] reason=cas_binding_mismatch published={n} verified={m}` and call `IHostApplicationLifetime.StopApplication()`
- `publishedMath > verifiedBindings` + mode == Shadow → log `[STARTUP_WARN]`, continue
- Emit gauge `cena_cas_binding_coverage_ratio` (verified/published)

### 2. Preserve the engine-probe check

Keep `CasBindingStartupCheck` as the engine-liveness probe; rename to `CasEngineStartupProbe` for clarity. Coverage check is a separate service that runs *after* engine probe.

### 3. Explicit skip env-var

Both checks honour `CENA_CAS_STARTUP_CHECK=skip` (off by default). When skipping, log a `LogCritical` so ops sees it in Grafana annotations.

### 4. Tests

- `CasBindingCoverageStartupCheckTests` — seeded DB: 10 published math questions, 5 Verified bindings → service calls `StopApplication` in Enforce
- Same scenario in Shadow → does not stop, emits metric
- `CENA_CAS_STARTUP_CHECK=skip` short-circuits both paths
- Registration test: both hosted services in Admin API Program

### 5. Update ADR-0032 §15

Distinguish engine-probe from coverage-probe. Both must pass in Enforce.

## Acceptance Criteria

- [ ] `CasBindingCoverageStartupCheck` registered in `Cena.Admin.Api.Host/Program.cs`
- [ ] Enforce mode + mismatch aborts startup with typed log
- [ ] Gauge `cena_cas_binding_coverage_ratio` emitted
- [ ] `CENA_CAS_STARTUP_CHECK=skip` bypasses with critical log
- [ ] Engine probe renamed `CasEngineStartupProbe`; both services registered
- [ ] Tests pass; ADR-0032 §15 updated
- [ ] Full sln builds green
