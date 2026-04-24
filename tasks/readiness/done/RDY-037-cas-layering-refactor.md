# RDY-037: CAS Gate Layering Refactor (Close Seed-Loader Bypass the Right Way)

- **Priority**: **Critical / ship-blocker** — closes the last ADR-0002 bypass
- **Complexity**: Senior engineer + confident on cross-project refactors
- **Source**: Coordinator follow-up to RDY-034 / RDY-036 merge (2026-04-15)
- **Tier**: 1
- **Effort**: 4-6 hours (single focused session)
- **Dependencies**: RDY-034, RDY-036 (merged in commit 55d46da + 382e9fd)
- **Owner**: claude-code (coordinator taking it directly — not enqueued for a worker)

## Problem

RDY-034/036 shipped the CAS gate but left a real architectural hole behind the guardrail:

1. `SeedLoaderMustUseQuestionBankServiceTest` was added to block new `StartStream<QuestionState>` call sites.
2. Two pre-existing sites — `QuestionBankSeedData` and `IngestionOrchestrator` — were **allowlisted with `KNOWN_VIOLATION_TODO` markers** so the test could pass.
3. Both files can still create `Published` questions with no CAS binding. The gate is not actually universal; it's universal-minus-two.

Attempting the obvious fix (have both call `IQuestionBankService.CreateQuestionAsync`) fails on a project-layering constraint:

- `IngestionOrchestrator` lives in `Cena.Actors`
- `IQuestionBankService` + `ICasVerificationGate` live in `Cena.Admin.Api`
- `Cena.Actors` does **not** reference `Cena.Admin.Api` (and must not — Admin depends on Actors, reverse would be a cycle)

The CAS gate contract is placed one layer too high. This is also why the bypass existed in the first place: the ingestion path could not physically reach the gate.

## Root-cause framing (why (A) is best practice)

CAS verification is a **domain invariant** per ADR-0002 ("SymPy CAS is the sole correctness oracle — no math reaches students unverified"). Domain invariants belong in the domain/application layer, not at the HTTP edge. Every other CAS primitive (`ICasRouterService`, `IMathNetVerifier`, `SymPySidecarClient`, `QuestionCasBinding` doc, `CasConformanceSuite`) already lives in `Cena.Actors.Cas` — `ICasVerificationGate` and friends were misplaced in `Cena.Admin.Api.QualityGate`.

Consolidating CAS in `Cena.Actors.Cas`:
- Restores dependency-inversion correctness (contract lives in a layer all consumers can see).
- Eliminates the allowlist permanently (single `StartStream<QuestionState>` site behind a gated persister).
- Matches existing CAS layout in the repo.
- Makes the arch-test a true guardrail rather than an exception bag.

Rejected alternatives:
- **(B)** New `IQuestionStreamWriter` in `Cena.Infrastructure`: leaks gate-call duplication to every caller; one missed call = new bypass. Pragmatic but not correct.
- **(C)** Leave the allowlist: ships a documented architectural hole on a ship-blocker gate.

## Scope

### 1. Move CAS gate primitives into `Cena.Actors.Cas`

Relocate (preserving public API, namespace change only):

- `src/api/Cena.Admin.Api/QualityGate/CasVerificationGate.cs` → `src/actors/Cena.Actors/Cas/CasVerificationGate.cs`
  - Types: `ICasVerificationGate`, `CasVerificationGate`, `CasGateResult`, `CasGateOutcome`
- `src/api/Cena.Admin.Api/QualityGate/CasGateExceptions.cs` → `src/actors/Cena.Actors/Cas/CasGateExceptions.cs`
  - Types: `CasVerificationFailedException`, peers
- `src/api/Cena.Admin.Api/Services/CasGateMode.cs` → `src/actors/Cena.Actors/Cas/CasGateMode.cs`
  - Types: `CasGateMode` enum, `ICasGateModeProvider`, `EnvironmentCasGateModeProvider`
- `src/api/Cena.Admin.Api/Services/MathContentDetector.cs` → `src/actors/Cena.Actors/Cas/MathContentDetector.cs`
  - Types: `IMathContentDetector`, `MathContentDetector`, `MathContentAnalysis`

Old namespace `Cena.Admin.Api.QualityGate` retains only the admin-specific `QualityGateService`, `GlossaryTermChecker`, etc. — not the CAS gate.

### 2. Introduce `ICasGatedQuestionPersister` (the single `StartStream<QuestionState>` site)

New file: `src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs`

