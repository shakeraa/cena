# MSG-003: Messaging NATS Integration — Streams, Consumers, Event Publishing

**Priority:** P1 — audit trail and cross-context integration
**Blocked by:** INF-003 (NATS JetStream), MSG-001 (domain model)
**Estimated effort:** 2 days
**Design:** `docs/messaging-context-design.md` Section 6

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Every message written to Redis Streams must also be published to NATS JetStream for three purposes:
1. **Audit trail** — 365-day retention (legal/compliance), independent of Redis TTL
2. **Analytics** — the Analytics context consumes all messaging events for engagement dashboards
3. **Cross-context routing** — inbound webhook replies are classified and routed via NATS commands

This task provisions the NATS streams, defines consumers, and implements the publisher/consumer services.

## Subtasks

### MSG-003.1: Stream & Consumer Provisioning

**Files:**
- `contracts/backend/nats-subjects.md` — append Messaging context subjects to existing hierarchy
- `scripts/nats/provision-messaging-streams.sh` — NATS CLI script to create streams and consumers

**Acceptance:**
- [ ] Subjects appended to `contracts/backend/nats-subjects.md` Section 1 hierarchy:
  ```
  ├── cena.messaging.events.>                    # Messaging bounded context events
  │   ├── cena.messaging.events.MessageSent      # Human message dispatched
  │   ├── cena.messaging.events.MessageRead      # Message marked as read
  │   ├── cena.messaging.events.ThreadCreated    # New conversation thread
  │   ├── cena.messaging.events.ThreadMuted      # Thread muted by participant
  │   ├── cena.messaging.events.MessageBlocked   # Content moderation blocked a message
  │   ├── cena.messaging.events.InboundReceived  # Webhook received (audit trail)
  │   └── cena.messaging.events.MessagesArchived # Batch archived to S3
  │
  ├── cena.messaging.commands.>                  # Cross-context commands TO messaging
  │   ├── cena.messaging.commands.SendMessage    # Request to send a message
  │   ├── cena.messaging.commands.RouteInboundReply # Webhook routes inbound reply
  │   └── cena.messaging.commands.BroadcastToClass  # Teacher broadcasts to class
  ```
- [ ] Stream provisioning script:
  ```bash
  # MESSAGING_EVENTS — 365-day audit trail
  nats stream add MESSAGING_EVENTS \
    --subjects "cena.messaging.events.>" \
    --retention limits --max-age 365d --max-bytes 5368709120 \
    --replicas 3 --storage file --discard old --dupe-window 2m

  # MESSAGING_COMMANDS — work queue
  nats stream add MESSAGING_COMMANDS \
    --subjects "cena.messaging.commands.>" \
    --retention work --max-age 7d --max-bytes 1073741824 \
    --replicas 3 --storage file --discard old --dupe-window 2m
  ```
- [ ] Consumer provisioning:
  ```bash
  # Process send requests
  nats consumer add MESSAGING_COMMANDS messaging-send-processor \
    --filter "cena.messaging.commands.SendMessage" \
    --deliver all --ack explicit --wait 30s \
    --max-deliver 5 --max-pending 200 --pull --replay instant

  # Route inbound replies (high retry)
  nats consumer add MESSAGING_COMMANDS messaging-inbound-router \
    --filter "cena.messaging.commands.RouteInboundReply" \
    --deliver all --ack explicit --wait 30s \
    --max-deliver 10 --max-pending 100 --pull --replay instant

  # Fan-out class broadcasts
  nats consumer add MESSAGING_COMMANDS messaging-broadcast-processor \
    --filter "cena.messaging.commands.BroadcastToClass" \
    --deliver all --ack explicit --wait 60s \
    --max-deliver 5 --max-pending 50 --pull --replay instant

  # Analytics: all messaging events (batch processing)
  nats consumer add MESSAGING_EVENTS analytics-all-messaging \
    --filter "cena.messaging.events.>" \
    --deliver all --ack explicit --wait 120s \
    --max-deliver 3 --max-pending 5000 --pull --replay instant

  # Archival: message events for S3 cold storage
  nats consumer add MESSAGING_EVENTS archival-message-events \
    --filter "cena.messaging.events.MessageSent" \
    --deliver all --ack explicit --wait 120s \
    --max-deliver 3 --max-pending 10000 --pull --replay instant
  ```
- [ ] DLQ subjects added: `cena.system.dlq.messaging.>` following existing pattern
- [ ] Script is idempotent (uses `nats stream add` which is a no-op if stream exists with same config)

