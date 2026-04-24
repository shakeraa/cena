# MOB-003: WebSocket Service (SignalR-Compatible)

**Priority:** P1 — real-time communication backbone
**Blocked by:** MOB-002 (domain models for message types)
**Estimated effort:** 4 days
**Contract:** `contracts/mobile/lib/core/services/websocket_service.dart`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Cena's real-time learning loop depends on a persistent WebSocket connection to a .NET 9 SignalR hub. The service must handle automatic reconnection with exponential backoff + jitter, heartbeat ping/pong, message framing via `MessageEnvelope`, and offline command queuing. The `MessageRouter` dispatches incoming events by `target` name to typed handlers. All 8 client commands (`StartSession`, `AttemptConcept`, `EndSession`, `RequestHint`, `SkipQuestion`, `AddAnnotation`, `SwitchApproach`, `SensorUpdate`) and 10 server events (`QuestionPresented`, `AnswerEvaluated`, `MasteryUpdated`, `MethodologySwitched`, `SessionSummary`, `XpAwarded`, `StreakUpdated`, `KnowledgeGraphUpdated`, `CognitiveLoadWarning`, `FocusStateUpdated`) must be routed correctly.

> **FOC-002 dependency:** The `SensorUpdate` command carries the `SensorSnapshot` from the mobile sensor layer (FOC-002) to the server's `FocusInput` (FOC-001). The `FocusStateUpdated` event returns the computed focus state. See [FOC-002](../focus/FOC-002-mobile-sensor-collection.md) for the field mapping.

## Subtasks

### MOB-003.1: Connection Lifecycle & Reconnection
**Files:**
- `lib/core/services/websocket_service_impl.dart`
- `lib/core/services/reconnection_strategy.dart`

**Acceptance:**
- [ ] `WebSocketServiceImpl implements WebSocketService` using `web_socket_channel` package
- [ ] `connect()` establishes a WebSocket connection to the provided `url` with bearer `authToken`
- [ ] Connection state transitions follow the contract: `disconnected` -> `connecting` -> `handshaking` -> `connected`
- [ ] `ConnectionState` enum: `disconnected`, `connecting`, `handshaking`, `connected`, `reconnecting`, `failed`
- [ ] `connectionState` stream emits on every state transition
- [ ] `currentState` getter returns the synchronous snapshot
- [ ] `disconnect()` sends a close frame, stops timers, transitions to `disconnected`
- [ ] `isConnected` returns true only when `currentState == ConnectionState.connected`
- [ ] `ReconnectionStrategy` class implements exponential backoff with jitter per `ReconnectionConfig`:
  - `initialDelay`: default 1 second
  - `maxDelay`: default 30 seconds
  - `backoffMultiplier`: default 2.0
  - `maxAttempts`: default 10
  - `jitterFactor`: default 0.2
- [ ] On unexpected disconnect: state transitions to `reconnecting`, strategy computes delay, reattempts
- [ ] After `maxAttempts` failures: state transitions to `failed`
- [ ] Successful reconnect: state transitions to `connected`, resubscribes to message stream
- [ ] Jitter formula: `delay * (1 + random.nextDouble() * jitterFactor)` to prevent thundering herd

