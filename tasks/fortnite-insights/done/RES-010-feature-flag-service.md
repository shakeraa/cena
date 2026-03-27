# RES-010: Global Feature Flag Service

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P3 -- Nice to have                           |
| **Effort**    | Medium (4-6 hours)                           |
| **Impact**    | Low now, enables A/B testing and safe rollouts|
| **Origin**    | Fortnite's "Lightswitch" service: system-wide feature availability control |
| **Status**    | DONE                                         |
| **Execution** | See [EXECUTION.md](EXECUTION.md#res-010-feature-flag-service--p3) |

---

## Problem

Cena's `MethodologySwitchService` is per-student. There's no system-wide mechanism to:
- Enable/disable LLM models globally (e.g., "Opus is having issues, disable for all")
- A/B test pedagogy approaches across student cohorts
- Gradually roll out new features (canary release)
- Kill-switch for experimental features

## Design

### FeatureFlagActor (Singleton)

```csharp
public sealed class FeatureFlagActor : IActor
{
    private Dictionary<string, FeatureFlag> _flags = new();

    // Messages
    public sealed record GetFlag(string FlagName);
    public sealed record SetFlag(string FlagName, bool Enabled, double RolloutPercent = 100.0);
    public sealed record GetAllFlags;
}

public sealed record FeatureFlag(
    string Name,
    bool Enabled,
    double RolloutPercent,  // 0-100, for gradual rollout
    DateTimeOffset UpdatedAt);
```

### Predefined Flags

| Flag                        | Default | Description                           |
|-----------------------------|---------|---------------------------------------|
| `llm.kimi.enabled`         | true    | Enable Kimi model for new sessions    |
| `llm.sonnet.enabled`       | true    | Enable Sonnet model                   |
| `llm.opus.enabled`         | true    | Enable Opus model                     |
| `pedagogy.socratic`        | true    | Enable Socratic methodology           |
| `pedagogy.scaffolding`     | true    | Enable scaffolding methodology        |
| `session.max_minutes`      | 45      | Max session duration                  |
| `outreach.enabled`         | true    | Enable proactive outreach             |
| `experimental.adaptive_difficulty` | false | New adaptive difficulty system |

### Storage

Simple: PostgreSQL table (or even a JSON file for v1). Not worth a separate service at current scale.

```sql
CREATE TABLE cena_feature_flags (
    name        TEXT PRIMARY KEY,
    enabled     BOOLEAN NOT NULL DEFAULT true,
    rollout_pct DOUBLE PRECISION NOT NULL DEFAULT 100.0,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

### Usage in Actors

```csharp
// In LearningSessionActor:
var flagResponse = await context.RequestAsync<FlagResponse>(
    _featureFlagPid, new GetFlag("llm.kimi.enabled"));
if (!flagResponse.Enabled)
    // Skip Kimi, use next model in tier
```

## Affected Files

- New: `src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs`
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` -- check flags for LLM selection
- `src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs` -- check outreach flag
- `src/actors/Cena.Actors.Host/Program.cs` -- register singleton

## Acceptance Criteria

- [ ] `FeatureFlagActor` singleton with get/set/list operations
- [ ] Flags persisted to PostgreSQL (survives restart)
- [ ] `GetFlag` request/response works from any actor
- [ ] Rollout percentage: hash student ID, enable for % of students
- [ ] Admin API endpoint to toggle flags at runtime
- [ ] Unit test: flag disabled → feature skipped
- [ ] Unit test: 50% rollout → approximately half of students get feature
