# ACT-016: Topology Actors (System Manager, Analytics, Dead Letters, Outreach Workers)

**Priority:** P1 — required for production-grade actor system lifecycle
**Blocked by:** ACT-001 (cluster bootstrap), ACT-002 (StudentActor), ACT-008 (CurriculumGraphActor)
**Estimated effort:** 5 days
**Contract:** `contracts/actors/actor_system_topology.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The actor system topology contract defines the full actor hierarchy. Several actors are covered by existing tasks (StudentActor in ACT-002, CurriculumGraphActor in ACT-008, StudentActorManager in ACT-010, GracefulShutdownCoordinator in ACT-014, LlmGatewayActor in ACT-009). This task fills the remaining gaps: the root ActorSystemManager, AnalyticsAggregatorActor, DeadLetterWatcher, OutreachDispatcherActor, and all four channel worker actors.

## Subtasks

### ACT-016.1: ActorSystemManager (Root Guardian, Ordered Bootstrap)
**Files:**
- `src/Cena.Actors/Topology/ActorSystemManager.cs` — root guardian actor

**Acceptance:**
- [ ] Implements `IActor` with `ReceiveAsync(IContext context)`
- [ ] On `Started` message: bootstraps all singleton actors in dependency order:
  1. CurriculumGraphActor (others query it)
  2. LlmGatewayActor (circuit breakers ready before students)
  3. StudentActorManager (student pool)
  4. OutreachDispatcherActor (outreach channels)
  5. AnalyticsAggregatorActor (analytics sink)
  6. DeadLetterWatcher (monitoring last)
  7. GracefulShutdownCoordinator (registers all actors)
- [ ] Each singleton spawned via `context.SpawnNamed()` with appropriate supervision strategy:
  - CurriculumGraph: `SupervisionStrategies.CriticalSingleton` (restart 3x in 1min)
  - LlmGateway: `SupervisionStrategies.LlmGateway` (Resume, circuit breaker self-heals)
  - StudentManager: `SupervisionStrategies.StudentPool` (restart 3x in 60s)
  - OutreachDispatcher: `SupervisionStrategies.OutreachWorkers` (restart 5x in 2min)
  - AnalyticsAggregator: `SupervisionStrategies.AnalyticsSink` (restart 3x in 1min)
- [ ] Child PIDs stored as private fields for health check and shutdown coordination
- [ ] On `Stopping` message: delegates to `GracefulShutdownCoordinator` with 30s timeout
- [ ] On `SystemHealthCheckRequest`: responds with `SystemHealthReport` containing readiness status of each child
- [ ] Logs each singleton startup at INFO level: "ActorSystemManager: {name} started"
- [ ] Logs full bootstrap completion: "ActorSystemManager: all actors bootstrapped successfully"

**Test:**
```csharp
[Fact]
public async Task ActorSystemManager_BootstrapsAllSingletons()
{
    var system = new ActorSystem();
    var spawnedActors = new List<string>();

    // Use DI mock that tracks spawned actors
    var manager = system.Root.SpawnNamed(
        Props.FromProducer(() => new ActorSystemManager(system, _logger)),
        "system-manager");

    // Give bootstrap time to complete
    await Task.Delay(TimeSpan.FromSeconds(2));

    var health = await system.Root.RequestAsync<SystemHealthReport>(
        manager, new SystemHealthCheckRequest(), TimeSpan.FromSeconds(5));

    Assert.True(health.CurriculumGraphLoaded);
    Assert.True(health.LlmGatewayReady);
    Assert.True(health.StudentManagerReady);
    Assert.True(health.OutreachDispatcherReady);
    Assert.True(health.AnalyticsReady);
}

[Fact]
public async Task ActorSystemManager_BootstrapsInOrder()
{
    var bootOrder = new List<string>();
    // Verify CurriculumGraph starts before StudentManager
    // (CurriculumGraph is dependency for prerequisite checks)
    Assert.True(bootOrder.IndexOf("CurriculumGraph") < bootOrder.IndexOf("StudentManager"));
}

