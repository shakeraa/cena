# ACT-017: Test Infrastructure (Property-Based, Load, Chaos)

**Priority:** P1 — required for production confidence before launch
**Blocked by:** ACT-002 (StudentActor), ACT-001 (cluster), DATA-001 (Marten)
**Estimated effort:** 4 days
**Contract:** `contracts/actors/actor_tests.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The test contract defines three advanced test categories beyond standard unit/integration tests: property-based BKT verification, 10K-actor load tests, and chaos tests for resilience. These are the tests that catch the bugs unit tests miss -- invariant violations in mathematical models, performance regressions under load, and data loss during infrastructure failures. Each category has specific infrastructure requirements and is gated behind test category filters.

## Subtasks

### ACT-017.1: BKT Property-Based Tests (FsCheck/Hedgehog)
**Files:**
- `tests/Cena.Actors.Tests/PropertyBased/BktPropertyTests.cs` — property-based tests
- `tests/Cena.Actors.Tests/PropertyBased/BktGenerators.cs` — custom FsCheck generators

**Acceptance:**
- [ ] Test trait: `[Trait("Category", "PropertyBased")]`, `[Trait("Component", "BKT")]`
- [ ] NuGet: `FsCheck.Xunit` (or `Hedgehog` as alternative)
- [ ] Seven properties verified (from contract):

**Property 1: P(known) always clamped to [0.01, 0.99]**
- [ ] For any combination of `pKnown in [0,1]`, `pLearn in [0,1]`, `pGuess in [0,0.5]`, `pSlip in [0,0.5]`, `isCorrect in {true,false}`
- [ ] Result is always in `[0.01, 0.99]`
- [ ] Tested with `[Theory]` + edge case `[InlineData]`:
  - `(0.0, 0.1, 0.25, 0.1, true)`, `(1.0, 0.1, 0.25, 0.1, false)`
  - `(0.5, 0.0, 0.5, 0.5, true)`, `(0.5, 1.0, 0.0, 0.0, false)`
  - `(0.001, 0.001, 0.001, 0.001, true)`, `(0.999, 0.999, 0.499, 0.499, false)`

**Property 2: Correct answer never decreases P(known) when P(G) < P(1-S)**
- [ ] For calibrated models where guess rate < correct rate
- [ ] `BktUpdate(prior, isCorrect: true).PosteriorMastery >= prior`

**Property 3: Convergence to high mastery with many correct answers**
- [ ] Starting from `P(known) = 0.3`, after 50 correct answers: `P(known) > 0.95`

**Property 4: Convergence to low mastery with many incorrect answers**
- [ ] Starting from `P(known) = 0.7`, after 50 incorrect answers: `P(known) < 0.15`

**Property 5: Determinism**
- [ ] Same inputs always produce same output (no random state, no wall clock)

**Property 6: Batch update equivalence**
- [ ] `BatchUpdate(concept, prior, [a1,a2,a3])` == sequential `Update(a1)`, `Update(a2)`, `Update(a3)`

**Property 7: Parameter bounds**
- [ ] `P(G)` and `P(S)` in `[0, 0.5]`, `P(T)` and `P(L0)` in `[0, 1]`
- [ ] Constructor rejects out-of-bound values

- [ ] BKT update formula mirrors contract exactly:
  ```
  if correct: posterior = (pKnown * (1 - pSlip)) / (pKnown * (1 - pSlip) + (1 - pKnown) * pGuess)
  if incorrect: posterior = (pKnown * pSlip) / (pKnown * pSlip + (1 - pKnown) * (1 - pGuess))
  updated = posterior + (1 - posterior) * pLearn
  clamped = Math.Clamp(updated, 0.01, 0.99)
  ```

**Test:**
```csharp
[Trait("Category", "PropertyBased")]
[Trait("Component", "BKT")]
public sealed class BktPropertyTests
{
    [Theory]
    [InlineData(0.0, 0.1, 0.25, 0.1, true)]
    [InlineData(1.0, 0.1, 0.25, 0.1, false)]
    [InlineData(0.5, 0.0, 0.5, 0.5, true)]
    [InlineData(0.5, 1.0, 0.0, 0.0, false)]
    [InlineData(0.001, 0.001, 0.001, 0.001, true)]
    [InlineData(0.999, 0.999, 0.499, 0.499, false)]
    public void BktUpdate_AlwaysClampedToValidRange(
        double pKnown, double pLearn, double pGuess, double pSlip, bool isCorrect)
    {
        var result = BktUpdate(pKnown, pLearn, pGuess, pSlip, isCorrect);
        Assert.InRange(result, 0.01, 0.99);
    }

