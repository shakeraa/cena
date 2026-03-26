# ACT-018: Advanced Supervision Strategies (Poison Messages, Circuit Breaker, Backoff)

**Priority:** P1 — required for production resilience
**Blocked by:** ACT-001 (cluster bootstrap), ACT-013 (basic supervision)
**Estimated effort:** 3 days
**Contract:** `contracts/actors/supervision_strategies.cs`

---

## Context
The basic supervision strategies (root restart, student child restart/stop) are covered in ACT-013. This task implements the three advanced patterns from the contract that make the system production-grade: poison message quarantine (prevents crash loops), per-actor circuit breakers (protects against cascading LLM failures), and exponential backoff (prevents restart storms). These are the strategies that distinguish a demo from a production system.

## Subtasks

### ACT-018.1: PoisonMessageAwareStrategy (Quarantine After N Failures)
**Files:**
- `src/Cena.Actors/Supervision/PoisonMessageAwareStrategy.cs` — wrapper strategy
- `src/Cena.Actors/Supervision/PoisonTracker.cs` — per-message failure tracking

**Acceptance:**
- [ ] Implements `ISupervisorStrategy` as a decorator wrapping an inner strategy
- [ ] Constructor: `PoisonMessageAwareStrategy(ISupervisorStrategy inner)`
- [ ] Tracks failures per `"{childPID}:{messageTypeName}"` key in `ConcurrentDictionary<string, PoisonTracker>`
- [ ] `PoisonThreshold = 2`: if the same message type causes 2 consecutive failures on the same actor, it's a poison message
- [ ] `HandleFailure()` logic:
  1. If `message == null`: delegate directly to `_inner.HandleFailure()`
  2. Build key: `$"{child}:{message.GetType().Name}"`
  3. Increment failure count for this key
  4. If `failureCount >= PoisonThreshold`:
     - Call `CenaSupervisionStrategies.QuarantinePoisonMessage(child, message, reason)`
     - Remove tracker entry (clean up)
     - Resume the actor via `supervisor.RestartChildren(reason, child)` (skip the poison message)
     - Return (do NOT delegate to inner strategy)
  5. Else: delegate to `_inner.HandleFailure()` (normal supervision)

**Quarantine actions (from contract):**
- [ ] Increment `cena.supervision.poison_messages_total` counter with tags `actor`, `message.type`
- [ ] Log at ERROR level: "POISON MESSAGE quarantined. Actor={Actor}, MessageType={Type}, Reason={Reason}. Message will be skipped."
- [ ] TODO markers for: publish to NATS `cena.alerts.poison_message`, store in dead-letter topic

**Telemetry:**
- [ ] Uses the shared `CenaSupervisionStrategies` static counters:
  - `cena.supervision.poison_messages_total` (Counter<long>)
- [ ] Tags: `actor` (PID string), `message.type` (message class name)

