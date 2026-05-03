# RDY-039: CAS Persister — Transactional Boundary With Caller Session (Fix #2)

- **Priority**: **Critical / ship-blocker** — data-integrity under crash
- **Complexity**: Senior engineer + Marten session familiarity
- **Source**: Senior-architect review of `claude-code/cas-gate-residuals` (2026-04-15)
- **Tier**: 1
- **Effort**: 4-6 hours
- **Dependencies**: RDY-037 (merged)

## Problem

`CasGatedQuestionPersister.PersistAsync` ([src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs:165](../../src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs#L165)) opens its own `_store.LightweightSession()` and `SaveChangesAsync` before returning.

`IngestionOrchestrator.ProcessFileAsync` ([src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs:294](../../src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs#L294)) calls the persister inside its own pipeline loop and then does `session.Store(pipelineItem)` + `session.SaveChangesAsync(ct)` at [line 330-331](../../src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs#L330) in a **separate** session.

A crash between the two saves leaves orphan question streams + bindings with a pipeline item still in "processing". ADR-0032 §16.1 advertises "atomic storage … in the same session/transaction" — false for this caller.

## Scope

### 1. Add session-aware overload

Extend `ICasGatedQuestionPersister`:

```csharp
Task<GatedPersistOutcome> PersistAsync(
    IDocumentSession session,              // NEW — caller-owned session
    string questionId,
    object creationEvent,
    GatedPersistContext context,
    IReadOnlyList<object>? extraEventsOnNewStream = null,
    IReadOnlyList<object>? companionDocuments = null,
    CasGateResult? preComputedGateResult = null,
    CancellationToken ct = default);
```

- The persister appends events + stores binding + stores companions on the caller's session
- Caller owns `SaveChangesAsync` — no nested transactions
- Keep the old (session-less) overload as a thin wrapper that opens its own session for backward compatibility with `QuestionBankService` / `QuestionBankSeedData`

### 2. Rewire `IngestionOrchestrator`

- Pass the existing `session` into the persister
- Remove the persister's implicit save
- `SaveChangesAsync` at [line 331](../../src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs#L331) now commits questions + bindings + pipeline item atomically

### 3. Integration test — crash-safety

New `PersisterCrashSafetyTests`:
- Stub `IDocumentSession` to throw on save after the persister call
- Assert no `QuestionState` stream is committed when the pipeline save fails
- Assert no `QuestionCasBinding` doc is committed

### 4. Update ADR-0032 §16.1

Replace "atomic storage … in the same session/transaction" with the precise contract:
- Session-less overload: atomic *within the persister*; caller-composable writes require the session-aware overload
- Session-aware overload: atomic with the caller's unit-of-work

## Acceptance Criteria

- [ ] `ICasGatedQuestionPersister` has a session-aware overload
- [ ] `IngestionOrchestrator` uses the session-aware overload; question + binding + pipeline item commit or rollback together
- [ ] `PersisterCrashSafetyTests` proves rollback semantics
- [ ] ADR-0032 §16.1 updated with the precise atomicity contract
- [ ] No regression in existing CAS tests
- [ ] Full sln builds green