[Fact]
public async Task ActorSystemManager_DelegatesShutdown()
{
    var system = new ActorSystem();
    var manager = SpawnTestManager(system);

    // Trigger shutdown
    await system.Root.StopAsync(manager);

    // Verify GracefulShutdownCoordinator was invoked
    // (verified through mock or structured log assertion)
}
```

---

### ACT-016.2: AnalyticsAggregatorActor (Event Batching, S3 Flush)
**Files:**
- `src/Cena.Actors/Topology/AnalyticsAggregatorActor.cs` — batching event sink
- `src/Cena.Actors/Topology/S3ExportWorkerActor.cs` — S3 upload child

**Acceptance:**
- [ ] Constructor: `AnalyticsAggregatorActor(int flushThreshold = 1000, TimeSpan? flushInterval = null)`
- [ ] Default flush interval: 5 minutes (via `context.SetReceiveTimeout`)
- [ ] On `Started`: sets receive timeout to `_flushInterval`
- [ ] On any domain event (namespace `Cena.Data.EventStore`): buffers in `List<object> _buffer`
- [ ] Flushes to S3 when:
  - Buffer reaches `_flushThreshold` (1000 events), OR
  - `ReceiveTimeout` fires (5 minutes elapsed since last message)
- [ ] Flush process:
  1. Anonymize PII: replace student IDs with HMAC-SHA256 using rotating epoch key
  2. Serialize to Parquet format
  3. Upload to S3 path: `s3://cena-analytics/{year}/{month}/{day}/{timestamp}.parquet`
  4. Clear buffer on success
- [ ] On flush failure: log ERROR, retain buffer (retry on next trigger)
- [ ] Metrics: `cena.analytics.events_buffered` counter, `cena.analytics.flushes` counter
- [ ] Detection: `IsDomainEvent()` checks `msg.GetType().Namespace?.StartsWith("Cena.Data.EventStore")`

**Test:**
```csharp
[Fact]
public async Task AnalyticsAggregator_BuffersEvents()
{
    var system = new ActorSystem();
    var actor = system.Root.SpawnNamed(
        Props.FromProducer(() => new AnalyticsAggregatorActor(flushThreshold: 5)),
        "analytics");

    // Send 4 events (below threshold)
    for (int i = 0; i < 4; i++)
        system.Root.Send(actor, CreateTestDomainEvent());

    // No flush yet (below threshold)
    await Task.Delay(100);
    var stats = await GetBufferStats(actor);
    Assert.Equal(4, stats.BufferedCount);
}

[Fact]
public async Task AnalyticsAggregator_FlushesAtThreshold()
{
    var s3Mock = new Mock<IS3Client>();
    var actor = SpawnWithMock(s3Mock, flushThreshold: 3);

    // Send 3 events (hits threshold)
    for (int i = 0; i < 3; i++)
        system.Root.Send(actor, CreateTestDomainEvent());

    await Task.Delay(500);
    s3Mock.Verify(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
}

[Fact]
public async Task AnalyticsAggregator_FlushesOnTimeout()
{
    var s3Mock = new Mock<IS3Client>();
    var actor = SpawnWithMock(s3Mock, flushThreshold: 1000,
        flushInterval: TimeSpan.FromMilliseconds(200));

    system.Root.Send(actor, CreateTestDomainEvent());

    // Wait for timeout flush
    await Task.Delay(500);
    s3Mock.Verify(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
}

[Fact]
public async Task AnalyticsAggregator_AnonymizesPii()
{
    var captured = new List<byte[]>();
    var s3Mock = SetupCapture(captured);
    var actor = SpawnWithMock(s3Mock, flushThreshold: 1);

    system.Root.Send(actor, new ConceptAttempted_V1("student-123", ...));
    await Task.Delay(500);

    var parquetData = captured[0];
    var content = Encoding.UTF8.GetString(parquetData);
    Assert.DoesNotContain("student-123", content); // PII anonymized
}
```

---

### ACT-016.3: DeadLetterWatcher (Monitoring, Quarantine)
**Files:**
- `src/Cena.Actors/Topology/DeadLetterWatcher.cs` — dead letter monitoring actor

**Acceptance:**
- [ ] On `Started`: subscribes to `context.System.EventStream.Subscribe<DeadLetterEvent>()`
- [ ] On each dead letter:
  - Increment `cena.deadletters.total` counter with tags: `message.type`, `target.pid`
  - Track message-type frequency in `ConcurrentDictionary<string, int> _poisonMessageCounts`
  - Key format: `"{MessageTypeName}:{PID}"`