**Test:**
```csharp
[Fact]
public void PoisonMessageAware_QuarantinesAfterTwoFailures()
{
    var innerDirectives = new List<SupervisorDirective>();
    var inner = new TrackingStrategy(innerDirectives);
    var strategy = new PoisonMessageAwareStrategy(inner);

    var supervisor = new MockSupervisor();
    var child = PID.FromAddress("test", "child-1");
    var rs = new RestartStatistics(0, DateTimeOffset.UtcNow);
    var poisonMsg = new BadMessage("crash me");
    var reason = new InvalidOperationException("boom");

    // First failure: delegates to inner
    strategy.HandleFailure(supervisor, child, rs, reason, poisonMsg);
    Assert.Single(innerDirectives); // Inner was called

    // Second failure: quarantined (inner NOT called again)
    strategy.HandleFailure(supervisor, child, rs, reason, poisonMsg);
    Assert.Single(innerDirectives); // Inner still called only once
    Assert.True(supervisor.RestartCalled); // Actor resumed
}

[Fact]
public void PoisonMessageAware_DifferentMessageTypes_NotQuarantined()
{
    var inner = new TrackingStrategy(new List<SupervisorDirective>());
    var strategy = new PoisonMessageAwareStrategy(inner);

    var supervisor = new MockSupervisor();
    var child = PID.FromAddress("test", "child-2");
    var rs = new RestartStatistics(0, DateTimeOffset.UtcNow);

    // Different message types — no quarantine
    strategy.HandleFailure(supervisor, child, rs,
        new Exception("fail"), new MessageTypeA());
    strategy.HandleFailure(supervisor, child, rs,
        new Exception("fail"), new MessageTypeB());

    // Both delegated to inner (neither quarantined)
    Assert.False(supervisor.RestartCalled);
}

[Fact]
public void PoisonMessageAware_NullMessage_DelegatesToInner()
{
    var innerCalled = false;
    var inner = new CallbackStrategy(() => innerCalled = true);
    var strategy = new PoisonMessageAwareStrategy(inner);

    strategy.HandleFailure(new MockSupervisor(), PID.FromAddress("t", "c"),
        new RestartStatistics(0, DateTimeOffset.UtcNow),
        new Exception("fail"), message: null);

    Assert.True(innerCalled);
}

[Fact]
public void PoisonMessageAware_CleansUpAfterQuarantine()
{
    var strategy = new PoisonMessageAwareStrategy(new NoOpStrategy());
    var supervisor = new MockSupervisor();
    var child = PID.FromAddress("test", "child-3");
    var rs = new RestartStatistics(0, DateTimeOffset.UtcNow);
    var msg = new BadMessage("crash");
    var reason = new Exception("fail");

    // Quarantine
    strategy.HandleFailure(supervisor, child, rs, reason, msg);
    strategy.HandleFailure(supervisor, child, rs, reason, msg);

    // Now the tracker is cleaned up — next occurrence starts fresh
    supervisor.ResetCalls();
    strategy.HandleFailure(supervisor, child, rs, reason, msg);
    // This is failure 1 of a new tracking cycle — delegates to inner
    Assert.False(supervisor.RestartCalled);
}
```

---

### ACT-018.2: ActorCircuitBreaker (Per-Actor Circuit Breaker)
**Files:**
- `src/Cena.Actors/Supervision/ActorCircuitBreaker.cs` — circuit breaker implementation

**Acceptance:**
- [ ] Constructor: `ActorCircuitBreaker(string name, ILogger logger, int failureThreshold = 5, TimeSpan? failureWindow = null, TimeSpan? openDuration = null)`
- [ ] Defaults: `failureThreshold: 5`, `failureWindow: 30s`, `openDuration: 60s`
- [ ] Three states: `CircuitState { Closed, Open, HalfOpen }`

**State machine:**
- [ ] **Closed** (normal): calls pass through. On failure: record in sliding window. If failures >= threshold within window: transition to Open.
- [ ] **Open**: all calls immediately throw `CircuitBreakerOpenException` with message including retry-after time. After `openDuration` expires: transition to HalfOpen.
- [ ] **HalfOpen**: allows a single probe call. On success: transition to Closed, clear failures. On failure: transition back to Open.

**Sliding window failure tracking:**
- [ ] Failures stored in `Queue<DateTimeOffset>`
- [ ] On each failure: enqueue timestamp, prune entries older than `_failureWindow`
- [ ] Trip when `_failures.Count >= _failureThreshold` after pruning

**Thread safety:**
- [ ] All state mutations under `lock (_lock)`
- [ ] `State` property calls `MaybeTransition()` before returning (handles Open->HalfOpen transition)

**ExecuteAsync<T>:**
- [ ] Check state under lock: if Open, throw `CircuitBreakerOpenException`
- [ ] Execute the action
- [ ] On success + HalfOpen: transition to Closed, clear failures, emit `cena.circuit_breaker.resets_total`
- [ ] On failure (not `CircuitBreakerOpenException`): call `RecordFailure()` under lock