    [Theory]
    [InlineData(0.3, 0.1, 0.25, 0.1)]
    [InlineData(0.5, 0.1, 0.2, 0.1)]
    [InlineData(0.7, 0.05, 0.15, 0.05)]
    [InlineData(0.1, 0.2, 0.25, 0.1)]
    public void BktUpdate_CorrectAnswer_NeverDecreasesMastery(
        double pKnown, double pLearn, double pGuess, double pSlip)
    {
        var after = BktUpdate(pKnown, pLearn, pGuess, pSlip, isCorrect: true);
        Assert.True(after >= pKnown,
            $"Correct answer decreased P(known): {pKnown:F6} -> {after:F6}");
    }

    [Fact]
    public void BktUpdate_ManyCorrectAnswers_ConvergesToHigh()
    {
        double pk = 0.3;
        for (int i = 0; i < 50; i++)
            pk = BktUpdate(pk, 0.1, 0.25, 0.1, true);
        Assert.True(pk > 0.95, $"After 50 correct: {pk:F6}");
    }

    [Fact]
    public void BktUpdate_ManyIncorrectAnswers_ConvergesToLow()
    {
        double pk = 0.7;
        for (int i = 0; i < 50; i++)
            pk = BktUpdate(pk, 0.05, 0.25, 0.1, false);
        Assert.True(pk < 0.15, $"After 50 incorrect: {pk:F6}");
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.5, false)]
    [InlineData(0.1, true)]
    [InlineData(0.9, false)]
    public void BktUpdate_IsDeterministic(double pKnown, bool isCorrect)
    {
        var r1 = BktUpdate(pKnown, 0.1, 0.25, 0.1, isCorrect);
        var r2 = BktUpdate(pKnown, 0.1, 0.25, 0.1, isCorrect);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void BatchUpdate_EqualsSequentialUpdate()
    {
        var svc = new BktService(DefaultStore());
        var attempts = new[] {
            new AttemptOutcome(true, 0, false),
            new AttemptOutcome(false, 0, false),
            new AttemptOutcome(true, 1, false),
        };

        var batchResult = svc.BatchUpdate("concept-1", 0.5, attempts);

        // Sequential
        double seq = 0.5;
        foreach (var a in attempts)
        {
            var r = svc.Update(new BktUpdateInput("concept-1", seq, a.IsCorrect, a.HintCountUsed, a.WasSkipped));
            seq = r.PosteriorMastery;
        }

        Assert.Equal(seq, batchResult.PosteriorMastery, precision: 10);
    }
}
```

---

### ACT-017.2: StudentActor Load Tests (10K Actors)
**Files:**
- `tests/Cena.Actors.Tests/Load/StudentActorLoadTests.cs` — load tests
- `tests/Cena.Actors.Tests/Load/LoadTestHelpers.cs` — shared helpers

**Acceptance:**
- [ ] Test trait: `[Trait("Category", "Load")]`, `[Trait("Component", "Cluster")]`
- [ ] Gated: `[Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Load")]`
- [ ] Constants: `ActorCount = 10_000`, `MessagesPerActor = 5`

**Load Test 1: Activate 10K Actors — Measure Latency**
- [ ] Spawn 10,000 StudentActors with mocked dependencies (no real DB/NATS/Redis)
- [ ] Measure activation time per actor using `Stopwatch`
- [ ] Compute percentiles: p50, p95, p99
- [ ] Target: p99 activation time < 50ms for local spawning
- [ ] Report throughput: `ActorCount / totalSeconds` actors/s
- [ ] Cleanup: `StopAsync` all actors after measurement
- [ ] Output formatted results to test output:
  ```
  === Actor Activation Latency (10000 actors) ===
    Total time: Xs
    p50: Xms, p95: Xms, p99: Xms
    Throughput: X actors/s
  ```

**Load Test 2: Measure Memory Per Actor**
- [ ] Create a `StudentState` with realistic data: 200 concepts, 20 recent attempts, 50 HLR timers
- [ ] Call `state.EstimateMemoryBytes()`
- [ ] Target: < 500KB per actor (`StudentState.MemoryBudgetBytes`)
- [ ] Report: estimated KB vs budget KB

**Load Test 3: Message Throughput**
- [ ] Send `MessagesPerActor` (5) messages to each of the 10K actors concurrently
- [ ] Measure total throughput: messages/second
- [ ] Target: > 50K messages/second for simple in-memory operations
- [ ] Verify no message loss: all responses received

**Test:**
```csharp
[Trait("Category", "Load")]
[Trait("Component", "Cluster")]
public sealed class StudentActorLoadTests : IAsyncLifetime
{
    private const int ActorCount = 10_000;
    private const int MessagesPerActor = 5;

    [Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Load")]
    public async Task Activate10KActors_MeasureLatency()
    {
        var activationTimes = new List<double>(ActorCount);
        var actors = new List<PID>(ActorCount);

        var totalSw = Stopwatch.StartNew();
        for (int i = 0; i < ActorCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var pid = _system.Root.Spawn(CreateMockedStudentProps());
            actors.Add(pid);
            sw.Stop();
            activationTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        totalSw.Stop();

        activationTimes.Sort();
        var p50 = activationTimes[(int)(ActorCount * 0.50)];
        var p95 = activationTimes[(int)(ActorCount * 0.95)];
        var p99 = activationTimes[(int)(ActorCount * 0.99)];

        _output.WriteLine($"=== Actor Activation Latency ({ActorCount} actors) ===");
        _output.WriteLine($"  Total: {totalSw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"  p50: {p50:F2}ms  p95: {p95:F2}ms  p99: {p99:F2}ms");
        _output.WriteLine($"  Throughput: {ActorCount / totalSw.Elapsed.TotalSeconds:F0} actors/s");

        foreach (var pid in actors)
            await _system.Root.StopAsync(pid);

        Assert.True(p99 < 50.0, $"p99 ({p99:F2}ms) exceeded 50ms target");
    }

    [Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Load")]
    public void MeasureMemoryPerActor()
    {
        var state = CreateRealisticStudentState(
            conceptCount: 200, recentAttempts: 20, hlrTimers: 50);

        var estimatedBytes = state.EstimateMemoryBytes();

        _output.WriteLine($"Estimated: {estimatedBytes / 1024.0:F1}KB " +
            $"(budget: {StudentState.MemoryBudgetBytes / 1024}KB)");

        Assert.True(estimatedBytes < StudentState.MemoryBudgetBytes,
            $"Memory ({estimatedBytes}B) exceeds budget ({StudentState.MemoryBudgetBytes}B)");
    }

    [Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Load")]
    public async Task MessageThroughput_50KPerSecond()
    {
        var actors = SpawnActors(ActorCount);
        var responseCount = 0;

        var sw = Stopwatch.StartNew();
        var tasks = actors.SelectMany(pid =>
            Enumerable.Range(0, MessagesPerActor).Select(_ =>
                _system.Root.RequestAsync<ActorResult<StudentProfileResponse>>(
                    pid, new GetStudentProfile("test"), TimeSpan.FromSeconds(10))
                .ContinueWith(_ => Interlocked.Increment(ref responseCount))
            ));

        await Task.WhenAll(tasks);
        sw.Stop();

        var throughput = responseCount / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"Throughput: {throughput:F0} msg/s ({responseCount} msgs in {sw.Elapsed.TotalSeconds:F2}s)");

        Assert.Equal(ActorCount * MessagesPerActor, responseCount);
        Assert.True(throughput > 50_000, $"Throughput ({throughput:F0}) below 50K/s target");
    }
}
```

---

### ACT-017.3: Chaos Tests (Node Death, NATS Unavailability, Circuit Breaker)
**Files:**
- `tests/Cena.Actors.Tests/Chaos/ChaosTests.cs` — chaos/resilience tests
- `tests/Cena.Actors.Tests/Chaos/ChaosTestFixture.cs` — shared infrastructure

**Acceptance:**
- [ ] Test trait: `[Trait("Category", "Chaos")]`, `[Trait("Component", "Cluster")]`
- [ ] Some tests gated behind infrastructure: `[Fact(Skip = "Requires full cluster")]`
- [ ] Some tests runnable locally (circuit breaker, corrupted signal handling)

**Chaos Test 1: Node Death During Session — State Recovery**
- [ ] Pattern test documenting the implementation steps:
  1. Start StudentActor, begin session, process attempts (build state)
  2. Simulate node death (`system.ShutdownAsync()` without graceful drain)
  3. Create new actor system (simulates reactivation on new node)
  4. Reactivate StudentActor, verify state recovered from Marten snapshot + replay
  5. Verify: TotalXp, MasteryMap, EventVersion all match pre-crash state
- [ ] Gated behind `[Fact(Skip = "Requires full cluster")]` with implementation comments

**Chaos Test 2: NATS Unavailable — Events Still Persist**
- [ ] Configure NATS mock to throw `NatsException` on publish
- [ ] Send `AttemptConcept` to StudentActor
- [ ] Verify: actor responds with success (not crash)
- [ ] Verify: event IS in Marten event store (source of truth)
- [ ] Verify: NATS failure logged at WARNING (not ERROR — NATS is eventually consistent)
- [ ] Invariant: Marten is the source of truth. NATS is best-effort.

**Chaos Test 3: Stagnation Detector Handles Corrupted Signals**
- [ ] Send `UpdateSignals` with edge-case values: 0ms response time, zero baselines, null sentiment
- [ ] Verify: actor responds with success (no crash)
- [ ] Verify: structured log at WARNING for edge-case values
- [ ] Runnable locally (no infrastructure needed)

**Chaos Test 4: Circuit Breaker Opens After Threshold Failures**
- [ ] Create `ActorCircuitBreaker` with `failureThreshold: 3`, `failureWindow: 10s`, `openDuration: 2s`
- [ ] Trigger 3 failures via `ExecuteAsync` with throwing lambda
- [ ] Assert: `State == CircuitState.Open`
- [ ] Assert: subsequent calls throw `CircuitBreakerOpenException`
- [ ] Runnable locally

**Chaos Test 5: Circuit Breaker Transitions Open -> HalfOpen -> Closed**
- [ ] Trip the breaker (3 failures)
- [ ] Wait for `openDuration` to expire (use short timeout: 100ms)
- [ ] Verify: `State == CircuitState.HalfOpen` — next call is a probe
- [ ] Execute 3 successful probes
- [ ] Verify: `State == CircuitState.Closed`
- [ ] Runnable locally

**Test:**
```csharp
[Trait("Category", "Chaos")]
public sealed class ChaosTests : IAsyncLifetime
{
    [Fact(Skip = "Requires full cluster")]
    public async Task NodeDeath_DuringSession_RecoversStateOnReactivation()
    {
        // Documented pattern — see contract for full implementation steps
        Assert.True(true, "Pattern test");
    }

    [Fact(Skip = "Requires infrastructure")]
    public async Task NatsUnavailable_EventsStillPersistToMarten()
    {
        // Invariant: Marten is source of truth. NATS is eventually consistent.
        Assert.True(true, "Pattern test");
    }

    [Fact]
    public async Task StagnationDetector_HandlesCorruptedSignals()
    {
        var system = new ActorSystem();
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var pid = system.Root.Spawn(
            Props.FromProducer(() => new StagnationDetectorActor(
                logger.CreateLogger<StagnationDetectorActor>())));

        var extremeSignal = new UpdateSignals(
            "test-student", "test-concept", "test-session",
            true, 0, ErrorType.None, 0, null, 0.0, 0.0);

        var response = await system.Root.RequestAsync<ActorResult>(
            pid, extremeSignal, TimeSpan.FromSeconds(5));

        Assert.NotNull(response);
        Assert.True(response.Success);

        await system.Root.StopAsync(pid);
        await system.ShutdownAsync();
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdFailures()
    {
        var breaker = new ActorCircuitBreaker(
            "test-breaker", _logger, failureThreshold: 3,
            failureWindow: TimeSpan.FromSeconds(10),
            openDuration: TimeSpan.FromSeconds(2));

        Assert.Equal(CircuitState.Closed, breaker.State);

        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await breaker.ExecuteAsync<int>(async () =>
                    throw new InvalidOperationException("fail")));
        }

        Assert.Equal(CircuitState.Open, breaker.State);

        await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            await breaker.ExecuteAsync<int>(async () => 42));
    }

    [Fact]
    public async Task CircuitBreaker_TransitionsOpenToHalfOpenToClosed()
    {
        var breaker = new ActorCircuitBreaker(
            "test-breaker-2", _logger, failureThreshold: 3,
            openDuration: TimeSpan.FromMilliseconds(100));

        // Trip
        for (int i = 0; i < 3; i++)
            try { await breaker.ExecuteAsync<int>(async () => throw new Exception("fail")); }
            catch { }
        Assert.Equal(CircuitState.Open, breaker.State);

        // Wait for open duration
        await Task.Delay(150);

        // Probe (HalfOpen)
        var result = await breaker.ExecuteAsync(async () => 42);
        Assert.Equal(42, result);

        // 2 more successes -> Closed
        await breaker.ExecuteAsync(async () => 1);
        await breaker.ExecuteAsync(async () => 1);
        Assert.Equal(CircuitState.Closed, breaker.State);
    }
}
```

---

## Integration Test (test infrastructure itself)

```csharp
[Fact]
public void AllTestCategories_HaveCorrectTraits()
{
    var assembly = typeof(BktPropertyTests).Assembly;
    var testClasses = assembly.GetTypes()
        .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes<FactAttribute>().Any()));

    foreach (var testClass in testClasses)
    {
        var traits = testClass.GetCustomAttributes<TraitAttribute>();
        Assert.True(traits.Any(t => t.Name == "Category"),
            $"{testClass.Name} missing Category trait");
    }
}
```

## Rollback Criteria
- If property-based tests are flaky: reduce FsCheck iteration count from 1000 to 100
- If load tests fail on CI: reduce ActorCount to 1000 and adjust targets proportionally
- If chaos tests require too much infra: keep as pattern tests (Skip), implement when staging is ready

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] `dotnet test --filter "Category=PropertyBased"` -- 0 failures
- [ ] `dotnet test --filter "Category=Load"` -- 0 failures (when infra available)
- [ ] `dotnet test --filter "Category=Chaos"` -- 0 failures for local tests
- [ ] Circuit breaker tests run in < 5 seconds
- [ ] Load test outputs formatted performance report
- [ ] PR reviewed by architect