- [ ] Poison message detection: if same key reaches `QuarantineThreshold` (3): log ERROR with full message context, publish alert to NATS subject `cena.alerts.poison_message`
- [ ] On `GetDeadLetterStats` query: respond with `DeadLetterStats(PoisonMessages, TotalDeadLetters)` where `PoisonMessages` = entries with count >= threshold
- [ ] Rate alert: if dead letters exceed 100 per minute, log CRITICAL and publish to `cena.alerts.dead_letter_storm`

**Test:**
```csharp
[Fact]
public async Task DeadLetterWatcher_CountsDeadLetters()
{
    var system = new ActorSystem();
    var watcher = system.Root.SpawnNamed(
        Props.FromProducer(() => new DeadLetterWatcher()),
        "dead-letter-watcher");

    // Simulate dead letters via event stream
    system.EventStream.Publish(new DeadLetterEvent(
        PID.FromAddress("test", "dead-actor"),
        new TestMessage("hello"),
        null));

    await Task.Delay(200);

    var stats = await system.Root.RequestAsync<DeadLetterStats>(
        watcher, new GetDeadLetterStats(), TimeSpan.FromSeconds(5));

    Assert.Equal(1, stats.TotalDeadLetters);
}

[Fact]
public async Task DeadLetterWatcher_DetectsPoisonMessages()
{
    var system = new ActorSystem();
    var watcher = SpawnWatcher(system);

    var pid = PID.FromAddress("test", "problematic-actor");

    // Send same message type to same PID 3 times (quarantine threshold)
    for (int i = 0; i < 3; i++)
    {
        system.EventStream.Publish(new DeadLetterEvent(pid, new BadMessage(), null));
    }

    await Task.Delay(200);

    var stats = await system.Root.RequestAsync<DeadLetterStats>(
        watcher, new GetDeadLetterStats(), TimeSpan.FromSeconds(5));

    Assert.True(stats.PoisonMessages.Count > 0);
    Assert.Contains("BadMessage", stats.PoisonMessages.Keys.First());
}

[Fact]
public async Task DeadLetterWatcher_IgnoresNonRepeatingDeadLetters()
{
    var system = new ActorSystem();
    var watcher = SpawnWatcher(system);

    // Different message types — no poison pattern
    system.EventStream.Publish(new DeadLetterEvent(
        PID.FromAddress("test", "a1"), new MsgTypeA(), null));
    system.EventStream.Publish(new DeadLetterEvent(
        PID.FromAddress("test", "a2"), new MsgTypeB(), null));

    await Task.Delay(200);

    var stats = await system.Root.RequestAsync<DeadLetterStats>(
        watcher, new GetDeadLetterStats(), TimeSpan.FromSeconds(5));

    Assert.Empty(stats.PoisonMessages);
    Assert.Equal(2, stats.TotalDeadLetters);
}
```

---

### ACT-016.4: OutreachDispatcherActor (Fan-Out Router)
**Files:**
- `src/Cena.Actors/Topology/OutreachDispatcherActor.cs` — fan-out router

**Acceptance:**
- [ ] On `Started`: spawns four channel worker pools:
  - WhatsApp: `Router.NewRoundRobinPool(props, 3)` — 3 workers
  - Telegram: `Router.NewRoundRobinPool(props, 2)` — 2 workers
  - Push: `Router.NewRoundRobinPool(props, 2)` — 2 workers
  - Voice: single actor (no pool) — 1 worker
- [ ] On `DispatchOutreach` message: routes to the appropriate pool based on `msg.Channel`:
  - "whatsapp" -> WhatsApp pool
  - "telegram" -> Telegram pool
  - "push" -> Push pool
  - "voice" -> Voice worker
  - Unknown channel -> defaults to Push pool
- [ ] Message record: `DispatchOutreach(string StudentId, string Channel, string TriggerType, string ContentHash, int Priority)`
- [ ] Worker PIDs stored as private fields for health monitoring

