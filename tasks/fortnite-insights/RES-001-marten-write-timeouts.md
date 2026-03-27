# RES-001: Add Marten Write Timeouts

| Field         | Value                                     |
|---------------|-------------------------------------------|
| **Priority**  | P0 -- Immediate                           |
| **Effort**    | Low (1-2 hours)                           |
| **Impact**    | High -- prevents actor mailbox starvation |
| **Origin**    | Fortnite Feb 2018 outage: 40s DB calls blocked MCP threads, cascaded system-wide |
| **Status**    | TODO                                      |

---

## Problem

`StudentActor` persists events to Marten via `session.SaveChangesAsync()` with no explicit timeout. If PostgreSQL becomes slow under load, the actor's mailbox blocks indefinitely -- exactly what killed Fortnite's MCP at 3.4M CCU.

## Affected Files

- `src/actors/Cena.Actors/Students/StudentActor.cs` -- all `SaveChangesAsync` calls
- Any other actor that writes to Marten directly

## Implementation

### 1. Add a timeout constant to StudentActor

```csharp
private static readonly TimeSpan EventPersistTimeout = TimeSpan.FromMilliseconds(2000);
```

### 2. Wrap all Marten writes with cancellation

```csharp
using var cts = new CancellationTokenSource(EventPersistTimeout);
try
{
    await session.SaveChangesAsync(cts.Token);
}
catch (OperationCanceledException)
{
    _logger.LogError("Event persist timed out for student {StudentId} after {Timeout}ms",
        _studentId, EventPersistTimeout.TotalMilliseconds);
    throw; // Let supervision strategy restart the actor
}
```

### 3. Record timeout metric

```csharp
private static readonly Counter<long> PersistTimeoutCounter =
    MeterInstance.CreateCounter<long>("cena.student.persist_timeout_total",
        description: "Event persistence timeouts");
```

## Acceptance Criteria

- [ ] All `SaveChangesAsync` calls in `StudentActor` have a 2s cancellation token
- [ ] `EventPersistLatency` histogram records actual persist time
- [ ] `PersistTimeoutCounter` increments on timeout
- [ ] Supervision strategy restarts the actor on `OperationCanceledException`
- [ ] Unit test: mock slow Marten session, verify timeout fires within 2.5s

## Why This Matters

Fortnite's MCP had unbounded DB writes. When MongoDB update times hit 40,000ms, threads blocked forever, cascading into total system failure for 3.4M concurrent users. A 2s timeout would have limited the blast radius to individual actors that could be restarted by supervision.