**Test:**
```dart
test('exponential backoff computes correct delays', () {
  final strategy = ReconnectionStrategy(
    config: ReconnectionConfig(
      initialDelay: Duration(seconds: 1),
      maxDelay: Duration(seconds: 30),
      backoffMultiplier: 2.0,
      maxAttempts: 10,
      jitterFactor: 0.0, // no jitter for deterministic test
    ),
  );
  expect(strategy.delayForAttempt(0).inSeconds, equals(1));
  expect(strategy.delayForAttempt(1).inSeconds, equals(2));
  expect(strategy.delayForAttempt(2).inSeconds, equals(4));
  expect(strategy.delayForAttempt(3).inSeconds, equals(8));
  expect(strategy.delayForAttempt(4).inSeconds, equals(16));
  expect(strategy.delayForAttempt(5).inSeconds, equals(30)); // capped at maxDelay
  expect(strategy.delayForAttempt(6).inSeconds, equals(30));
});

test('jitter adds randomness within bounds', () {
  final strategy = ReconnectionStrategy(
    config: ReconnectionConfig(jitterFactor: 0.2),
  );
  final delays = List.generate(100, (_) => strategy.delayForAttempt(0));
  final minExpected = Duration(milliseconds: 1000); // base
  final maxExpected = Duration(milliseconds: 1200); // base * (1 + 0.2)
  for (final d in delays) {
    expect(d.inMilliseconds, greaterThanOrEqualTo(minExpected.inMilliseconds));
    expect(d.inMilliseconds, lessThanOrEqualTo(maxExpected.inMilliseconds));
  }
});

test('connection state transitions on connect/disconnect', () async {
  final service = WebSocketServiceImpl();
  final states = <ConnectionState>[];
  service.connectionState.listen(states.add);

  await service.connect(url: 'wss://test', authToken: 'token');
  expect(states, containsAllInOrder([
    ConnectionState.connecting,
    ConnectionState.handshaking,
    ConnectionState.connected,
  ]));

  await service.disconnect();
  expect(states.last, equals(ConnectionState.disconnected));
});

test('reconnection attempts up to maxAttempts then fails', () async {
  final service = WebSocketServiceImpl();
  final config = ReconnectionConfig(maxAttempts: 3, initialDelay: Duration(milliseconds: 10));
  final states = <ConnectionState>[];
  service.connectionState.listen(states.add);

  // Simulate repeated connection failures
  await service.connect(url: 'wss://unreachable', authToken: 'token', reconnectionConfig: config);
  await Future.delayed(Duration(milliseconds: 200));

  expect(states, contains(ConnectionState.reconnecting));
  expect(states.last, equals(ConnectionState.failed));
});
```

**Edge Cases:**
- Server closes connection with 1001 (Going Away) — treat as transient, reconnect
- Server closes with 4001 (Unauthorized) — do NOT reconnect, emit `failed` with auth error
- App goes to background on iOS — connection may be suspended by OS; detect foreground return and reconnect
- Multiple rapid `connect()` calls — debounce, only process the latest

---

### MOB-003.2: Message Envelope & Serialization
**Files:**
- `lib/core/services/message_envelope.dart` (if separating from contract)
- `lib/core/services/websocket_service_impl.dart` (send method)

**Acceptance:**
- [ ] `send()` wraps payload in `MessageEnvelope` with `target`, `invocationId` (UUID if not provided), `arguments` (single-element list), and `timestamp`
- [ ] Envelope JSON matches SignalR protocol: `{"type": 1, "target": "...", "invocationId": "...", "arguments": [...]}`
- [ ] Messages end with the SignalR record separator character `\x1e`
- [ ] `onMessage` stream parses incoming JSON frames into `MessageEnvelope` instances
- [ ] If disconnected, `send()` queues the message for send-on-reconnect (in-memory queue, not durable — durable queue is MOB-004)
- [ ] Queued messages are flushed in FIFO order upon reconnection
- [ ] `invocationId` is auto-generated as UUID v4 if not provided

**Test:**
```dart
test('send wraps payload in SignalR-compatible envelope', () async {
  final mockChannel = MockWebSocketChannel();
  final service = WebSocketServiceImpl(channelFactory: (_) => mockChannel);
  await service.connect(url: 'wss://test', authToken: 'token');

  await service.send(
    target: 'AttemptConcept',
    payload: {'exerciseId': 'ex-001', 'answer': 'x=2'},
  );

  final sent = verify(() => mockChannel.sink.add(captureAny)).captured.single;
  final json = jsonDecode(sent.replaceAll('\x1e', '')) as Map<String, dynamic>;
  expect(json['target'], equals('AttemptConcept'));
  expect(json['arguments'], isList);
  expect(json['arguments'][0]['exerciseId'], equals('ex-001'));
  expect(json['invocationId'], isNotNull);
});

test('incoming message parsed to MessageEnvelope', () async {
  final mockChannel = MockWebSocketChannel();
  final service = WebSocketServiceImpl(channelFactory: (_) => mockChannel);
  await service.connect(url: 'wss://test', authToken: 'token');

  // Simulate server message
  mockChannel.emitMessage(
    '{"type":1,"target":"QuestionPresented","invocationId":"inv-001",'
    '"arguments":[{"exercise":{"id":"ex-001","conceptId":"c-001",'
    '"questionType":"mcq","difficulty":5,"content":"Solve x"}},'
    '"sessionId":"s-001","questionNumber":1,"estimatedRemaining":9]}\x1e',
  );

  final envelope = await service.onMessage.first;
  expect(envelope.target, equals('QuestionPresented'));
  expect(envelope.invocationId, equals('inv-001'));
  expect(envelope.arguments.first['exercise']['id'], equals('ex-001'));
});

test('messages queued when disconnected are flushed on reconnect', () async {
  final mockChannel = MockWebSocketChannel();
  final service = WebSocketServiceImpl(channelFactory: (_) => mockChannel);

  // Send while disconnected
  await service.send(target: 'AddAnnotation', payload: {'text': 'note'});
  expect(service.isConnected, isFalse);

  // Connect — queued message should flush
  await service.connect(url: 'wss://test', authToken: 'token');
  verify(() => mockChannel.sink.add(any)).called(1);
});
```

