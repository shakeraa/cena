# SES-001: SignalR Hub — Student Session Real-Time Bridge

**Priority:** P0 — no real-time student-facing communication layer exists
**Blocked by:** MSG-001 (messaging context), INF-003 (NATS)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/signalr-messages.ts`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The Cena platform has a full actor system (Proto.Actor) managing learning sessions, tutoring conversations, and messaging — but **no WebSocket/SignalR layer** exists to push events to browser clients. NATS is internal only. Students, teachers, and parents using web/mobile clients have no way to receive real-time updates. This task creates the ASP.NET Core SignalR hub that bridges Proto.Actor events to browser clients.

## Architecture

```
Browser (React PWA) ←→ SignalR Hub ←→ NATS Subscriber ←→ Proto.Actor Cluster
                                   ←→ Direct Actor Request (commands)
```

- **Commands** (client → server): Hub receives typed commands, routes to Proto.Actor virtual actors via `ActorSystem.Root.RequestAsync()`
- **Events** (server → client): NATS subscriber listens to `cena.events.*`, pushes to connected SignalR clients via `IHubContext<CenaHub>`
- **Authentication**: Firebase JWT bearer token via `accessTokenFactory`

## Subtasks

### SES-001.1: CenaHub — Hub Definition & Command Routing

**Files:**
- `src/api/Cena.Api.Host/Hubs/CenaHub.cs` — SignalR hub
- `src/api/Cena.Api.Host/Hubs/HubContracts.cs` — typed hub interfaces (ICenaClient, ICenaHub)

**Acceptance:**
- [ ] `CenaHub : Hub<ICenaClient>` with `[Authorize]` attribute
- [ ] `ICenaClient` interface defines all server→client methods matching `ServerEvent` types from contract
- [ ] Command methods: `StartSession`, `SubmitAnswer`, `EndSession`, `RequestHint`, `SkipQuestion`, `AddAnnotation`, `SwitchApproach`, `RequestNextConcept`
- [ ] Each command extracts `studentId` from JWT claims, routes to virtual actor via cluster
- [ ] `MessageEnvelope<T>` wrapper with `correlationId`, `timestamp`, `type` fields
- [ ] Response sent back to caller via `Clients.Caller` with matching `correlationId`
- [ ] `OnConnectedAsync` logs connection, adds to student group (`Groups.AddToGroupAsync`)
- [ ] `OnDisconnectedAsync` cleans up, notifies session actor of disconnect
- [ ] Rate limiting: max 10 commands/second per connection

### SES-001.2: NATS → SignalR Event Bridge

**Files:**
- `src/api/Cena.Api.Host/Hubs/NatsSignalRBridge.cs` — `BackgroundService` that subscribes to NATS events and pushes to SignalR clients
- `src/api/Cena.Api.Host/Hubs/SignalRGroupManager.cs` — maps studentId → connectionId groups

**Acceptance:**
- [ ] Subscribes to `cena.events.student.{studentId}.*` per connected student
- [ ] Routes events to correct SignalR group by `studentId`
- [ ] Event types bridged: `SessionStarted`, `QuestionPresented`, `AnswerEvaluated`, `MasteryUpdated`, `MethodologySwitched`, `SessionSummary`, `XpAwarded`, `StreakUpdated`, `KnowledgeGraphUpdated`, `CognitiveLoadWarning`, `HintDelivered`, `StagnationDetected`
- [ ] Tutoring events bridged: `TutoringStarted`, `TutorMessage`, `TutoringEnded`
- [ ] Handles NATS reconnection gracefully (resubscribe on reconnect)
- [ ] Unsubscribes per-student NATS subjects when student disconnects

### SES-001.3: Hub Registration & Middleware

**Files:**
- `src/api/Cena.Api.Host/Program.cs` — add SignalR services and hub mapping
- `src/api/Cena.Api.Host/Hubs/SignalRConfiguration.cs` — configuration extension methods

**Acceptance:**
- [ ] `builder.Services.AddSignalR()` with JSON protocol
- [ ] `app.MapHub<CenaHub>("/hub/cena")` — hub endpoint
- [ ] CORS configured for web client origins
- [ ] Firebase JWT authentication integrated with SignalR `accessTokenFactory`
- [ ] Health check endpoint for SignalR: `GET /health/signalr`
- [ ] Connection limit: max 1 active connection per studentId (disconnect stale)

## Tests

```csharp
[Fact]
public async Task StartSession_RoutesToStudentActor()
{
    // Arrange: connect with JWT for student-123
    var hub = CreateTestHub(studentId: "student-123");

    // Act
    await hub.StartSession(new StartSessionCommand("math", null, DeviceInfo.Default));

    // Assert: actor received StartSession message
    _actorSystem.Verify(a => a.RequestAsync<SessionStartedEvent>(
        "student-123", It.IsAny<StartSession>()), Times.Once);
}

[Fact]
public async Task NatsBridge_PushesEventsToCorrectClient()
{
    // Arrange: student-123 connected to SignalR
    var connection = await ConnectAsStudent("student-123");
    var received = new TaskCompletionSource<QuestionPresentedEvent>();
    connection.On<QuestionPresentedEvent>("QuestionPresented", e => received.SetResult(e));

    // Act: publish NATS event
    await _nats.PublishAsync("cena.events.student.student-123.question_presented", testEvent);

    // Assert
    var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("q-1", result.QuestionId);
}
```

## Rollback Criteria
- If SignalR scaling issues: add Redis backplane (`AddStackExchangeRedis`)
- If mobile clients need different protocol: add raw WebSocket endpoint alongside
- If NATS subscription per-student is too many: switch to wildcard `cena.events.student.>` with client-side filtering

## Definition of Done
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` — all hub tests pass
- [ ] SignalR hub accepts connections at `/hub/cena` with Firebase JWT
- [ ] Commands route to Proto.Actor virtual actors
- [ ] NATS events push to connected SignalR clients in <100ms
- [ ] Rate limiting enforced (10 cmd/s)
- [ ] Max 1 connection per student enforced
