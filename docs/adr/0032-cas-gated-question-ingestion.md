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

## §16 — Layering (RDY-037 addendum)

The initial RDY-034/036 implementation placed the CAS gate contract
(`ICasVerificationGate`, `CasVerificationGate`, `CasGateMode`,
`MathContentDetector`, `CasGateExceptions`) in `Cena.Admin.Api` alongside the
`QualityGate` service. That worked for the admin authoring path but left the
ingestion path (`IngestionOrchestrator` in `Cena.Actors`) unable to reach the
gate — `Cena.Actors` does not (and must not) reference `Cena.Admin.Api`. The
result was two pre-existing direct writes to new `QuestionState` streams
allow-listed via `KNOWN_VIOLATION_TODO` markers in the architecture test —
ADR-0002 was locally enforced but globally bypassable.

RDY-037 moves the CAS gate primitives down to the `Cena.Actors.Cas`
**namespace** (inside the `Cena.Actors` project, under `src/actors/Cena.Actors/Cas/`),
where `ICasRouterService`, `IMathNetVerifier`, and `SymPySidecarClient`
already live. This placement reflects the domain status of CAS
verification: per ADR-0002, CAS is a platform-level correctness
invariant, not an HTTP-adapter concern. Every adapter (Admin.Api, Actors
ingest pipeline, future hosts) can now reach the gate without a
reverse-layer reference.

**RDY-047 clarification (2026-04-15):** There is no separate
`Cena.Actors.Cas.csproj` assembly — the gate primitives live inside
`Cena.Actors` alongside the aggregates. Any consumer that references
`Cena.Actors` gets the full aggregates surface. Separation into a
dedicated project remains an option if a future adapter needs the
invariant without the aggregates surface; until then, the "namespace
inside the same project" shape is the load-bearing structure.

### §16.1 — The single-writer invariant

`ICasGatedQuestionPersister` (new, in `Cena.Actors.Cas`) is the ONE
legitimate writer of new `QuestionState` streams in the repository. The
architecture test `SeedLoaderMustUseQuestionBankServiceTest` now allow-lists
only `CasGatedQuestionPersister.cs` plus the test file itself. Every other
caller — `QuestionBankService.CreateQuestionAsync`, `IngestionOrchestrator`,
`QuestionBankSeedData`, future AI-batch persistence, test fixtures — routes
through `persister.PersistAsync`.

The persister owns: (a) the CAS gate call (optional skip via
`preComputedGateResult` when the caller already ran the gate to drive
conditional auto-approval), (b) the event-stream append, (c) atomic storage
of the `QuestionCasBinding` document in the same session, (d) optional
companion documents (e.g., `ModerationAuditDocument`).

### §16.2 — Ingestion-stage semantics

The Bagrut OCR ingestion path produces open-ended questions whose correct
answer is not yet known at ingestion time — classification/authoring fills
that in later. The persister is called with `CorrectAnswerRaw = string.Empty`;
the gate produces an `Unverifiable` binding. These questions cannot
auto-approve (approval gate rejects missing/non-`Verified` bindings). A
follow-up `CasBackfillEndpoint` run or manual re-author upgrades the binding
to `Verified` once the answer lands.

### §16.3 — SeedContext delegate

`DatabaseSeeder.SeedAllAsync` gained a `Func<SeedContext, Task>[]` overload
(legacy `Func<IDocumentStore, ILogger, Task>[]` kept as a thin wrapper) so
seed delegates that need the CAS persister can resolve it via
`ctx.Services.GetRequiredService<ICasGatedQuestionPersister>()`. Callers
that don't need extra services (legacy seeds) use the old shape
unchanged.

### §16.4 — Rejected alternatives

- **Leave primitives in `Cena.Admin.Api`, allow-list violations**: ships a
  documented architectural hole. Rejected.