**Test:**
```csharp
[Fact]
public async Task OutreachDispatcher_RoutesToCorrectChannel()
{
    var whatsappReceived = new TaskCompletionSource<DispatchOutreach>();
    var telegramReceived = new TaskCompletionSource<DispatchOutreach>();

    var dispatcher = SpawnWithMockWorkers(
        onWhatsApp: msg => whatsappReceived.SetResult(msg),
        onTelegram: msg => telegramReceived.SetResult(msg));

    system.Root.Send(dispatcher,
        new DispatchOutreach("s1", "whatsapp", "review_due", "hash1", 1));
    system.Root.Send(dispatcher,
        new DispatchOutreach("s2", "telegram", "streak_expiring", "hash2", 2));

    var wa = await whatsappReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var tg = await telegramReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal("s1", wa.StudentId);
    Assert.Equal("s2", tg.StudentId);
}

[Fact]
public async Task OutreachDispatcher_UnknownChannel_DefaultsToPush()
{
    var pushReceived = new TaskCompletionSource<DispatchOutreach>();
    var dispatcher = SpawnWithMockWorkers(onPush: msg => pushReceived.SetResult(msg));

    system.Root.Send(dispatcher,
        new DispatchOutreach("s3", "carrier_pigeon", "test", "hash3", 1));

    var push = await pushReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("s3", push.StudentId);
}

[Fact]
public async Task OutreachDispatcher_WhatsAppPool_RoundRobins()
{
    var workerHits = new ConcurrentDictionary<string, int>();
    var dispatcher = SpawnWithTrackedWorkers(workerHits, whatsAppPoolSize: 3);

    // Send 6 messages to WhatsApp
    for (int i = 0; i < 6; i++)
    {
        system.Root.Send(dispatcher,
            new DispatchOutreach($"s{i}", "whatsapp", "test", $"hash{i}", 1));
    }

    await Task.Delay(500);

    // Each of the 3 workers should have received 2 messages
    Assert.Equal(3, workerHits.Count);
    Assert.True(workerHits.Values.All(v => v == 2));
}
```

---

### ACT-016.5: Channel Worker Actors (WhatsApp, Telegram, Push, Voice)
**Files:**
- `src/Cena.Actors/Topology/Workers/WhatsAppWorkerActor.cs`
- `src/Cena.Actors/Topology/Workers/TelegramWorkerActor.cs`
- `src/Cena.Actors/Topology/Workers/PushNotificationWorkerActor.cs`
- `src/Cena.Actors/Topology/Workers/VoiceCallWorkerActor.cs`

**Acceptance (shared pattern, all four workers):**
- [ ] Each implements `IActor` with `ReceiveAsync(IContext context)`
- [ ] On `DispatchOutreach`:
  1. Resolve student contact info from `IStudentContactService` (injected)
  2. Build channel-specific message from `ContentHash` via `IContentService`
  3. Send via channel SDK/API client (injected interface)
  4. On success: publish `OutreachMessageSent_V1` event to NATS
  5. On failure: log WARNING, retry up to 3 times with exponential backoff (1s, 2s, 4s)
  6. On permanent failure (after 3 retries): log ERROR, publish to `cena.outreach.failures`
- [ ] Quiet hours enforcement: check `IQuietHoursService.IsQuietHours(studentTimezone)` before sending. If quiet hours: queue for later delivery
- [ ] Metrics per worker: `cena.outreach.{channel}.sent_total`, `cena.outreach.{channel}.failed_total`, `cena.outreach.{channel}.latency_ms`

**Channel-specific requirements:**

**WhatsAppWorkerActor:**
- [ ] Uses WhatsApp Business API client (`IWhatsAppClient`)
- [ ] Supports template messages (required by WhatsApp policy for first contact)
- [ ] Rate limit: 80 messages/second shared across pool

**TelegramWorkerActor:**
- [ ] Uses Telegram Bot API client (`ITelegramClient`)
- [ ] Supports inline keyboard for quick review responses
- [ ] Rate limit: 30 messages/second per bot token

**PushNotificationWorkerActor:**
- [ ] Uses FCM/APNS via unified push service (`IPushService`)
- [ ] Supports rich notifications with action buttons
- [ ] Handles token invalidation: on `410 Gone`, remove device token

**VoiceCallWorkerActor:**
- [ ] Uses Twilio Voice API (`IVoiceCallClient`)
- [ ] Used only for critical escalations (stagnation-at-risk students)
- [ ] Rate limit: 1 concurrent call (single worker, no pool)
- [ ] Call duration capped at 2 minutes

