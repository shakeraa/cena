# RDY-042: CAS Residuals — Conformance, Atomicity, Layering, Telemetry (Fixes #3, #4, #7–#12)

- **Priority**: High — ship-blocker companions to RDY-038..041
- **Complexity**: Mid-senior engineer, multiple small scoped fixes
- **Source**: Senior-architect review of `claude-code/cas-gate-residuals` (2026-04-15)
- **Tier**: 1
- **Effort**: 2-3 days total (can be split)
- **Dependencies**: RDY-037

## Problem

The senior-architect review surfaced 7 additional findings that are not stand-alone ship-blockers but collectively misrepresent what the CAS gate does. Each is small; together they let ADR-0032 claim more than the code delivers.

## Scope (each bullet is a separate PR-sized slice)

### #3 — Conformance runner bypasses `ICasRouterService`

`CasConformanceSuiteRunner` calls `IMathNetVerifier` + `ISymPySidecarClient` directly ([src/actors/Cena.Actors/Cas/CasConformanceSuiteRunner.cs:107](../../src/actors/Cena.Actors/Cas/CasConformanceSuiteRunner.cs#L107)). ADR-0032 §Enforcement claims it runs via the router.

Fix: add a second mode `RunThroughRouterAsync` that uses `ICasRouterService` end-to-end; nightly runs both. Publish both numbers in the baseline doc. Pick one as the CI gate; update ADR to match.

### #4 — Conformance baseline doesn't reconcile

Baseline doc enumerates 27 cases; runner filters 450 placeholders and claims "50 concrete pairs"; no measured nightly has run. Fix:
- First nightly run publishes measured numbers (pair count + agreement rate) into `ops/reports/cas-conformance-baseline.md`
- Remove inconsistent "27 / 50 / 500" narrative — pick the real number
- Block the "Enforce in prod" flag on a green nightly present in the report

### #7 — Override endpoint missing security-team notification

`CasOverrideEndpoint` emits a SIEM log only. RDY-036 §9 required Slack/email notification on every `cas-override` event.

Fix: inject `ISecurityNotifier` (thin wrapper around existing notification infra or a new HTTP client for the Slack webhook env `CENA_SECURITY_SLACK_WEBHOOK`). On any override, fire-and-forget the notification; failure logs warning but does not fail the request.

### #8 — AI-batch `DroppedForCasFailure` counter unverified

Add `AiGenerationCasGatingTests` that drives the real gate through `AiGenerationService.BatchGenerateAsync` and asserts:
- Dropped batch size matches `DroppedForCasFailure`
- `CasDropReasons` contains a per-question reason
- Prometheus counter `cena_ai_gen_cas_drops_total` increments by the dropped count

### #9 — Layering: `Cena.Actors.Cas` project separation (or ADR correction)

ADR-0032 §16 claims CAS primitives moved to `Cena.Actors.Cas`. In reality they live in a `Cas/` folder inside the single `Cena.Actors` project.

Pick one:
- (a) Create `src/actors/Cena.Actors.Cas/Cena.Actors.Cas.csproj`, move the 10 Cas files, update references everywhere. Then the stated layering is real.
- (b) Amend ADR-0032 §16 to say "`Cena.Actors.Cas` namespace inside `Cena.Actors`" — remove the "enforceable separation" claim.

Default recommendation: (b) now, (a) when/if another adapter appears that doesn't want the full aggregates surface.

### #10 — Math-subject-but-no-math-content bypass

`CasVerificationGate` treats `Subject=math` + empty `HasMathContent` detection as math → NormalForm on prose → Error → `Unverifiable` + `NeedsReview`. Word problems land unverified.

Fix: for math-subject questions where the stem has math-adjacent natural-language patterns (numbers + operators, Arabic `اِحسُب`, Hebrew `חשב`), route through a word-problem extractor (RDY-038 `IStemSolutionExtractor`). If still non-extractable, return `Unverifiable` but tag the binding with reason `word_problem_non_extractable` so the admin queue can prioritize.

### #11 — Idempotency cache only keys `Verified` bindings

Current cache short-circuits only on `Status=Verified` ([CasVerificationGate.cs:175](../../src/actors/Cena.Actors/Cas/CasVerificationGate.cs#L175)). A transient `Failed` on retry can flip outcomes under concurrency.

Fix:
- Add Marten unique index `(QuestionId, CorrectAnswerHash)` on `QuestionCasBinding` (enforced via `UniqueIndex` config + migration)
- Cache hit for any `Status` where `VerifiedAt > now - 5m` — returns the same outcome; no double-write
- Integration test: 10 concurrent verify calls on the same `(QuestionId, hash)` produce exactly one CAS call and one binding row

### #12 — `IngestionOrchestrator` still emits `QuestionIngested_V1`

ADR-0032 + RDY-034 talk exclusively about `V2`. Check: is there a `QuestionIngested_V2` record? If yes, migrate ingest; if no, either add V2 and migrate, or amend ADR-0032 to say the gate covers both `V1` and `V2` and update the arch-test regex.

## Deferred-but-claimed (tracked here; owner required)

- **k6 load test** (`tests/Cena.Load/CasGateLoadTests.cs`) — ADR-0032 "Open items". Create follow-up ticket + owner before pilot.
- **Chaos SIGKILL test** (`tests/Cena.Chaos/SymPyKillTest.cs`) — same.
- **Grafana dashboard JSON** (`ops/grafana/cas-gate.json`) — same.
- **52 pre-existing test failures**: commit `adf14d0` closes 19; 33 remain. File per-category triage tickets (GDPR, focus-analytics, NSubstitute matcher, Marten proxy cast).
- **PERSONAS.md roster**: 9 personas documented; coordinator references "10". Add the missing persona or fix the reference.
- **Open the actual PR**: branch is pushed, PR not yet opened — URL in coordinator notes is the "create PR" page.

## Acceptance Criteria

Each numbered slice above ships as its own commit + tests, and the corresponding narrative in ADR-0032 / RDY-036 README is corrected to match what the code actually does.