```csharp
namespace Cena.Actors.Cas;

public sealed record GatedPersistContext(
    string Subject,
    string Stem,
    string CorrectAnswerRaw,
    string Language,
    string? Variable = null);

public sealed record GatedPersistOutcome(
    CasGateOutcome Outcome,
    string Engine,
    string? FailureReason,
    QuestionCasBinding? Binding);

public interface ICasGatedQuestionPersister
{
    /// <summary>
    /// The ONE legitimate write path for a new QuestionState stream. Runs the
    /// CAS gate, appends the caller-supplied creation event, stores the
    /// QuestionCasBinding atomically. Throws CasVerificationFailedException
    /// in Enforce mode on Failed outcome; otherwise returns the outcome for
    /// the caller to surface.
    /// </summary>
    Task<GatedPersistOutcome> PersistAsync(
        string questionId,
        object creationEvent,
        GatedPersistContext context,
        IReadOnlyList<object>? extraEventsOnNewStream = null,
        IReadOnlyList<object>? companionDocuments = null,
        CancellationToken ct = default);
}
```

Implementation owns the single `session.Events.StartStream<QuestionState>(...)` call in the entire codebase.

### 3. Rewire callers

- **`QuestionBankService.CreateQuestionAsync`** (Admin.Api) — inject `ICasGatedQuestionPersister`, replace the inline CAS + StartStream block with one persister call. Keep the quality-gate post-processing (auto-approve on Verified+passing) where it is.
- **`IngestionOrchestrator`** (Actors) — inject `ICasGatedQuestionPersister`, replace `session.Events.StartStream<QuestionState>(questionId, ingestedEvent)` with persister call. Bagrut ingestion has no known correct answer at this stage → pass empty `CorrectAnswerRaw`, gate produces `Unverifiable` binding, question flows to classification with `NeedsReview = true`.
- **`QuestionBankSeedData`** (Admin.Api, static class) — accept `ICasGatedQuestionPersister` as a parameter. Each seed question already has the correct answer text available in `QuestionOptionData.IsCorrect` → pass that to the persister.
- **`CasBackfillEndpoint`** — unchanged (it resolves existing streams; no StartStream).

### 4. Update DI registrations

- `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs` — add `services.AddScoped<ICasGatedQuestionPersister, CasGatedQuestionPersister>();`
- `src/actors/Cena.Actors.Host/Program.cs` — add the same registration (IngestionOrchestrator runs there)
- Both hosts already register `ICasVerificationGate`, `ICasGateModeProvider`, `IMathContentDetector` — registrations move with the types but DI lines stay the same if `using Cena.Actors.Cas;` is added.

### 5. Update `DatabaseSeeder.SeedAllAsync`

Current signature:
```csharp
params Func<IDocumentStore, ILogger, Task>[] additionalSeeds
```

Problem: seed lambdas can't reach the persister with only `IDocumentStore` + `ILogger`.

Fix: upgrade the delegate to `Func<SeedContext, Task>` where:
```csharp
public sealed record SeedContext(IDocumentStore Store, IServiceProvider Services, ILogger Logger);
```

All in-tree call sites (2) update their lambdas. No public API surface beyond the repo is affected.

### 6. Update `SeedLoaderMustUseQuestionBankServiceTest`

- Remove both `KNOWN_VIOLATION_TODO` entries from the allowlist.
- Keep (or narrow to) only the implementation file: `CasGatedQuestionPersister.cs` — the ONE place allowed to write `StartStream<QuestionState>`.
- Remove `QuestionBankService.cs` and `CasBackfillEndpoint.cs` from allowlist (they no longer contain the pattern after the refactor).
- Test comment updates: pattern is now a true universal guardrail, not a gated allowlist.

### 7. Update ADR-0032

Add addendum documenting:
- Why CAS gate primitives live in `Cena.Actors.Cas`, not `Cena.Admin.Api.QualityGate`.
- The `ICasGatedQuestionPersister` single-writer invariant.
- Ingestion stage produces `Unverifiable` bindings (no answer known yet); classification/authoring adds the answer and triggers re-gate via `CasBackfillEndpoint` or a new `UpgradeIngestedBindingAsync` operation.

## Files to Create / Modify

### Create
- `src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs`
- `src/actors/Cena.Actors/Cas/CasVerificationGate.cs` (moved)
- `src/actors/Cena.Actors/Cas/CasGateExceptions.cs` (moved)
- `src/actors/Cena.Actors/Cas/CasGateMode.cs` (moved)
- `src/actors/Cena.Actors/Cas/MathContentDetector.cs` (moved)
- `src/shared/Cena.Infrastructure/Seed/SeedContext.cs` (new delegate carrier)
- `docs/adr/0032-cas-gated-question-ingestion.md` (addendum §16 layering)

