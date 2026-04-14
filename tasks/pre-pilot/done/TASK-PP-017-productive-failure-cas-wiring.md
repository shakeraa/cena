# PP-017: Wire Exploratory Scaffolding Level into CAS Response Messaging

- **Priority**: Low — pedagogical enhancement, not a correctness issue
- **Complexity**: Senior engineer — scaffolding context threading through CAS flow
- **Source**: Expert panel review § CAS Engine (Dr. Nadia)

## Problem

SCAFFOLD-001 added an `Exploratory = 3` level to `StepScaffoldingLevel` in `StepSolverQuestionDocument.cs`. Per Kapur (2016), productive failure at the Exploratory level should celebrate valid non-canonical approaches rather than just labeling them as divergences.

Currently, `StepVerifierService.VerifyStepAsync` returns `DivergenceDescription: "Valid but non-canonical approach"` for valid-but-different steps — this is the same message regardless of scaffolding level. At the Exploratory level, the message should be encouraging (e.g., "Great approach! This is different from the standard method — keep going to see if it leads to the answer.") rather than neutral.

## Scope

1. Thread the current `StepScaffoldingLevel` through to `StepVerifierService` (add parameter to `VerifyStepAsync` or use a context object)
2. At Exploratory level, when a step is valid but non-canonical:
   - `DivergenceDescription` should use encouraging language
   - `SuggestedNextStep` should be null (let the student explore)
   - A new boolean `IsProductiveFailurePath` should be set to true
3. The frontend `StepSolverCard.vue` should render a distinct UI treatment for productive failure paths (e.g., a green "exploring!" badge instead of a yellow "different approach" warning)

## Files to Modify

- `src/actors/Cena.Actors/Cas/StepVerifierService.cs` — accept scaffolding level, vary messaging
- `src/student/full-version/src/components/session/StepInput.vue` — render productive failure UI
- `src/actors/Cena.Actors/Cas/StepVerifierService.cs` — add `IsProductiveFailurePath` to result

## Acceptance Criteria

- [ ] At Exploratory level, valid non-canonical steps get encouraging messaging
- [ ] At Full/Faded/Minimal levels, behavior is unchanged
- [ ] Frontend shows distinct visual treatment for productive failure exploration
- [ ] No CAS verification behavior changes — only the messaging layer