**Test:**
```bash
#!/bin/bash
# Integration test: run against local NATS server
set -euo pipefail

# Provision
bash scripts/nats/provision-messaging-streams.sh

# Verify streams
nats stream info MESSAGING_EVENTS --json | jq -e '.config.max_age == 31536000000000000'  # 365 days in ns
nats stream info MESSAGING_EVENTS --json | jq -e '.config.num_replicas == 3'
nats stream info MESSAGING_COMMANDS --json | jq -e '.config.retention == "workqueue"'

# Verify consumers
nats consumer info MESSAGING_COMMANDS messaging-send-processor --json | jq -e '.config.max_deliver == 5'
nats consumer info MESSAGING_COMMANDS messaging-inbound-router --json | jq -e '.config.max_deliver == 10'
nats consumer info MESSAGING_EVENTS analytics-all-messaging --json | jq -e '.config.max_ack_pending == 5000'

echo "All NATS messaging provisioning tests passed"
```

---

### MSG-003.2: NATS Event Publisher

**Files:**
- `src/infrastructure/Cena.Infrastructure/Nats/MessagingNatsPublisher.cs` — publishes messaging events
- `src/infrastructure/Cena.Infrastructure/Nats/IMessagingEventPublisher.cs` — interface

**Acceptance:**
- [ ] `PublishMessageSentAsync(MessageSent_V1)` publishes to `cena.messaging.events.MessageSent`
- [ ] `PublishMessageReadAsync(MessageRead_V1)` publishes to `cena.messaging.events.MessageRead`
- [ ] `PublishThreadCreatedAsync(ThreadCreated_V1)` publishes to `cena.messaging.events.ThreadCreated`
- [ ] `PublishMessageBlockedAsync(threadId, senderId, reason)` publishes to `cena.messaging.events.MessageBlocked`
- [ ] `PublishInboundReceivedAsync(source, externalId, text)` publishes to `cena.messaging.events.InboundReceived`
- [ ] All published messages set required headers:
  - `Nats-Msg-Id` = domain event's `EventId` (UUIDv7) — deduplication
  - `Cena-Correlation-Id` = end-to-end correlation ID
  - `Cena-Causation-Id` = ID of the command that caused this event
  - `Cena-Schema-Version` = `1`
  - `Cena-Thread-Type` = `DirectMessage|ClassBroadcast|ParentThread` (for consumer filtering)
- [ ] Publisher uses `IJetStream.PublishAsync` with `PublishAck` validation (confirms server received)
- [ ] On publish failure: log error, do NOT throw — message is in Redis (hot store), NATS catches up on retry
- [ ] Telemetry: `cena.messaging.nats.published_total` counter with `type` tag

**Test:**
```csharp
using Cena.Infrastructure.Nats;
using NATS.Client.JetStream;
using Xunit;

public class MessagingNatsPublisherTests
{
    [Fact]
    public async Task PublishMessageSent_SetsCorrectSubjectAndHeaders()
    {
        var (publisher, captured) = CreateTestPublisher();

        await publisher.PublishMessageSentAsync(new MessageSent_V1(
            ThreadId: "t-1", MessageId: "msg-1",
            SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            Content: new MessageContent("Great work!", "text", null, null),
            Channel: MessageChannel.InApp,
            SentAt: DateTimeOffset.UtcNow, ReplyToMessageId: null));

        var msg = captured.Single();
        Assert.Equal("cena.messaging.events.MessageSent", msg.Subject);
        Assert.Equal("msg-1", msg.Headers["Nats-Msg-Id"]);
        Assert.NotNull(msg.Headers["Cena-Correlation-Id"]);
        Assert.Equal("1", msg.Headers["Cena-Schema-Version"]);
    }

    [Fact]
    public async Task PublishMessageBlocked_IncludesReason()
    {
        var (publisher, captured) = CreateTestPublisher();

        await publisher.PublishMessageBlockedAsync("t-1", "parent-1", "phone_number_detected");

        var msg = captured.Single();
        Assert.Equal("cena.messaging.events.MessageBlocked", msg.Subject);
        var body = DeserializePayload(msg);
        Assert.Equal("phone_number_detected", body.Reason);
    }

    [Fact]
    public async Task PublishFailure_LogsButDoesNotThrow()
    {
        var (publisher, _) = CreateTestPublisher(simulateFailure: true);
        var logger = GetTestLogger();

        // Should not throw
        await publisher.PublishMessageSentAsync(CreateTestEvent());

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error
            && e.Message.Contains("NATS publish failed"));
    }
}
```

---

### MSG-003.3: Inbound Message Consumer & Classifier Router

**Files:**
- `src/infrastructure/Cena.Infrastructure/Nats/InboundMessageConsumer.cs` — consumes `RouteInboundReply` commands
- `src/domain/Cena.Domain/Messaging/MessageClassifier.cs` — classifies inbound text
- `src/domain/Cena.Domain/Messaging/ClassificationResult.cs` — result type