**Edge Cases:**
- Malformed JSON from server — catch `FormatException`, log error, do not crash stream
- Server sends batch of multiple messages in one frame (separated by `\x1e`) — split and parse each
- Empty frames or heartbeat-only frames — filter out, do not emit on `onMessage`

---

### MOB-003.3: Heartbeat Ping/Pong
**Files:**
- `lib/core/services/heartbeat_monitor.dart`

**Acceptance:**
- [ ] `HeartbeatMonitor` sends periodic ping frames at `heartbeatInterval` (default 15 seconds)
- [ ] If pong not received within `heartbeatTimeout` (default 10 seconds), connection is considered dead
- [ ] Dead connection triggers reconnection sequence
- [ ] Heartbeat timer starts after successful handshake, stops on disconnect
- [ ] Heartbeat uses SignalR ping format: `{"type": 6}\x1e`
- [ ] Incoming pong resets the timeout timer
- [ ] Heartbeat is paused when app is in background (no wasted battery)

**Test:**
```dart
test('heartbeat detects dead connection', () {
  fakeAsync((async) {
    final monitor = HeartbeatMonitor(
      interval: Duration(seconds: 15),
      timeout: Duration(seconds: 10),
      onDead: () => deadCallCount++,
    );
    monitor.start(mockSink);

    // Advance past interval + timeout without pong
    async.elapse(Duration(seconds: 26));
    expect(deadCallCount, equals(1));
  });
});

test('pong resets timeout', () {
  fakeAsync((async) {
    int deadCallCount = 0;
    final monitor = HeartbeatMonitor(
      interval: Duration(seconds: 15),
      timeout: Duration(seconds: 10),
      onDead: () => deadCallCount++,
    );
    monitor.start(mockSink);

    // First ping sent at 15s
    async.elapse(Duration(seconds: 15));
    // Pong received at 16s
    async.elapse(Duration(seconds: 1));
    monitor.onPongReceived();

    // Advance another full cycle — should NOT be dead
    async.elapse(Duration(seconds: 15));
    monitor.onPongReceived();

    expect(deadCallCount, equals(0));
  });
});

test('heartbeat sends SignalR ping format', () {
  fakeAsync((async) {
    final monitor = HeartbeatMonitor(
      interval: Duration(seconds: 15),
      timeout: Duration(seconds: 10),
      onDead: () {},
    );
    monitor.start(mockSink);

    async.elapse(Duration(seconds: 15));
    verify(() => mockSink.add('{"type":6}\x1e')).called(1);
  });
});
```

**Edge Cases:**
- Network switches (WiFi -> cellular) may cause temporary disconnects without close frame — heartbeat catches this
- Server is alive but slow — allow configurable timeout; default 10s is generous
- Background/foreground transitions — pause/resume heartbeat to avoid false positives

---

### MOB-003.4: Message Router & Typed Convenience Senders
**Files:**
- `lib/core/services/message_router_impl.dart`
- `lib/core/services/websocket_service_impl.dart` (convenience methods)

