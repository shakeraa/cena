# RDY-059: Corpus Expander — one-shot "populate the bank from stems"

**Parent**: RDY-058 (generate-similar) + RDY-019c (coverage report)
**Priority**: High — closes the last authorship-scale gap; without this, populating the bank is a 30-click wand march
**Complexity**: Mid (coordinator + planner + budget guard + UI)
**Effort**: 1 day
**Blocker status**: None.

## Problem

RDY-058 gave curators one-click variant generation from a single question. For 30 seed stems, that's 30 manual clicks. Worse, the curator has no view of "which taxonomy leaves are still sparse" at the moment of generation — coverage gaps are queried separately via RDY-019c.

The gap: a coordinator that reads the seed set (or a selector result), identifies sparse leaves via `ContentCoverageService`, loops `GenerateSimilarHandler` per source × difficulty-band, and writes every CAS-passing child — with budget caps, dry-run mode, and SuperAdmin gate.

## Scope

### 1. Contracts

```csharp
public sealed record CorpusExpansionRequest(
    string SourceSelector,             // "seed" | "bagrut" | "concept:CAL-003" | "all"
    IReadOnlyList<DifficultyBandConfig> DifficultyBands,
    int StopAfterLeafFull = 5,         // skip leaves that already have >= this many items
    int MaxTotalCandidates = 200,      // hard cap on LLM-generated attempts
    string? Language = null,           // optional override; otherwise inherit source
    bool DryRun = true);               // default dry-run — operator must opt into cost

public sealed record DifficultyBandConfig(float Min, float Max, int Count);

public sealed record CorpusExpansionResponse(
    string RunId,
    bool DryRun,
    IReadOnlyList<PerSourcePlan> Plan,
    IReadOnlyList<PerSourceOutcome>? Outcomes,    // null in dry-run
    int TotalPlannedCandidates,
    int TotalAttempted,
    int TotalPassedCas,
    int TotalDropped,
    string StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
```

### 2. Selector resolver

- `seed` → `QuestionReadModel` where `CreatedBy == "System"` OR `SourceType ∈ { "seed" }`
- `bagrut` → rows whose `SourceType == "bagrut-reference"` or concept under a bagrut leaf
- `concept:X` → `Concepts` contains `X`
- `all` → every row with status `Published` (never variants-of-variants)

### 3. Planner

Reads `ContentCoverageService` once. For each resolved source, computes its leaf via the taxonomy + the source's concept ids. Skips sources whose leaf already has ≥ `StopAfterLeafFull` items. Enumerates difficulty bands per source. Stops planning once the cumulative `Count` sum would exceed `MaxTotalCandidates`.

### 4. Runner (skipped in dry-run)

For each planned work-item, call `GenerateSimilarHandler.RunCoreAsync` (new — returns the `BatchGenerateResponse` directly without the IResult wrapper). Aggregate per-source outcomes. Continue on errors — one source failing must not abort the run.

### 5. Event

`CorpusExpansionRun_V1 { RunId, Selector, SourceCount, TotalAttempted, TotalPassedCas, TotalDropped, StartedBy, StartedAt, CompletedAt, DryRun }` — append to a system stream `corpus-expansion:{runId}`.

### 6. Endpoint

`POST /api/admin/questions/expand-corpus` — **SuperAdminOnly** + `ai` rate-limit. Default body is `dryRun: true`; operators must pass `dryRun: false` to spend.

### 7. Admin UI

`CorpusExpanderDialog.vue` — selector combobox, difficulty-band rows (add/remove), budget input, dry-run toggle, two-phase flow (plan → confirm → run). Renders per-source outcomes as a table with CAS drop counts.

### 8. Tests

Handler-level (NSubstitute): 10+
- Selector shapes (seed / bagrut / concept:X / all / invalid)
- Planner skips full leaves
- MaxTotalCandidates caps planning
- DryRun returns plan without LLM calls
- Wet run invokes RunCoreAsync per source
- Error in one source doesn't abort others
- CAS-drop counts aggregate

## Files

- New: `src/actors/Cena.Actors/Events/QuestionRecreationEvents.cs` (add `CorpusExpansionRun_V1`)
- Edit: `src/api/Cena.Admin.Api/Questions/GenerateSimilarHandler.cs` — expose `RunCoreAsync`
- New: `src/api/Cena.Admin.Api/Questions/CorpusExpanderHandler.cs`
- New: `src/api/Cena.Admin.Api/Questions/CorpusExpanderEndpoints.cs`
- Edit: `Registration/CenaAdminServiceRegistration.cs` (map endpoint)
- New: `src/api/Cena.Admin.Api.Tests/Questions/CorpusExpanderHandlerTests.cs`
- New: `src/admin/full-version/src/views/apps/questions/CorpusExpanderDialog.vue`
- Edit: `src/admin/full-version/src/pages/apps/questions/list/index.vue` — "Populate bank" header button

## Acceptance Criteria

- [ ] Dry-run returns a plan without invoking any LLM call
- [ ] Wet run respects `MaxTotalCandidates`
- [ ] SuperAdminOnly gate enforced
- [ ] `CorpusExpansionRun_V1` emitted once per run
- [ ] Full handler test suite passes
- [ ] `Cena.Actors.sln` builds with 0 errors
- [ ] Admin UI "Populate bank" button opens the dialog + shows plan + runs

## Coordination notes

- NEVER bypass the CAS gate — every child flows through `BatchGenerateAsync` which runs the gate.
- Provenance: each child gets the standard creation event via `CasGatedQuestionPersister`; the parent stream ALSO gets the existing `QuestionSimilarGenerated_V1` (via `GenerateSimilarHandler.RunCoreAsync`).
- `CorpusExpansionRun_V1` is a separate operator event — audits the batch, not individual children.