### Modify
- `src/api/Cena.Admin.Api/QuestionBankService.cs` — use persister
- `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — accept persister, loop per-question
- `src/api/Cena.Admin.Api/AiGenerationService.cs` — `using Cena.Actors.Cas;` update only
- `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` — `using` update only
- `src/api/Cena.Admin.Api/Endpoints/CasBackfillEndpoint.cs` — `using` update only
- `src/api/Cena.Admin.Api/Endpoints/CasOverrideEndpoint.cs` — `using` update only
- `src/api/Cena.Admin.Api/Startup/CasBindingStartupCheck.cs` — `using` update only
- `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs` — `using` + add persister line
- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` — 2 seed call-site updates
- `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` — use persister
- `src/actors/Cena.Actors.Host/Program.cs` — register persister + update seed call-site
- `src/shared/Cena.Infrastructure/Seed/DatabaseSeeder.cs` — new delegate signature
- `src/actors/Cena.Actors.Tests/Architecture/SeedLoaderMustUseQuestionBankServiceTest.cs` — allowlist down to 1 entry

### Delete
- `src/api/Cena.Admin.Api/QualityGate/CasVerificationGate.cs` (after move)
- `src/api/Cena.Admin.Api/QualityGate/CasGateExceptions.cs` (after move)
- `src/api/Cena.Admin.Api/Services/CasGateMode.cs` (after move)
- `src/api/Cena.Admin.Api/Services/MathContentDetector.cs` (after move)

## Acceptance Criteria

- [ ] Exactly one `StartStream<QuestionState>` call exists in the `src/` tree — inside `CasGatedQuestionPersister.cs`.
- [ ] `SeedLoaderMustUseQuestionBankServiceTest` allowlist contains only `CasGatedQuestionPersister.cs` (and the test file itself).
- [ ] Zero `KNOWN_VIOLATION_TODO` markers remain in the arch test.
- [ ] `Cena.Admin.Api.QualityGate` namespace no longer contains CAS gate types (greppable verification).
- [ ] `QuestionBankService.CreateQuestionAsync` produces identical behavior (verified by existing Admin.Api tests green).
- [ ] `IngestionOrchestrator.ProcessFileAsync` produces Bagrut questions with `QuestionCasBinding { Status = Unverifiable }` (verified by new integration test).
- [ ] `QuestionBankSeedData.SeedQuestionsAsync` produces `Verified` bindings for math/physics seed questions when the CAS sidecar is reachable; `Unverifiable` + `NeedsReview` otherwise.
- [ ] Full `Cena.Actors.sln` builds with 0 errors.
- [ ] No new test failures vs pre-refactor baseline (52 pre-existing remain; nothing new breaks).
- [ ] ADR-0032 addendum §16 merged.

## Rollout

Single branch: `claude-code/cas-gate-residuals`. Single merge commit to `main` after full-sln build passes. Branch includes only the layering refactor; test coverage and 52-failure triage ship as separate follow-ups to keep the diff reviewable.

## Out of Scope

- The 5 missing test files (CasGatingTests, CasIdempotencyTests, MathContentDetectorTests, CasGateModeTests, CasOverrideAuditTests) — separate follow-up.
- `CasConformanceSuiteRunner` 500-pair runner + baseline measurement — separate follow-up.
- The 52 pre-existing test failures (GDPR, focus-analytics path bugs, NSubstitute matcher, Marten proxy cast) — separate follow-ups per category.

## Rationale Log (for the ADR addendum)

> Why not keep `ICasVerificationGate` in `Cena.Admin.Api`? Because ADR-0002 treats CAS as a platform-level domain invariant, and platform invariants must be enforceable from any adapter. Leaving the gate at the HTTP-adapter layer permitted the `IngestionOrchestrator` bypass we are now closing. The fix is to move the invariant to the layer it belongs to, not to work around the layering.

> Why not make `ICasGatedQuestionPersister` the only CAS entry point and delete `ICasVerificationGate`? Because AI generation (`AiGenerationService`) needs to run verification on *candidate* questions before deciding which to persist — verification without persistence. Keeping the gate interface separate preserves that use case.

> Why pass `SeedContext` to seed delegates rather than injecting services directly? Because seed functions are static (no DI), called from a single seeder orchestrator, and accepting a context carrier is simpler than converting every seed to a registered service.
