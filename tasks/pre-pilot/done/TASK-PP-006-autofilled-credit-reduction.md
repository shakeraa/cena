# PP-006: Reduce AutoFilled Assistance Credit from 0.25 to 0.05

- **Priority**: High — inflated mastery estimates mislead students and teachers
- **Complexity**: Senior engineer — single constant change with cascading test updates
- **Source**: Expert panel review § BKT+ & Mastery (Dr. Nadia)

## Problem

`BktPlusCalculator` in `src/actors/Cena.Actors/Services/BktPlusCalculator.cs:115` defines assistance credit multipliers as:

```csharp
private static readonly double[] AssistanceCreditMultipliers = [1.0, 0.75, 0.50, 0.25];
```

The `AutoFilled = 3` level (answer was fully revealed or step was auto-completed) receives 0.25 credit. This is too generous — when the answer is fully revealed, the student performed no cognitive work. Heffernan & Heffernan (2014, ASSISTments platform) demonstrated that assistance credit at this level inflates mastery estimates for students who game the hint ladder by clicking through hints rapidly to extract the answer.

## Research Basis

- Heffernan & Heffernan (2014): ASSISTments found that students who game hints (click-click-click to reveal answer) show P(known) inflation of up to 0.15 when given 0.25 credit for auto-filled responses
- Baker et al. (2008): "Gaming the system" behavior accounts for 10-20% of interactions in adaptive learning platforms
- The correct signal: auto-fill/reveal tells us the student did NOT know the answer — it should barely move the mastery needle

## Scope

1. Change `AssistanceCreditMultipliers[3]` from `0.25` to `0.05`
2. Optionally add a `Gaming = 4` assistance level with `0.0` credit for when the gaming detector identifies rapid hint-clicking behavior (future enhancement, not required for this task)
3. Update all unit tests in `BktPlusCalculatorTests.cs` that depend on the AutoFilled multiplier
4. Document the change in the test file with the Heffernan citation

## Files to Modify

- `src/actors/Cena.Actors/Services/BktPlusCalculator.cs` — change multiplier at index 3
- `src/actors/Cena.Actors.Tests/Services/BktPlusCalculatorTests.cs` — update expected values

## Acceptance Criteria

- [ ] `AssistanceCreditMultipliers` is `[1.0, 0.75, 0.50, 0.05]`
- [ ] All existing BKT+ tests pass with updated expected values
- [ ] A new test verifies that AutoFilled barely moves mastery: starting at P=0.50, after one AutoFilled correct, P should increase by less than 0.02
- [ ] Comment in code cites Heffernan & Heffernan (2014) as rationale
