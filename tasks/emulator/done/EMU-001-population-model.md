# EMU-001: Student Population Model — 1,000 Students, 8 Archetypes, Demographic Realism

**Priority:** P0 — foundation for all other emulator tasks
**Blocked by:** None
**Estimated effort:** 2 days
**Contract:** `src/actors/Cena.Actors/Simulation/MasterySimulator.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.**

## Context

The current emulator generates 300 students with 8 archetypes and replays their entire 60-day history at startup. This creates an unrealistic burst that overwhelms the actor system. The new population model generates 1,000 students with realistic demographics, school assignments, and study habit profiles.

## Subtasks

### EMU-001.1: Student Profile Generator

**Files to create/modify:**
- `src/emulator/Population/StudentProfileGenerator.cs`

**Acceptance:**
- [ ] Generate 1,000 unique student profiles with:
  - Student ID: `emu-{archetype_short}-{seq:000}` (e.g., `emu-genius-042`)
  - Archetype: 8 types distributed realistically:
    - Genius (3%), HighAchiever (12%), SteadyLearner (35%), Struggling (20%), FastCareless (10%), SlowThorough (8%), Inconsistent (10%), VeryLowCognitive (2%)
  - Depth unit: 3-unit (20%), 4-unit (35%), 5-unit (45%) — weighted by archetype
  - Language: Hebrew (70%), Arabic (30%)
  - School: Distributed across 5 schools (`school-alpha` through `school-epsilon`)
  - Study habits profile: `{ avgSessionMinutes, sessionsPerDay, preferredHours, weekendMultiplier }`
  - Bagrut exam date: all students target same exam session
- [ ] Deterministic generation (seeded RNG) — same seed = same population
- [ ] Profiles stored as in-memory list, not persisted to DB

**Test:**
```csharp
[Fact]
public void Generator_Produces1000Students()
{
    var students = StudentProfileGenerator.Generate(1000, seed: 42);
    Assert.Equal(1000, students.Count);
    Assert.True(students.Select(s => s.StudentId).Distinct().Count() == 1000);
}

[Fact]
public void Generator_ArchetypeDistribution()
{
    var students = StudentProfileGenerator.Generate(1000, seed: 42);
    var geniusCount = students.Count(s => s.Archetype == "Genius");
    Assert.InRange(geniusCount, 20, 40); // ~3% of 1000
}
```

---

### EMU-001.2: Study Habit Profiles

**Files to create/modify:**
- `src/emulator/Population/StudyHabitProfile.cs`

**Acceptance:**
- [ ] Each archetype has a characteristic study pattern:

| Archetype | Avg Session (min) | Sessions/Day | Peak Hours | Weekend Factor |
|---|---|---|---|---|
| Genius | 20-30 | 1-2 | 21:00-23:00 | 0.5x |
| HighAchiever | 30-45 | 2-3 | 16:00-20:00 | 1.2x |
| SteadyLearner | 25-35 | 1-2 | 17:00-21:00 | 0.8x |
| Struggling | 15-25 | 1 | 18:00-20:00 | 0.3x |
| FastCareless | 10-15 | 2-3 | scattered | 0.5x |
| SlowThorough | 40-60 | 1 | 15:00-19:00 | 1.5x |
| Inconsistent | 5-45 (high variance) | 0-3 | random | 0.2-2.0x |
| VeryLowCognitive | 10-20 | 0-1 | 18:00-19:00 | 0.1x |

- [ ] Session duration has per-student variance (±30% of archetype mean)
- [ ] Focus degradation rate varies by archetype (Genius: slow, Struggling: fast)