**Acceptance:**
- [ ] `MessageRouterImpl implements MessageRouter` — maintains `Map<String, _HandlerEntry>` of target -> handler+fromJson
- [ ] `on<T>()` registers a typed handler for a target name with its `fromJson` factory
- [ ] `dispatch()` looks up target in the map, deserializes `arguments[0]` via `fromJson`, calls handler
- [ ] Unknown targets are logged as warnings but do not throw
- [ ] `clear()` removes all registered handlers
- [ ] All `CommandTargets` constants match contract: `StartSession`, `AttemptConcept`, `EndSession`, `RequestHint`, `SkipQuestion`, `AddAnnotation`, `SwitchApproach`, `SensorUpdate`
- [ ] All `EventTargets` constants match contract: `QuestionPresented`, `AnswerEvaluated`, `MasteryUpdated`, `MethodologySwitched`, `SessionSummary`, `XpAwarded`, `StreakUpdated`, `KnowledgeGraphUpdated`, `CognitiveLoadWarning`, `FocusStateUpdated`
- [ ] Convenience senders on `WebSocketServiceImpl`: `startSession(StartSession)`, `attemptConcept(AttemptConcept)`, `endSession(EndSession)`, `requestHint(RequestHint)`, `skipQuestion(SkipQuestion)`, `addAnnotation(AddAnnotation)`, `switchApproach(SwitchApproach)`, `sensorUpdate(SensorUpdate)`
- [ ] Each convenience sender calls `send()` with the correct `target` from `CommandTargets` and serialized payload

**Test:**
```dart
test('MessageRouter dispatches to registered handler', () {
  final router = MessageRouterImpl();
  QuestionPresented? received;

  router.on<QuestionPresented>(
    'QuestionPresented',
    (event) => received = event,
    QuestionPresented.fromJson,
  );

  router.dispatch(MessageEnvelope(
    target: 'QuestionPresented',
    invocationId: 'inv-1',
    arguments: [{
      'exercise': {
        'id': 'ex-001',
        'conceptId': 'c-001',
        'questionType': 'mcq',
        'difficulty': 5,
        'content': 'Solve x',
      },
      'sessionId': 's-001',
      'questionNumber': 1,
      'estimatedRemaining': 9,
    }],
    timestamp: DateTime.now(),
  ));

  expect(received, isNotNull);
  expect(received!.exercise.id, equals('ex-001'));
  expect(received!.questionNumber, equals(1));
});

test('unknown target does not throw', () {
  final router = MessageRouterImpl();
  expect(
    () => router.dispatch(MessageEnvelope(
      target: 'UnknownEvent',
      invocationId: 'inv-1',
      arguments: [{}],
      timestamp: DateTime.now(),
    )),
    returnsNormally,
  );
});

test('convenience sender startSession sends correct target', () async {
  final mockChannel = MockWebSocketChannel();
  final service = WebSocketServiceImpl(channelFactory: (_) => mockChannel);
  await service.connect(url: 'wss://test', authToken: 'token');

  await service.startSession(StartSession(
    studentId: 'stu-001',
    subject: Subject.math,
    durationMinutes: 25,
  ));

  final sent = verify(() => mockChannel.sink.add(captureAny)).captured.single;
  final json = jsonDecode(sent.replaceAll('\x1e', ''));
  expect(json['target'], equals('StartSession'));
  expect(json['arguments'][0]['studentId'], equals('stu-001'));
  expect(json['arguments'][0]['subject'], equals('math'));
});

test('all command targets match contract constants', () {
  expect(CommandTargets.startSession, equals('StartSession'));
  expect(CommandTargets.attemptConcept, equals('AttemptConcept'));
  expect(CommandTargets.endSession, equals('EndSession'));
  expect(CommandTargets.requestHint, equals('RequestHint'));
  expect(CommandTargets.skipQuestion, equals('SkipQuestion'));
  expect(CommandTargets.addAnnotation, equals('AddAnnotation'));
  expect(CommandTargets.switchApproach, equals('SwitchApproach'));
  expect(CommandTargets.sensorUpdate, equals('SensorUpdate'));
});

test('all event targets match contract constants', () {
  expect(EventTargets.questionPresented, equals('QuestionPresented'));
  expect(EventTargets.answerEvaluated, equals('AnswerEvaluated'));
  expect(EventTargets.masteryUpdated, equals('MasteryUpdated'));
  expect(EventTargets.methodologySwitched, equals('MethodologySwitched'));
  expect(EventTargets.sessionSummary, equals('SessionSummary'));
  expect(EventTargets.xpAwarded, equals('XpAwarded'));
  expect(EventTargets.streakUpdated, equals('StreakUpdated'));
  expect(EventTargets.knowledgeGraphUpdated, equals('KnowledgeGraphUpdated'));
  expect(EventTargets.cognitiveLoadWarning, equals('CognitiveLoadWarning'));
  expect(EventTargets.focusStateUpdated, equals('FocusStateUpdated'));
});

test('router.clear removes all handlers', () {
  final router = MessageRouterImpl();
  bool called = false;
  router.on<XpAwarded>('XpAwarded', (_) => called = true, XpAwarded.fromJson);
  router.clear();
  router.dispatch(MessageEnvelope(
    target: 'XpAwarded',
    invocationId: 'x',
    arguments: [{'amount': 10, 'reason': 'correct', 'totalXp': 100, 'currentLevel': 2}],
    timestamp: DateTime.now(),
  ));
  expect(called, isFalse);
});
```