**Acceptance:**
- [ ] `InboundMessageConsumer` pulls from `messaging-inbound-router` consumer group
- [ ] For each `RouteInboundReply` command:
  1. Call `MessageClassifier.Classify(text, locale)` to determine intent
  2. If `isLearningSignal`:
     - Publish to `cena.outreach.events.ResponseReceived` (existing subject)
     - Include `intent` and `confidence` in event payload
     - The Outreach context consumer routes this to `StudentActor`
  3. If communication:
     - Resolve `threadId` for the sender (lookup existing thread or create new)
     - Send `SendMessage` command to `ConversationThreadActor`
     - Include `channel: WhatsApp|Telegram` in the message
- [ ] `MessageClassifier.Classify(text, locale)` rules (see design doc Section 5.2):
  - Numeric-only (`^\d+(\.\d+)?$`) → `LearningSignal(intent: "quiz-answer", confidence: 0.95)`
  - Single letter a-d (`^[a-dA-D]$`) → `LearningSignal(intent: "quiz-answer", confidence: 0.90)`
  - Confirmation words (`yes|no|כן|לא|نعم|لا`) → `LearningSignal(intent: "confirmation", confidence: 0.85)`
  - Question starters (`^(what is|how do|explain|מה זה|كيف|اشرح)`) → `LearningSignal(intent: "concept-question", confidence: 0.75)`
  - URL detected → `Communication(intent: "resource-share", confidence: 0.95)`
  - Greeting detected → `Communication(intent: "greeting", confidence: 0.90)`
  - Default → `Communication(intent: "general", confidence: 1.0)`
- [ ] Confidence < 0.7 always routes to communication (safe default)
- [ ] Consumer acknowledges message only after successful routing (at-least-once delivery)

**Test:**
```csharp
using Cena.Domain.Messaging;
using Xunit;

public class MessageClassifierTests
{
    private readonly MessageClassifier _classifier = new();

    // ── Learning Signals ──

    [Theory]
    [InlineData("42", "quiz-answer")]
    [InlineData("3.14", "quiz-answer")]
    [InlineData("0", "quiz-answer")]
    [InlineData("a", "quiz-answer")]
    [InlineData("B", "quiz-answer")]
    [InlineData("d", "quiz-answer")]
    public void NumericAndLetterAnswers_AreLearningSignals(string text, string expectedIntent)
    {
        var result = _classifier.Classify(text, "en");
        Assert.True(result.IsLearningSignal);
        Assert.Equal(expectedIntent, result.Intent);
        Assert.True(result.Confidence >= 0.9);
    }

    [Theory]
    [InlineData("yes", "en", "confirmation")]
    [InlineData("no", "en", "confirmation")]
    [InlineData("כן", "he", "confirmation")]
    [InlineData("לא", "he", "confirmation")]
    [InlineData("نعم", "ar", "confirmation")]
    [InlineData("لا", "ar", "confirmation")]
    public void Confirmations_AreLearningSignals(string text, string locale, string expectedIntent)
    {
        var result = _classifier.Classify(text, locale);
        Assert.True(result.IsLearningSignal);
        Assert.Equal(expectedIntent, result.Intent);
    }

    [Theory]
    [InlineData("what is a fraction?", "en")]
    [InlineData("how do I solve this?", "en")]
    [InlineData("מה זה שבר?", "he")]
    [InlineData("كيف أحل هذا؟", "ar")]
    [InlineData("explain derivatives", "en")]
    public void ConceptQuestions_AreLearningSignals(string text, string locale)
    {
        var result = _classifier.Classify(text, locale);
        Assert.True(result.IsLearningSignal);
        Assert.Equal("concept-question", result.Intent);
    }

    // ── Communication ──

    [Theory]
    [InlineData("Good morning teacher!", "greeting")]
    [InlineData("Thanks for the help!", "general")]
    [InlineData("כל הכבוד!", "general")]
    [InlineData("أحسنت!", "general")]
    [InlineData("I'll study tonight, promise", "general")]
    public void SocialMessages_AreCommunication(string text, string expectedIntent)
    {
        var result = _classifier.Classify(text, "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal(expectedIntent, result.Intent);
    }

    [Fact]
    public void ResourceLinks_AreCommunication()
    {
        var result = _classifier.Classify(
            "Check this video: https://www.youtube.com/watch?v=abc", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("resource-share", result.Intent);
    }

    // ── Edge Cases ──

    [Fact]
    public void EmptyString_DefaultsToCommunication()
    {
        var result = _classifier.Classify("", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("general", result.Intent);
    }

    [Fact]
    public void MixedContent_HighestConfidenceWins()
    {
        // "42 thanks!" — has a number but also social text
        var result = _classifier.Classify("42 thanks!", "en");
        // Not purely numeric, so routes to communication (safe default)
        Assert.False(result.IsLearningSignal);
    }

    [Fact]
    public void AmbiguousText_DefaultsToCommunication()
    {
        // "ok" could be confirmation or just casual
        var result = _classifier.Classify("ok", "en");
        // Confidence below threshold → communication
        Assert.False(result.IsLearningSignal);
    }
}

public class InboundMessageConsumerTests
{
    [Fact]
    public async Task LearningSignal_PublishesToOutreachResponse()
    {
        var (consumer, captured) = CreateTestConsumer();

        await consumer.HandleAsync(new RouteInboundReply(
            Source: "whatsapp", SenderId: "student-phone-1",
            StudentId: "student-1", Text: "42",
            ReceivedAt: DateTimeOffset.UtcNow));

        Assert.Contains(captured, m =>
            m.Subject == "cena.outreach.events.ResponseReceived");
        Assert.DoesNotContain(captured, m =>
            m.Subject.StartsWith("cena.messaging."));
    }

    [Fact]
    public async Task Communication_RoutesToMessagingContext()
    {
        var (consumer, captured) = CreateTestConsumer();

        await consumer.HandleAsync(new RouteInboundReply(
            Source: "whatsapp", SenderId: "student-phone-1",
            StudentId: "student-1", Text: "Thanks teacher!",
            ReceivedAt: DateTimeOffset.UtcNow));

        Assert.DoesNotContain(captured, m =>
            m.Subject.StartsWith("cena.outreach.events."));
        // Message routed to ConversationThreadActor
        Assert.True(captured.Any(m =>
            m.Subject == "cena.messaging.commands.SendMessage"
            || m is ActorMessage));
    }
}
```

