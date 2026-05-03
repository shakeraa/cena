# TASK-PRR-252: Wire `ICasGatedQuestionPersister` into Student API + add endpoint auth gate

**Priority**: P0 — HIGH gate for PRR-245 reference library variant route
**Effort**: S (1-2 days)
**Lens consensus**: claude-1 PRR-250 finding §6
**Source docs**: [PRR-250 findings](reviews/PRR-250-verification-sweep-findings.md), [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md)
**Assignee hint**: backend (claude-code or kimi-coder)
**Tags**: source=prr-250-finding,epic=epic-prr-n,priority=p0,backend,di,auth
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

`ICasGatedQuestionPersister` is registered only in `Cena.Admin.Api`'s DI today. Student-API (`Cena.Student.Api.Host`) has no registration, which means PRR-245's variant-generation endpoint cannot reach the persister to save CAS-verified variants. Additionally the persister has no role gate — admin-side it relies on the admin-policy middleware; student-side it must enforce auth at the endpoint layer.

This task wires the persister into student-api with a documented auth-gate contract.

## Scope

1. **Register `ICasGatedQuestionPersister`** in `Cena.Student.Api.Host.Program.cs` DI container with the same lifetime as the admin-side registration. Match implementation type unless a student-specific impl is justified (it isn't, today).
2. **Define `IStudentCasPersistContext`** — a lightweight context carrier capturing:
   - `studentId` (the calling student)
   - `sourceProvenance: Provenance?` (the source's provenance — non-null when the variant lineages back to a `MinistryBagrut` corpus item)
   - `sourceShailonCode + questionIndex` (lineage fields per ADR-0059 §6)
   - `variationKind: "parametric" | "structural"` (per ADR-0059 §5)
3. **Endpoint-layer auth contract**: the variant-generation endpoint (PRR-245 §Backend item 4) MUST call `ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId)` BEFORE invoking `ICasGatedQuestionPersister`. The persister itself does NOT check role/identity — that's an endpoint responsibility per the existing admin-side pattern.
4. **Idempotency keying**: extend persister to accept an idempotency key derived from `{sourceShailonCode, questionIndex, variationKind, parametricSeed?}` so two students requesting the same variant return the same persisted document (cost-amortizing dedup per ADR-0059 §5).
5. **Tests**:
   - DI smoke test: `Cena.Student.Api.Host` resolves `ICasGatedQuestionPersister` without throwing.
   - Endpoint test: variant request without student auth returns 401; with auth returns 200 + persisted document; second call with same source returns same `variantQuestionId` (idempotency).
   - Negative test: persister called with `sourceProvenance.Kind == MinistryBagrut` directly (not through `Reference<T>`) is allowed (the variant is the recreation, not the source); but a deliverable-side path that wraps `MinistryBagrut` in `Deliverable<T>` still throws (ADR-0043 enforcement unchanged).

## Files

### Modified
- `src/api/Cena.Student.Api.Host/Program.cs` — DI registration
- `src/actors/Cena.Actors/Persistence/CasGatedQuestionPersister.cs` (or wherever the impl lives) — add idempotency-key parameter

### New
- `src/actors/Cena.Actors/Persistence/IStudentCasPersistContext.cs` — student-side context carrier
- `src/api/Cena.Student.Api.Tests/Persistence/CasGatedQuestionPersisterDiTests.cs`

## Definition of Done

- `Cena.Student.Api.Host` DI container resolves `ICasGatedQuestionPersister`.
- Idempotency key working — repeated identical requests return same variant document.
- Endpoint auth gate documented + tested.
- Full `Cena.Actors.sln` build green.

## Blocking

- None.

## Non-negotiable references

- Memory "No stubs — production grade"
- ADR-0059 §5 + §6
- ADR-0043 (the `Deliverable<T>` ban remains; persister handles `Reference<T>`-wrapped sources via lineage fields, not direct delivery)

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + DI smoke test sha>"`
