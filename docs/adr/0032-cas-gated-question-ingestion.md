# ADR-0032: CAS-Gated Question Ingestion

Date: 2026-04-15
Status: Accepted
Supersedes: —
Relates to: [ADR-0002](0002-sympy-correctness-oracle.md) (SymPy correctness oracle)

## Context

Per ADR-0002, no math question may reach students unverified by the SymPy CAS oracle.
The original implementation applied CAS verification only at the `QuestionBankService.CreateQuestion` entry point. Three bypass paths remained:

1. **AI batch generation** — `AiGenerationService.BatchGenerateAsync` and `GenerateFromTemplateAsync` produced `BatchGenerateResult`s that could be persisted without a CAS check.
2. **Seed loaders** — any code path calling `StartStream<QuestionState>` directly bypassed the service.
3. **Quality gate** — the LLM-produced `FactualAccuracy` score was used for math questions despite SymPy being available.

## Decision

All question ingestion funnels through `ICasVerificationGate.VerifyForCreateAsync` when the subject is math or physics and the boundary probe (`MathContentDetector`) matches. The gate has three modes:

- `Off` — probe skipped (used in non-math subject pipelines and tests)
- `Shadow` — probe runs, results logged, questions still persist
- `Enforce` — probe runs, questions with `CasGateOutcome.Failed` are dropped

### §12 — AI generation closure
`AiGenerationService` injects `IServiceScopeFactory` + `ICasGateModeProvider`. For each generated question in batch/template flows, a scope is created, `ICasVerificationGate` resolved, and `VerifyForCreateAsync` called. In Enforce mode, failures are dropped and counted via `DroppedForCasFailure` + `CasDropReasons` on the response. Log line `[AI_GEN_CAS_REJECT]` is emitted for every drop.

### §13 — QualityGate CAS override
`QualityGateService.EvaluateAsync` optionally receives `IDocumentStore`. For math/physics subjects it loads `QuestionCasBinding` by questionId and overrides the factual accuracy score:

- `Verified` → 100
- `OverriddenByOperator` → 90 (audit trail preserved)
- `Failed` → 0 (blocks approval)
- `Unverifiable` / no binding → fall back to LLM score

### §14 — Operator surfaces
Two endpoints ship together:
- `POST /api/admin/questions/{id}/cas-override` — super-admin only, gated by env `CENA_CAS_OVERRIDE_ENABLED=true`; requires reason (≥20 chars) + ticket reference. Appends `QuestionCasBindingOverridden_V1` event and sets `binding.Status = OverriddenByOperator`. Counter `cena_cas_override_total`.
- `POST /api/admin/questions/cas-backfill` — admin only; re-verifies questions with missing/Failed/Unverifiable bindings. Idempotent via binding cache. Default batch 50, max 500.

### §15 — Startup probe + refuse-to-serve
`CasBindingStartupCheck : IHostedService` runs at Admin Host boot with probe expression `x + 1` (3 retries, 1s apart). In `Enforce` mode, a persistently-failing probe triggers `IHostApplicationLifetime.StopApplication()` — the host refuses to serve traffic. Gauge `cena_cas_startup_ok` reports 1/0.

## Enforcement

- **Architecture test**: `SeedLoaderMustUseQuestionBankServiceTest` fails the build if any file outside `QuestionBankService` or `CasBackfillEndpoint` writes `QuestionAuthored_V2 | QuestionAiGenerated_V2 | QuestionIngested_V2` directly or calls `StartStream<QuestionState>`.
- **Nightly conformance suite**: `CasConformanceSuiteRunner` loads `ops/reports/cas-conformance-baseline.md` golden cases and asserts ≥99% pass rate via `ICasRouterService`. Runs in `.github/workflows/cas-nightly.yml`.
- **Operator alerts**: see `docs/ops/alerts/cas-gate.md`.

## Consequences

- **Positive**: zero unverified math reaches students; audit-trailed overrides for true positives the CAS can't handle; fail-fast on startup prevents silent degradation.
- **Negative**: CAS latency (p99 currently ≤150ms SymPy, ≤800ms NATS fallback) adds to ingestion path.
- **Operational**: environments without a CAS engine must explicitly set `CENA_CAS_GATE_MODE=Off` or `Shadow`; default is `Enforce`.

## Open items

- k6 load test for sustained ingestion with CAS enforcement — deferred to post-pilot.
- SIGKILL chaos test for circuit breaker recovery — deferred.
- Grafana dashboard JSON — deferred (alert thresholds in `docs/ops/alerts/cas-gate.md` cover the MVP case).
