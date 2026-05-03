# REV-008: Fix Backend Critical Code Issues (async void, Memory Leak, Mock Data)

**Priority:** P1 -- HIGH (process crash risk, unbounded memory growth, lying dashboard data)
**Blocked by:** None
**Blocks:** None
**Estimated effort:** 1 day
**Source:** System Review 2026-03-28 -- Backend Senior (C2, C3, I2, I3, I8)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Five backend code issues need surgical fixes:

1. **`async void` in actor** -- `PublishMethodologyAlert` can crash the process on unobserved exceptions
2. **ConcurrentBag memory leak** -- `_recentErrors` in NatsBusRouter grows unbounded
3. **Redis connection fallback flaw** -- first ConnectionMultiplexer not disposed on failure
4. **EventStreamService mock data** -- admin dashboard shows fabricated events and DLQ
5. **Fabricated physics data** -- dashboard shows `Physics: masteryPct * 0.7f` instead of real data

## Subtasks

### REV-008.1: Fix async void in StudentActor.Methodology.cs

**File to modify:** `src/actors/Cena.Actors/Students/StudentActor.Methodology.cs` (line ~96)

```csharp
// BEFORE -- async void swallows exceptions, can crash process
private async void PublishMethodologyAlert(
    MethodologyLevel level, string levelId, MethodologyAssignment assignment)

// AFTER -- return Task, fire-and-forget with explicit error handling
private Task PublishMethodologyAlert(
    MethodologyLevel level, string levelId, MethodologyAssignment assignment)
```

At the call site in `CheckConfidenceThreshold`, use fire-and-forget with error capture:
```csharp
// Fire-and-forget but capture errors
_ = PublishMethodologyAlert(level, levelId, assignment)
    .ContinueWith(t =>
    {
        if (t.IsFaulted)
            _logger.LogError(t.Exception, "Failed to publish methodology alert for {Level}", level);
    }, TaskContinuationOptions.OnlyOnFaulted);
```

**Acceptance:**
- [ ] No `async void` methods exist in actor code (`grep -r "async void" src/actors/` returns zero results excluding test files)
- [ ] NATS publish failures are logged, not swallowed
- [ ] Process does not crash on NATS connection failure during alert publish

### REV-008.2: Fix ConcurrentBag Memory Leak in NatsBusRouter

**File to modify:** `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` (lines 291-297)

```csharp
// BEFORE -- ConcurrentBag grows unbounded
private readonly ConcurrentBag<ErrorEntry> _recentErrors = new();

// AFTER -- ConcurrentQueue with bounded size
private readonly ConcurrentQueue<ErrorEntry> _recentErrors = new();
private const int MaxRecentErrors = 250;

// In error recording method:
_recentErrors.Enqueue(new ErrorEntry(...));
while (_recentErrors.Count > MaxRecentErrors && _recentErrors.TryDequeue(out _)) { }
```

**Acceptance:**
- [ ] `_recentErrors` uses `ConcurrentQueue<ErrorEntry>`, not `ConcurrentBag`
- [ ] Collection never exceeds 250 entries
- [ ] Oldest entries are dropped when limit is reached
- [ ] No `ConcurrentBag` usage remains in NatsBusRouter

### REV-008.3: Fix Redis Connection Fallback Logic

**File to modify:** `src/actors/Cena.Actors.Host/Program.cs` (lines 80-99)

```csharp
// BEFORE -- first Connect() not disposed on failure, second may never connect
try {
    var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
    return multiplexer;
} catch {
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
}

// AFTER -- match Admin API Host pattern (single attempt with retry)
var options = ConfigurationOptions.Parse(redisConnectionString);
options.AbortOnConnectFail = false;
options.ConnectRetry = 3;
options.ConnectTimeout = 5000;
options.SyncTimeout = 3000;
var multiplexer = ConnectionMultiplexer.Connect(options);
return multiplexer;
```

**Acceptance:**
- [ ] Single `Connect()` call with `AbortOnConnectFail = false`
- [ ] No try/catch around connection creation (let DI handle registration failure)
- [ ] Pattern matches Admin API Host's Redis connection code
- [ ] Both hosts use identical Redis connection configuration approach

### REV-008.4: Replace EventStreamService Mock Data with Real Queries

**File to modify:** `src/api/Cena.Admin.Api/EventStreamService.cs`

**Replace** `GenerateMockEvents()` and `GenerateMockDlq()` with real Marten queries:

```csharp
public async Task<IReadOnlyList<DomainEvent>> GetRecentEventsAsync(int limit = 50)
{
    await using var session = _store.QuerySession();
    var events = await session.Events
        .QueryAllRawEvents()
        .OrderByDescending(e => e.Timestamp)
        .Take(limit)
        .ToListAsync();

    return events.Select(e => new DomainEvent
    {
        Id = e.Id,
        StreamId = e.StreamKey ?? e.StreamId.ToString(),
        EventType = e.EventTypeName,
        Timestamp = e.Timestamp,
        Data = e.Data
    }).ToList();
}

public async Task<IReadOnlyList<DeadLetterMessage>> GetDeadLetterQueueAsync(int limit = 50)
{
    await using var session = _store.QuerySession();
    return await session.Query<NatsOutboxDeadLetter>()
        .OrderByDescending(d => d.FailedAt)
        .Take(limit)
        .ToListAsync();
}
```

**Acceptance:**
- [ ] `GetRecentEventsAsync` returns real Marten events, not mock data
- [ ] `GetEventRatesAsync` computes from real event timestamps
- [ ] `GetDeadLetterQueueAsync` queries `NatsOutboxDeadLetter` documents
- [ ] No `GenerateMockEvents()` or `GenerateMockDlq()` methods remain
- [ ] No `#pragma warning disable CS1998` remains in the file
- [ ] Dashboard shows real event data after emulator run

### REV-008.5: Remove Fabricated Physics Data

**File to modify:** `src/api/Cena.Admin.Api/AdminDashboardService.cs` (line ~269)

```csharp
// BEFORE -- fabricated
Physics: MathF.Round(masteryPct * 0.7f, 1)

// AFTER -- only show subjects that have real data
// If physics curriculum doesn't exist yet, don't show a physics column
```

**Acceptance:**
- [ ] Dashboard does not display physics mastery unless real physics events exist
- [ ] If physics data is needed, add a TODO task (CNT-006) rather than faking it
- [ ] No hardcoded multipliers fabricating subject data exist in the service
