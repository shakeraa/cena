# RES-007: Profile Multiplexing Per Subject

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P2 -- Architecture improvement               |
| **Effort**    | Medium (6-8 hours)                           |
| **Impact**    | Medium -- reduces aggregate bloat, enables per-subject analytics |
| **Origin**    | Fortnite's multi-profile pattern: `athena`, `campaign`, `common_core` per player |
| **Status**    | TODO                                         |
| **Execution** | See [EXECUTION.md](EXECUTION.md#res-007-profile-multiplexing--p2-breaking-change) |

---

## Problem

`StudentState` currently holds all mastery data, BKT parameters, and session history for all subjects in a single aggregate. As students study multiple subjects, this aggregate grows unbounded. Fortnite solved this with per-game-mode profiles.

## Design

### Profile Hierarchy

```
StudentActor (aggregate root)
  ├── common profile:
  │     engagement metrics, fatigue model, preferences, contact info
  ├── math profile:
  │     BKT params per math concept, mastery levels, session history
  ├── science profile:
  │     BKT params per science concept, mastery levels, session history
  └── english profile:
        BKT params per english concept, mastery levels, session history
```

### Event Stream Structure

Option A -- **Sub-streams in Marten:**
```
student-{id}           → common profile events (registration, preferences, engagement)
student-{id}-math      → math-specific events (attempts, mastery changes)
student-{id}-science   → science-specific events
```

Option B -- **Single stream, tagged events:**
```
student-{id} → all events tagged with subject
  ConceptAttempted { subject: "math", conceptId: "fractions", ... }
  MasteryChanged   { subject: "science", conceptId: "photosynthesis", ... }
```

**Recommendation:** Option A (sub-streams). Matches Fortnite's clean separation, allows independent snapshotting per subject, and means loading the math profile doesn't require replaying science events.

### StudentState Refactor

```csharp
public sealed class StudentState
{
    public CommonProfile Common { get; init; } = new();
    public Dictionary<string, SubjectProfile> Subjects { get; init; } = new();
}

public sealed class CommonProfile
{
    public string Name { get; set; } = "";
    public double FatigueBaseline { get; set; }
    public string PreferredMethodology { get; set; } = "socratic";
    // ... engagement, contact, preferences
}

public sealed class SubjectProfile
{
    public string Subject { get; set; } = "";
    public Dictionary<string, double> BktMastery { get; set; } = new();
    public int TotalAttempts { get; set; }
    public int TotalCorrect { get; set; }
    public DateTimeOffset LastSessionAt { get; set; }
    // ... per-subject state
}
```

## Affected Files

- `src/actors/Cena.Actors/Students/StudentState.cs` -- refactor to profile structure
- `src/actors/Cena.Actors/Students/StudentActor.cs` -- load/save per-profile streams
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` -- pass subject profile
- `src/actors/Cena.Actors/Events/` -- tag events with subject

## Acceptance Criteria

- [ ] `StudentState` split into `CommonProfile` + `Dictionary<string, SubjectProfile>`
- [ ] Event streams separated by subject (Option A)
- [ ] Activation loads only common + requested subject profile (lazy loading)
- [ ] Snapshot per profile stream independently
- [ ] Migration path from current single-stream to multi-stream
- [ ] Unit test: load math profile without loading science events