**Telemetry:**
- [ ] `cena.circuit_breaker.trips_total` counter (tag: `breaker` = name)
- [ ] `cena.circuit_breaker.rejections_total` counter (tag: `breaker` = name)
- [ ] `cena.circuit_breaker.resets_total` counter (tag: `breaker` = name)
- [ ] Log at WARNING on trip: "Circuit breaker '{Name}' OPENED after {Count} failures in {Window}s"
- [ ] Log at INFO on close: "Circuit breaker '{Name}' CLOSED after successful probe"

**Test:**
```csharp
[Fact]
public async Task CircuitBreaker_StartsInClosedState()
{
    var breaker = CreateBreaker(failureThreshold: 5);
    Assert.Equal(CircuitState.Closed, breaker.State);
}

[Fact]
public async Task CircuitBreaker_TripsAfterThresholdFailures()
{
    var breaker = CreateBreaker(failureThreshold: 3, failureWindow: TimeSpan.FromSeconds(10));

    for (int i = 0; i < 3; i++)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await breaker.ExecuteAsync<int>(async () =>
                throw new InvalidOperationException("fail")));
    }

    Assert.Equal(CircuitState.Open, breaker.State);
}

[Fact]
public async Task CircuitBreaker_RejectsCallsWhenOpen()
{
    var breaker = CreateAndTrip(failureThreshold: 3);

    await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
        await breaker.ExecuteAsync<int>(async () => 42));
}

[Fact]
public async Task CircuitBreaker_TransitionsToHalfOpen()
{
    var breaker = CreateAndTrip(failureThreshold: 3,
        openDuration: TimeSpan.FromMilliseconds(100));

    await Task.Delay(150);

    // Probe succeeds
    var result = await breaker.ExecuteAsync(async () => 42);
    Assert.Equal(42, result);
}

[Fact]
public async Task CircuitBreaker_ClosesAfterSuccessfulProbe()
{
    var breaker = CreateAndTrip(failureThreshold: 3,
        openDuration: TimeSpan.FromMilliseconds(100));

    await Task.Delay(150);

    // 1 probe success transitions HalfOpen -> may need additional successes
    await breaker.ExecuteAsync(async () => 1);

    // Full close after successful execution
    Assert.Equal(CircuitState.Closed, breaker.State);
}

[Fact]
public async Task CircuitBreaker_HalfOpenFailure_ReOpens()
{
    var breaker = CreateAndTrip(failureThreshold: 3,
        openDuration: TimeSpan.FromMilliseconds(100));

    await Task.Delay(150); // Transition to HalfOpen

    // Probe fails
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        await breaker.ExecuteAsync<int>(async () =>
            throw new InvalidOperationException("still broken")));

    Assert.Equal(CircuitState.Open, breaker.State); // Back to Open
}

[Fact]
public async Task CircuitBreaker_SlidingWindowPrunes()
{
    var breaker = CreateBreaker(failureThreshold: 3,
        failureWindow: TimeSpan.FromMilliseconds(200));

    // 2 failures
    for (int i = 0; i < 2; i++)
        try { await breaker.ExecuteAsync<int>(async () => throw new Exception("f")); }
        catch { }

    // Wait for window to expire
    await Task.Delay(300);

    // 1 more failure (old ones pruned, count = 1, below threshold)
    try { await breaker.ExecuteAsync<int>(async () => throw new Exception("f")); }
    catch { }

    Assert.Equal(CircuitState.Closed, breaker.State); // NOT tripped
}

[Fact]
public async Task CircuitBreaker_VoidOverload_Works()
{
    var breaker = CreateBreaker(failureThreshold: 3);
    var executed = false;

    await breaker.ExecuteAsync(async () => { executed = true; });

    Assert.True(executed);
}
```

---

### ACT-018.3: ExponentialBackoffStrategy (1s -> 30s Cap)
**Files:**
- `src/Cena.Actors/Supervision/ExponentialBackoffStrategy.cs` — backoff strategy