**Test:**
```csharp
[Fact]
public async Task WhatsAppWorker_SendsAndPublishesEvent()
{
    var whatsappMock = new Mock<IWhatsAppClient>();
    whatsappMock.Setup(w => w.SendTemplateAsync(It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync(new SendResult(true, "msg-123"));

    var natsMock = new Mock<INatsConnection>();
    var worker = SpawnWhatsAppWorker(whatsappMock, natsMock);

    system.Root.Send(worker,
        new DispatchOutreach("student-1", "whatsapp", "review_due", "hash1", 1));

    await Task.Delay(500);

    whatsappMock.Verify(w => w.SendTemplateAsync(
        It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
    natsMock.Verify(n => n.PublishAsync(
        It.Is<string>(s => s.Contains("outreach")), It.IsAny<byte[]>(), default), Times.Once);
}

[Fact]
public async Task PushWorker_HandlesTokenInvalidation()
{
    var pushMock = new Mock<IPushService>();
    pushMock.Setup(p => p.SendAsync(It.IsAny<PushMessage>(), default))
        .ThrowsAsync(new DeviceTokenInvalidException("token-abc"));

    var contactMock = new Mock<IStudentContactService>();
    var worker = SpawnPushWorker(pushMock, contactMock);

    system.Root.Send(worker,
        new DispatchOutreach("student-2", "push", "streak_expiring", "hash2", 1));

    await Task.Delay(500);

    contactMock.Verify(c => c.RemoveDeviceTokenAsync("student-2", "token-abc", default), Times.Once);
}

[Fact]
public async Task Worker_RespectsQuietHours()
{
    var quietMock = new Mock<IQuietHoursService>();
    quietMock.Setup(q => q.IsQuietHours(It.IsAny<string>())).Returns(true);

    var whatsappMock = new Mock<IWhatsAppClient>();
    var worker = SpawnWhatsAppWorker(whatsappMock, quietHours: quietMock);

    system.Root.Send(worker,
        new DispatchOutreach("student-3", "whatsapp", "review_due", "hash3", 1));

    await Task.Delay(500);

    // Should NOT have sent (quiet hours)
    whatsappMock.Verify(w => w.SendTemplateAsync(
        It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
}

[Fact]
public async Task Worker_RetriesOnTransientFailure()
{
    var callCount = 0;
    var whatsappMock = new Mock<IWhatsAppClient>();
    whatsappMock.Setup(w => w.SendTemplateAsync(It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync(() =>
        {
            callCount++;
            if (callCount < 3) throw new HttpRequestException("transient");
            return new SendResult(true, "msg-456");
        });

    var worker = SpawnWhatsAppWorker(whatsappMock);
    system.Root.Send(worker,
        new DispatchOutreach("student-4", "whatsapp", "review_due", "hash4", 1));

    await Task.Delay(TimeSpan.FromSeconds(8)); // Allow retries

    Assert.Equal(3, callCount); // 2 failures + 1 success
}
```

---

## Integration Test (full outreach flow)

```csharp
[Fact]
public async Task OutreachFlow_EndToEnd()
{
    // Setup: system manager bootstraps all actors
    var system = CreateTestActorSystem();
    var manager = SpawnSystemManager(system);

    await Task.Delay(TimeSpan.FromSeconds(2)); // Bootstrap completes

    // 1. Dispatch outreach through the system
    var outreachPid = system.Root.GetChild(manager, "outreach-dispatcher");
    system.Root.Send(outreachPid!,
        new DispatchOutreach("student-1", "push", "review_due", "hash1", 1));

    // 2. Verify analytics aggregator received the event
    var analyticsPid = system.Root.GetChild(manager, "analytics-aggregator");
    // (analytics buffers domain events — verify via stats)

    // 3. Verify dead letter watcher is monitoring
    var watcherPid = system.Root.GetChild(manager, "dead-letter-watcher");
    var stats = await system.Root.RequestAsync<DeadLetterStats>(
        watcherPid!, new GetDeadLetterStats(), TimeSpan.FromSeconds(5));
    Assert.Equal(0, stats.TotalDeadLetters); // No dead letters in healthy flow
}
```

## Rollback Criteria
- If ActorSystemManager bootstrap fails: fall back to manual DI registration (no actor hierarchy)
- If analytics flush loses events: switch to synchronous write-through to PostgreSQL (slower but durable)
- If outreach workers overload external APIs: reduce pool sizes and add back-pressure

## Definition of Done
- [ ] All 5 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=Topology"` -- 0 failures
- [ ] System bootstrap completes in < 5 seconds
- [ ] Analytics aggregator handles 10K events/minute without memory pressure
- [ ] Dead letter watcher detects poison messages within 3 occurrences
- [ ] All outreach channels deliverable in staging environment
- [ ] PR reviewed by architect
