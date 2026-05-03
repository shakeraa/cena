# PP-010: Skill-Category-Specific Half-Life for BKT+ Forgetting Curve

- **Priority**: Medium — improves mastery accuracy for mixed skill types
- **Complexity**: Senior engineer — data-driven constant with per-skill metadata
- **Source**: Expert panel review § BKT+ & Mastery (Dr. Yael)

## Problem

`BktPlusCalculator` in `src/actors/Cena.Actors/Services/BktPlusCalculator.cs` uses a global default half-life of 14 days for all skills:

```csharp
public const double DefaultHalfLifeDays = 14.0;
```

In psychometric research, different skill categories have different forgetting rates:
- **Procedural skills** (polynomial factoring, matrix operations, trig identities): decay faster, half-life ~7 days (Bahrick & Hall 1991)
- **Conceptual skills** (understanding what a function is, interpreting graphs): decay slower, half-life ~21 days
- **Meta-cognitive skills** (knowing when to apply which technique): decay slowest, half-life ~30 days

A global 14-day half-life overestimates retention for procedural skills (students think they remember factoring when they don't) and underestimates retention for conceptual skills (triggering unnecessary refresh sessions).

## Scope

### 1. Add SkillCategory to skill metadata

```csharp
public enum SkillCategory
{
    Procedural,   // half-life: 7 days
    Conceptual,   // half-life: 21 days
    MetaCognitive, // half-life: 30 days
    Mixed          // half-life: 14 days (default)
}
```

### 2. Map default half-life from category

```csharp
public static double DefaultHalfLifeForCategory(SkillCategory category) => category switch
{
    SkillCategory.Procedural => 7.0,
    SkillCategory.Conceptual => 21.0,
    SkillCategory.MetaCognitive => 30.0,
    _ => 14.0
};
```

### 3. Use category in SkillMasteryState initialization

When creating a new `SkillMasteryState` for a skill the student has never attempted, initialize `HalfLifeDays` from the skill's category rather than the global constant.

### 4. Annotate prerequisite JSON files

Add `"category": "procedural"` (or conceptual/metacognitive) to each skill entry in the prerequisite JSON files:
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-036.json`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-806.json`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-807.json`

## Files to Modify

- `src/actors/Cena.Actors/Services/BktPlusCalculator.cs` — add SkillCategory enum, category-based default
- `src/actors/Cena.Actors/Services/SkillPrerequisiteGraph.cs` — expose category metadata
- `src/actors/Cena.Actors/Services/Prerequisites/*.json` — annotate with category
- `src/actors/Cena.Actors.Tests/Services/BktPlusCalculatorTests.cs` — test category-specific half-lives

## Acceptance Criteria

- [ ] `SkillCategory` enum exists with Procedural (7d), Conceptual (21d), MetaCognitive (30d), Mixed (14d)
- [ ] New `SkillMasteryState` instances use category-specific half-life
- [ ] Existing states with explicitly set half-lives are not affected (the half-life adjusts from practice)
- [ ] All three prerequisite JSON files annotated with skill categories
- [ ] Test: procedural skill decays to 50% mastery in 7 days, conceptual in 21 days