**Acceptance:**
- [ ] Implements `ISupervisorStrategy`
- [ ] Constructor: `ExponentialBackoffStrategy(TimeSpan initialBackoff, TimeSpan resetBackoffAfter, TimeSpan? maxBackoff = null)`
- [ ] Defaults: `maxBackoff = 30 seconds`
- [ ] Backoff formula: `delay = min(initialBackoff * 2^(failureCount - 1), maxBackoff)`
  - Failure 1: 1s
  - Failure 2: 2s
  - Failure 3: 4s
  - Failure 4: 8s
  - Failure 5: 16s
  - Failure 6+: 30s (capped)

**Per-actor state tracking:**
- [ ] `ConcurrentDictionary<string, BackoffState>` keyed by `child.ToString()`
- [ ] `BackoffState { int ConsecutiveFailures, DateTimeOffset? LastFailure }`
- [ ] Reset condition: if `(now - lastFailure) > resetBackoffAfter`, reset `ConsecutiveFailures = 0`

**HandleFailure:**
- [ ] Get or create `BackoffState` for the child PID
- [ ] Check reset condition: if enough time since last failure, reset counter
- [ ] Increment `ConsecutiveFailures`
- [ ] Record `LastFailure = UtcNow`
- [ ] Calculate backoff duration
- [ ] Log via `CenaSupervisionStrategies.LogRestart(actorId, failures, backoff, reason)`:
  "Backoff supervisor: restarting {Actor} after {Backoff}ms. ConsecutiveFailures={Failures}. Reason={Reason}"
- [ ] Schedule delayed restart: `Task.Delay(backoff).ContinueWith(_ => supervisor.RestartChildren(reason, child))`

**Test:**
```csharp
[Fact]
public void ExponentialBackoff_CalculatesCorrectDelays()
{
    // Verify the formula: min(initial * 2^(n-1), max)
    var initial = TimeSpan.FromSeconds(1);
    var max = TimeSpan.FromSeconds(30);

    Assert.Equal(1_000, CalculateBackoff(initial, max, failures: 1));  // 1s
    Assert.Equal(2_000, CalculateBackoff(initial, max, failures: 2));  // 2s
    Assert.Equal(4_000, CalculateBackoff(initial, max, failures: 3));  // 4s
    Assert.Equal(8_000, CalculateBackoff(initial, max, failures: 4));  // 8s
    Assert.Equal(16_000, CalculateBackoff(initial, max, failures: 5)); // 16s
    Assert.Equal(30_000, CalculateBackoff(initial, max, failures: 6)); // 30s (capped)
    Assert.Equal(30_000, CalculateBackoff(initial, max, failures: 10)); // Still 30s
}

[Fact]
public async Task ExponentialBackoff_RestartsChildAfterDelay()
{
    var supervisor = new MockSupervisor();
    var strategy = new ExponentialBackoffStrategy(
        initialBackoff: TimeSpan.FromMilliseconds(50),
        resetBackoffAfter: TimeSpan.FromMinutes(2));

    var child = PID.FromAddress("test", "child-1");
    var reason = new Exception("transient");

    strategy.HandleFailure(supervisor, child,
        new RestartStatistics(0, DateTimeOffset.UtcNow), reason, null);

    // Not restarted immediately
    Assert.False(supervisor.RestartCalled);

    // Restarted after delay (50ms for first failure)
    await Task.Delay(100);
    Assert.True(supervisor.RestartCalled);
}

[Fact]
public async Task ExponentialBackoff_IncreasingDelays()
{
    var restartTimes = new List<DateTimeOffset>();
    var supervisor = new TimestampingSupervisor(restartTimes);
    var strategy = new ExponentialBackoffStrategy(
        initialBackoff: TimeSpan.FromMilliseconds(50),
        resetBackoffAfter: TimeSpan.FromMinutes(2));

    var child = PID.FromAddress("test", "child-2");

    // Trigger 3 failures
    for (int i = 0; i < 3; i++)
    {
        strategy.HandleFailure(supervisor, child,
            new RestartStatistics(0, DateTimeOffset.UtcNow),
            new Exception("fail"), null);
        await Task.Delay(200); // Wait for delayed restart
    }

    Assert.Equal(3, restartTimes.Count);

    // Verify increasing gaps (50ms, 100ms, 200ms approximately)
    var gap1 = (restartTimes[1] - restartTimes[0]).TotalMilliseconds;
    var gap2 = (restartTimes[2] - restartTimes[1]).TotalMilliseconds;
    Assert.True(gap2 > gap1, $"Gaps should increase: {gap1:F0}ms, {gap2:F0}ms");
}

[Fact]
public async Task ExponentialBackoff_ResetsAfterQuietPeriod()
{
    var supervisor = new MockSupervisor();
    var strategy = new ExponentialBackoffStrategy(
        initialBackoff: TimeSpan.FromMilliseconds(50),
        resetBackoffAfter: TimeSpan.FromMilliseconds(200));

    var child = PID.FromAddress("test", "child-3");

    // 3 failures (backoff grows)
    for (int i = 0; i < 3; i++)
    {
        strategy.HandleFailure(supervisor, child,
            new RestartStatistics(0, DateTimeOffset.UtcNow),
            new Exception("fail"), null);
    }

    // Wait for reset period
    await Task.Delay(300);

    // Next failure should use initial backoff (reset happened)
    supervisor.ResetCalls();
    strategy.HandleFailure(supervisor, child,
        new RestartStatistics(0, DateTimeOffset.UtcNow),
        new Exception("fail"), null);

    await Task.Delay(100); // Initial backoff is 50ms
    Assert.True(supervisor.RestartCalled);
}

[Fact]
public void ExponentialBackoff_CapsAt30Seconds()
{
    var strategy = new ExponentialBackoffStrategy(
        initialBackoff: TimeSpan.FromSeconds(1),
        resetBackoffAfter: TimeSpan.FromMinutes(2),
        maxBackoff: TimeSpan.FromSeconds(30));

    // After 10 failures: 1 * 2^9 = 512s, but capped at 30s
    var backoff = CalculateBackoffFromStrategy(strategy, failures: 10);
    Assert.True(backoff.TotalSeconds <= 30.0,
        $"Backoff ({backoff.TotalSeconds}s) should be capped at 30s");
}
```

