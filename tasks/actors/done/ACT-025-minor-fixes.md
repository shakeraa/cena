# ACT-025: Minor Fixes — Hot Path LINQ, Supervision Comment, Snapshot Mutability

**Priority:** P3 — low-severity cleanups
**Blocked by:** None
**Estimated effort:** 0.5 days
**Source:** Actor system review L2, L3, M5

---

## Subtasks

### ACT-025.1: Replace LINQ on Hot Path in LearningSessionActor (L2)
**File:** `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` — ComputeFatigueScore method

**Problem:** `.Skip(count - 5).Average()` allocates an iterator on every question attempt.

**Acceptance:**
- [ ] Replace LINQ with a simple loop computing rolling average over last 5 items
- [ ] Zero allocations on the hot path
- [ ] Fatigue scores remain identical (behavioral equivalence test)

### ACT-025.2: Fix Misleading Supervision Comment (L3)
**File:** `src/actors/Cena.Actors/Infrastructure/CenaSupervisionStrategies.cs:21`

**Problem:** Comment says "Stops child after 3 consecutive failures" but Proto.Actor **escalates to parent** after `maxNrOfRetries`.

**Acceptance:**
- [ ] Comment updated to: "Restarts child on failure. After 3 restarts within 60s, escalates to parent supervisor."

### ACT-025.3: Internal Setters on ConceptMasteryState (M5)
**File:** `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs`

**Problem:** `ConceptMasteryState` has fully public `{ get; set; }` properties. External code can mutate snapshot state.

**Acceptance:**
- [ ] Change properties to `{ get; internal set; }` — Marten can still deserialize (same assembly)
- [ ] Verify Marten serialization still works with internal setters
- [ ] Build passes