**Edge Cases:**
- Handler throws an exception — catch it, log the error, do not break the dispatch loop for other events
- Multiple handlers registered for the same target — last registration wins (or document multi-handler support)
- `arguments` list is empty — log warning, do not call handler

---

## Integration Test

```dart
void main() {
  group('MOB-003 Integration: Full WebSocket lifecycle', () {
    test('connect -> send command -> receive event -> disconnect', () async {
      final mockServer = MockSignalRServer();
      final service = WebSocketServiceImpl(
        channelFactory: (url) => mockServer.createChannel(),
      );

      // Connect
      await service.connect(url: 'wss://test', authToken: 'token');
      expect(service.isConnected, isTrue);

      // Register event handler
      final router = MessageRouterImpl();
      AnswerEvaluated? result;
      router.on<AnswerEvaluated>(
        EventTargets.answerEvaluated,
        (event) => result = event,
        AnswerEvaluated.fromJson,
      );
      service.onMessage.listen(router.dispatch);

      // Send command
      await service.attemptConcept(AttemptConcept(
        sessionId: 's-001',
        exerciseId: 'ex-001',
        conceptId: 'c-001',
        answer: 'x=2',
        timeSpentMs: 15000,
        idempotencyKey: 'key-001',
      ));

      // Server responds
      mockServer.sendEvent(EventTargets.answerEvaluated, {
        'exerciseId': 'ex-001',
        'result': {
          'isCorrect': true,
          'errorType': 'none',
          'priorMastery': 0.65,
          'posteriorMastery': 0.78,
          'feedback': 'נכון! פתרת נכון',
          'xpEarned': 10,
        },
      });

      await Future.delayed(Duration(milliseconds: 50));
      expect(result, isNotNull);
      expect(result!.result.isCorrect, isTrue);
      expect(result!.result.posteriorMastery, equals(0.78));

      // Disconnect
      await service.disconnect();
      expect(service.isConnected, isFalse);
    });

    test('reconnection preserves message router handlers', () async {
      final mockServer = MockSignalRServer();
      final service = WebSocketServiceImpl(
        channelFactory: (url) => mockServer.createChannel(),
      );
      await service.connect(
        url: 'wss://test',
        authToken: 'token',
        reconnectionConfig: ReconnectionConfig(
          initialDelay: Duration(milliseconds: 10),
          maxAttempts: 3,
        ),
      );

      // Simulate disconnect + reconnect
      mockServer.forceDisconnect();
      await Future.delayed(Duration(milliseconds: 100));
      expect(service.currentState, equals(ConnectionState.connected));
    });
  });
}
```

## Rollback Criteria
- If `web_socket_channel` has bugs with SignalR framing: implement raw `dart:io WebSocket` with manual frame parsing
- If SignalR handshake is too complex: simplify to plain WebSocket JSON without SignalR protocol wrapping
- If heartbeat causes excessive battery drain: switch to server-initiated keepalive (server pings, client responds)

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] WebSocket connects to a local mock SignalR server and exchanges messages
- [ ] Reconnection recovers from transient disconnects within 30 seconds
- [ ] Heartbeat detects stale connections within 25 seconds
- [ ] All 8 command senders and 10 event targets are wired and tested
- [ ] Message Router handles all event types without crashes
- [ ] No memory leaks: `dispose()` cancels all subscriptions and timers
- [ ] PR reviewed by mobile lead