- **New writer in `Cena.Infrastructure`, CAS gate call duplicated at each
  caller**: pragmatic but leaks invariant enforcement to the edge. One
  missed call equals a new bypass — the exact failure mode this addendum
  closes. Rejected.

## §17 — Correctness vs parseability (RDY-038)

The initial RDY-034 implementation only issued `CasOperation.NormalForm` on
the author's raw answer string. NormalForm simplifies the string and
returns its canonical form — it does not compare the answer to anything.
For stem `"Solve 2x+3=7"` with author answer `"x=99"`, NormalForm returns
`"x=99"` and the gate marked the binding `Verified`. ADR-0002 says "no
math reaches students unverified"; what shipped proved the answer *parsed*,
not that it was *correct*.

`IStemSolutionExtractor` (regex-backed, best-effort) now extracts the
stem's expected solution. The gate routes:

- **Extracted equation** — builds residual `(lhs) − (rhs)` substituted
  with the author's answer, sends `CasOperation.Equivalence` vs `0`.
  Substitution is symbolic (SymPy), not numeric.
- **Extracted direct expression** — sends `CasOperation.Equivalence`
  between author answer and the extracted expression.
- **Non-extractable stem** (prose / word problem) — falls through to
  NormalForm as a parseability probe, **but the binding status is
  `Unverifiable`, not `Verified`**. Such questions cannot auto-approve
  and must be routed to manual review.

Rule: the gate only marks a binding `Verified` when it ran a stem-driven
Equivalence check. Parseability alone is never enough.

## §18 — Transactional boundary (RDY-039)

`ICasGatedQuestionPersister` now exposes a session-aware overload:

```csharp
Task<GatedPersistOutcome> PersistAsync(
    IDocumentSession session,         // caller-owned
    string questionId,
    object creationEvent,
    GatedPersistContext context, ...);
```

The session-aware overload appends events, stores the binding, and stores
companion documents on the caller's session, then **returns without
calling `SaveChangesAsync`**. Caller owns the commit — so the question
stream + binding + any pipeline document (e.g. `PipelineItemDocument`
from `IngestionOrchestrator`) commit atomically or roll back together.

The session-less overload is preserved as a thin wrapper that opens + saves
its own session for callers without composable writes (admin UI authoring,
seed data). It delegates to the session-aware overload internally.

## §19 — No caller-supplied gate results (RDY-041)

`preComputedGateResult` is removed from `ICasGatedQuestionPersister`.
Callers that already invoked `ICasVerificationGate` (e.g.
`QuestionBankService.CreateQuestionAsync`, which needs the outcome to
decide on auto-approval events) now rely on the gate's own idempotency
cache — the second call against the same `(QuestionId, CorrectAnswerHash)`
hits `CacheHits` and returns without a sidecar round-trip. Net: +0 CAS
calls, forgery surface closed.

## §20 — Binding coverage startup check (RDY-040)

`CasBindingStartupCheck` (pre-RDY-040) probed the CAS engine with `x + 1`
— it checked *liveness*, not *data*. `CasBindingCoverageStartupCheck`
(new) additionally queries `QuestionState` + `QuestionCasBinding`:

- In **Enforce** mode, `published_math > verified_bindings` calls
  `IHostApplicationLifetime.StopApplication()` — the Admin API refuses
  to serve traffic.
- In **Shadow** mode, same condition logs `[STARTUP_WARN]` and continues.
- `CENA_CAS_STARTUP_CHECK=skip` bypasses both checks with a `LogCritical`.

Gauge `cena_cas_binding_coverage_ratio` (verified / published) is emitted
on every successful query and should live on the Grafana CAS dashboard.

## Open items

- k6 load test for sustained ingestion with CAS enforcement — deferred to post-pilot.
- SIGKILL chaos test for circuit breaker recovery — deferred.
- Grafana dashboard JSON — deferred (alert thresholds in `docs/ops/alerts/cas-gate.md` cover the MVP case).
