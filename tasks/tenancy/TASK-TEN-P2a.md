# TASK-TEN-P2a: Mastery State Re-Key per ADR-0002

**Phase**: 2
**Priority**: high
**Effort**: 3--5d
**Depends on**: TEN-VERIFY-0001
**Blocks**: TEN-P2f
**Queue ID**: `t_08f268d584e8`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Re-key every student's mastery state (BKT posterior, Elo rating, HLR half-life) according to the model locked in ADR-0002. This is the most data-sensitive migration in the tenancy bundle -- it touches every `ConceptMasteryMap` entry in every `StudentProfileSnapshot`.

## Background

ADR-0001 Decision 2 determines how mastery state is keyed across enrollments. This task implements whichever model ADR-0002 locks: Model A (shared `conceptId` key), Model B (isolated `(enrollmentId, conceptId)` key), or Model C (seeded-but-divergent hybrid). The implementation path differs materially for each.

## Specification

### Model A implementation path (if ADR-0002 locks shared)

- No re-key needed. `ConceptMasteryMap` stays keyed by `conceptId`.
- Add a `TrackConceptOverlapIndex` projection that maps which concepts are shared across tracks (for analytics only).
- Update `BktService` and `EloDifficultyService` to log a warning when a concept appears in multiple enrollments (monitoring, not blocking).

### Model B implementation path (if ADR-0002 locks isolated)

- Re-key `ConceptMasteryMap` from `Dictionary<string, ConceptMastery>` to `Dictionary<string, ConceptMastery>` where the key becomes `$"{enrollmentId}:{conceptId}"`.
- Add a Marten event upcaster that transforms existing keys: for every existing `conceptId` key, prefix with `$"enroll-default-{studentId}:"`.
- Update `BktService.UpdateMastery` to accept `enrollmentId` as a parameter.
- Update `EloDifficultyService` to scope Elo updates to the enrollment.
- Update `HlrService` to scope half-life records to the enrollment.
- Cold-start for new enrollments: `PriorMastery = 0.3` (default prior).

### Model C implementation path (if ADR-0002 locks seeded-divergent)

- Re-key same as Model B: `$"{enrollmentId}:{conceptId}"`.
- On `EnrollmentCreated_V1` for a student who already has mastery data: for each concept in the new enrollment's track that overlaps with an existing enrollment, seed the new entry:
  ```
  newPMastery = transferWeight * existingPMastery + (1 - transferWeight) * prior
  newElo      = transferWeight * existingElo      + (1 - transferWeight) * 1500.0
  newHalfLife = existingHalfLife  // HLR seeds from best existing data
  ```
- `transferWeight` comes from ADR-0002 (literature-derived default, e.g. 0.8 for near-identical concepts, 0.3 for exam-strategy concepts).
- Add `ConceptTransferWeightDocument` in `src/shared/Cena.Infrastructure/Documents/` if weights are per-concept.

### Common to all models

- Add a `MasteryKeyMigration` step in `DatabaseSeeder` (or as an async projection rebuild) that transforms existing snapshots.
- The migration must be idempotent and reversible (log the before/after state).

## Implementation notes

- This task CANNOT start until VERIFY-0001 completes and ADR-0002 is locked. The spec above covers all three paths so the implementer can read ahead, but only one path will execute.
- Follow FIND-data-007 CQRS pattern: mastery state changes go through events, not direct snapshot writes.
- Follow the existing dual-Elo update pattern in `EloDifficultyService.cs` -- student Elo and question Elo update on the same session.
- The re-key migration is the single highest-risk operation. It must include a rollback script.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. The migration must be tested against a snapshot with 300+ students (the simulation seed). Follow FIND-data-007 CQRS purity. Follow FIND-sec-005 tenant scoping -- mastery state must never leak across enrollments in Models B and C.

## Tests required

**Test class**: `MasteryReKeyTests` in `src/actors/Cena.Actors.Tests/Mastery/MasteryReKeyTests.cs`

| Test method | Assertion |
|---|---|
| `ExistingStudent_SingleEnrollment_MasteryPreserved` | After re-key, student's mastery values are identical (no data loss). |
| `ExistingStudent_ReKey_IsIdempotent` | Run re-key twice, assert state identical both times. |
| `NewEnrollment_SharedConcept_ModelA_SameState` | (Model A only) New enrollment reads same mastery as old. |
| `NewEnrollment_SharedConcept_ModelB_ColdStart` | (Model B only) New enrollment starts at prior, not existing mastery. |
| `NewEnrollment_SharedConcept_ModelC_SeededState` | (Model C only) New enrollment starts at weighted blend of existing + prior. |
| `CrossEnrollment_MasteryIsolation_ModelB` | (Model B only) Attempt in enrollment A does not affect enrollment B's mastery. |
| `CrossEnrollment_MasteryIsolation_ModelC` | (Model C only) After seeding, subsequent attempts diverge independently. |

**Regression test class**: `MasteryReKeyRegressionTests` (two-track regression)

| Test method | Assertion |
|---|---|
| `TwoTrack_BagrutAndSAT_SharedLinearEquations` | Student enrolled in both bagrut-5 and SAT. Verify `linear-equations` mastery behaves per ADR-0002 model. |
| `TwoTrack_SATAndPsychometry_StrategyConceptsDiverge` | Verify exam-strategy concepts (e.g. `sat-mc-strategy`) are isolated even in Model A. |

## Definition of Done

- [ ] Mastery state re-keyed per ADR-0002 locked model
- [ ] `BktService`, `EloDifficultyService`, `HlrService` updated for new key format
- [ ] Migration step added (idempotent, reversible)
- [ ] All 7+ model-specific tests pass
- [ ] Two-track regression tests pass
- [ ] Existing mastery tests still pass (no regressions)
- [ ] 300-student simulation seed produces correct post-migration state
- [ ] `dotnet build` succeeds with zero warnings

## Files to read first

1. `docs/adr/0002-mastery-sharing-model.md` -- the locked decision (from VERIFY-0001)
2. `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` -- `ConceptMasteryMap`
3. `src/actors/Cena.Actors/Services/EloDifficultyService.cs` -- dual Elo pattern
4. `src/actors/Cena.Actors/Mastery/BktService.cs` -- BKT update path
5. `docs/adr/0001-multi-institute-enrollment.md` -- Decision 2 models

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` | modify | Re-key `ConceptMasteryMap` |
| `src/actors/Cena.Actors/Mastery/BktService.cs` | modify | Accept enrollment-scoped key |
| `src/actors/Cena.Actors/Services/EloDifficultyService.cs` | modify | Enrollment-scoped Elo |
| `src/shared/Cena.Infrastructure/Documents/ConceptTransferWeightDocument.cs` | create (Model C only) | Transfer weights |
| `src/actors/Cena.Actors.Tests/Mastery/MasteryReKeyTests.cs` | create | 7+ tests |
| `src/actors/Cena.Actors.Tests/Mastery/MasteryReKeyRegressionTests.cs` | create | 2 regression tests |