---

## Integration Test (strategies working together)

```csharp
[Fact]
public async Task SupervisionStrategies_ComposeCorrectly()
{
    // Backoff with poison message detection
    var backoff = new ExponentialBackoffStrategy(
        TimeSpan.FromMilliseconds(50), TimeSpan.FromMinutes(2));
    var composed = new PoisonMessageAwareStrategy(backoff);

    var supervisor = new MockSupervisor();
    var child = PID.FromAddress("test", "child-composed");

    // First failure with message: delegates to backoff
    composed.HandleFailure(supervisor, child,
        new RestartStatistics(0, DateTimeOffset.UtcNow),
        new Exception("fail"), new TestMessage("a"));

    await Task.Delay(100);
    Assert.True(supervisor.RestartCalled); // Backoff triggered restart

    supervisor.ResetCalls();

    // Second failure with SAME message: quarantined (poison)
    composed.HandleFailure(supervisor, child,
        new RestartStatistics(0, DateTimeOffset.UtcNow),
        new Exception("fail"), new TestMessage("a"));

    // Poison handler resumed immediately (no backoff delay)
    Assert.True(supervisor.RestartCalled);
}
```

## Rollback Criteria
- If poison message detection is too aggressive (false positives): increase `PoisonThreshold` from 2 to 5
- If circuit breaker opens too frequently: increase `failureThreshold` from 5 to 10 or widen `failureWindow`
- If exponential backoff causes timeouts: reduce `maxBackoff` from 30s to 10s

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=Supervision"` -- 0 failures
- [ ] Thread-safety verified: all strategies safe under concurrent failures
- [ ] Telemetry verified: counters increment correctly in tests
- [ ] Composed strategies (poison + backoff) work together
- [ ] PR reviewed by architect
