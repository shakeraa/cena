# EMU-002: Arrival Scheduler — Peak Distribution, 30% Max Concurrency

**Priority:** P0 — controls when students arrive and leave
**Blocked by:** EMU-001
**Estimated effort:** 2 days
**Contract:** `docs/question-ingestion-specification.md` (serving section)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.**

## Context

The current emulator replays ALL students' history at once, creating an unrealistic burst. The new scheduler models a realistic school day: students arrive in waves (morning, afternoon peak, evening taper), study for their session duration, then leave. Max 30% concurrent (300 of 1,000) at any time.

## Subtasks

### EMU-002.1: Daily Schedule Model

**Files to create/modify:**
- `src/emulator/Scheduler/DailyScheduleModel.cs`

**Acceptance:**
- [ ] Models a 24-hour day with arrival rate curve:
  ```
  Arrival Rate (students/minute):

  06:00-08:00  ░░░░░               (trickle: early birds, 2-5/min)
  08:00-10:00  ░░░░░░░░░           (morning wave: 5-10/min)
  10:00-14:00  ░░░░░░              (school hours: low, 3-6/min)
  14:00-16:00  ░░░░░░░░░░░░░       (after school PEAK: 10-20/min)
  16:00-18:00  ░░░░░░░░░░░░░░░░░░  (PEAK: 15-25/min — max concurrency)
  18:00-20:00  ░░░░░░░░░░░░        (dinner dip + evening: 8-15/min)
  20:00-22:00  ░░░░░░░░░           (evening: 5-10/min)
  22:00-00:00  ░░░░                (taper: 2-4/min)
  00:00-06:00  ░                   (night owls: 0-1/min)
  ```
- [ ] Arrival rate is a smooth curve (not step function) — use sine-based model
- [ ] Weekend schedule: shifted later (peak at 11:00-14:00), lower overall volume (60% of weekday)
- [ ] Friday (Israel): early shutdown at 14:00, no evening activity
- [ ] Configurable via `config/emulator/schedule.yaml`

### EMU-002.2: Concurrency Limiter

**Files to create/modify:**
- `src/emulator/Scheduler/ConcurrencyLimiter.cs`

**Acceptance:**
- [ ] Hard cap: max 30% of total students active concurrently (300 of 1,000)
- [ ] Soft cap: arrival rate naturally limited by schedule curve
- [ ] When at capacity: new arrivals queue and enter when a slot opens (student finishes session)
- [ ] Metrics exposed: `currentActive`, `queueDepth`, `peakConcurrency`, `avgSessionDuration`
- [ ] Backpressure: if queue > 50, slow down arrival rate (simulates "server busy" in real app)

**Test:**
```csharp
[Fact]
public async Task Limiter_NeverExceeds30Percent()
{
    var limiter = new ConcurrencyLimiter(maxConcurrency: 300);
    var peakObserved = 0;
    var tasks = Enumerable.Range(0, 500).Select(async i =>
    {
        await limiter.AcquireAsync();
        var current = limiter.CurrentActive;
        Interlocked.CompareExchange(ref peakObserved, current, current > peakObserved ? peakObserved : current);
        await Task.Delay(Random.Shared.Next(10, 50)); // simulate work
        limiter.Release();
    });
    await Task.WhenAll(tasks);
    Assert.True(peakObserved <= 300);
}
```

### EMU-002.3: Session Lifecycle Manager

**Files to create/modify:**
- `src/emulator/Scheduler/SessionLifecycleManager.cs`

**Acceptance:**
- [ ] Each student session follows this lifecycle:
  1. **Arrive**: student enters the platform (session start command via NATS)
  2. **Study**: generates concept attempts at archetype-specific pace (1 attempt every 15-60s compressed)
  3. **Focus degrade**: after `avgSessionMinutes × (0.7-1.3)`, focus declines
  4. **Break or leave**: either takes microbreak (20% chance) and continues, or ends session
  5. **Depart**: session end command via NATS, releases concurrency slot
- [ ] Session duration drawn from archetype's study habit profile with Gaussian noise
- [ ] Students may return later in the same day (1-3 sessions/day per archetype)
- [ ] Between sessions: cooldown period (30min-2hrs) before next session eligible

**Test:**
```csharp
[Fact]
public void SessionLifecycle_DurationMatchesArchetype()
{
    var profile = StudyHabitProfile.ForArchetype("SteadyLearner");
    var durations = Enumerable.Range(0, 100)
        .Select(_ => SessionLifecycleManager.GenerateSessionDuration(profile, new Random(42)))
        .ToList();
    var avg = durations.Average(d => d.TotalMinutes);
    Assert.InRange(avg, 20, 40); // SteadyLearner: 25-35 min mean
}
```