---

## Integration Test

```csharp
[Fact]
public async Task NatsPublishAndConsume_EndToEnd()
{
    // 1. Publish a MessageSent event
    await _publisher.PublishMessageSentAsync(new MessageSent_V1(
        ThreadId: "t-1", MessageId: "msg-1",
        SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
        Content: new MessageContent("Review fractions!", "text", null, null),
        Channel: MessageChannel.InApp,
        SentAt: DateTimeOffset.UtcNow, ReplyToMessageId: null));

    // 2. Analytics consumer receives it
    var analyticsMsg = await _analyticsConsumer.PullAsync(TimeSpan.FromSeconds(5));
    Assert.NotNull(analyticsMsg);
    Assert.Equal("cena.messaging.events.MessageSent", analyticsMsg.Subject);
    Assert.Equal("msg-1", analyticsMsg.Headers["Nats-Msg-Id"]);

    // 3. Acknowledge
    analyticsMsg.Ack();

    // 4. Re-pull returns no message (consumed)
    var retry = await _analyticsConsumer.PullAsync(TimeSpan.FromSeconds(1));
    Assert.Null(retry);
}

[Fact]
public async Task InboundReply_ClassifiedAndRouted_EndToEnd()
{
    // 1. Publish an inbound reply command
    await _nats.PublishAsync("cena.messaging.commands.RouteInboundReply",
        new RouteInboundReply(Source: "telegram", SenderId: "tg-user-1",
            StudentId: "student-1", Text: "42",
            ReceivedAt: DateTimeOffset.UtcNow));

    // 2. Inbound router consumer processes it
    await Task.Delay(500); // Allow consumer to process

    // 3. Learning signal was routed to outreach events
    var outreachMsg = await _outreachConsumer.PullAsync(TimeSpan.FromSeconds(5));
    Assert.NotNull(outreachMsg);
    Assert.Equal("cena.outreach.events.ResponseReceived", outreachMsg.Subject);
}
```

## Rollback Criteria

- If NATS publish latency exceeds 50ms p99: enable publisher batching (accumulate 10 messages, flush every 100ms)
- If `MESSAGING_EVENTS` stream exceeds 5 GB before 365 days: increase `max-bytes` to 10 GB or reduce retention to 180 days
- If consumer lag on `analytics-all-messaging` exceeds 10,000: increase `max-pending` to 10,000 or add horizontal consumers

## Definition of Done

- [ ] All 3 subtasks pass their tests
- [ ] `dotnet test --filter NatsMessaging` → 0 failures
- [ ] `bash scripts/nats/provision-messaging-streams.sh` is idempotent and succeeds on clean and existing clusters
- [ ] NATS subjects documented in `contracts/backend/nats-subjects.md`
- [ ] Publisher sets all required headers (Nats-Msg-Id, Cena-Correlation-Id, Cena-Schema-Version)
- [ ] Publisher does not throw on NATS failure (logs error, Redis is the hot store)
- [ ] MessageClassifier correctly routes learning signals vs communication
- [ ] InboundMessageConsumer processes `RouteInboundReply` commands with at-least-once delivery
- [ ] DLQ routing configured for `cena.system.dlq.messaging.>`
