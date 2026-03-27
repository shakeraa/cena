# MSG-001: Messaging Context — Domain Model, Actor & Contract Definitions

**Priority:** P1 — critical gap; teachers/parents cannot contact students
**Blocked by:** INF-003 (NATS JetStream), SEC-001 (auth/roles), ACT-002 (StudentActor)
**Estimated effort:** 2 days
**Design:** `docs/messaging-context-design.md`
**Contract:** `contracts/backend/actor-contracts.cs`, `contracts/frontend/signalr-messages.ts`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Cena currently distills learning interactions into a concept memory graph (mastery, BKT scores, HLR timers) but has **no channel for human-to-human communication**. This task defines the domain model for the Messaging bounded context — the aggregate root, events, contracts, and actor. See `docs/messaging-context-design.md` for the full architecture rationale.

### Implementation Plan (4 Tasks)

| Task | Scope | Blocked By | Effort |
|------|-------|------------|--------|
| **MSG-001** (this) | Domain model, actor, contracts | INF-003, SEC-001, ACT-002 | 2 days |
| **MSG-002** | Redis Streams — hot message store (write/read/pagination) | INF-004, MSG-001 | 3 days |
| **MSG-003** | NATS — streams, consumers, event publishing, classifier | INF-003, MSG-001 | 2 days |
| **MSG-004** | S3 archival — nightly worker, cold read-through | INF-005, MSG-002 | 2 days |
| **MSG-005** | Webhooks + SignalR — WhatsApp, Telegram, in-app messaging | MSG-002, MSG-003, WEB-002 | 3 days |

**Total estimated effort:** 12 days

### Storage Architecture (from design doc)

Messages are **NOT** stored in PostgreSQL/Marten. Different tiers for different access patterns:

| Tier | Store | Data | TTL |
|------|-------|------|-----|
| **Hot** | Redis Streams | Recent messages (0-30 days) | 30 days auto-evict |
| **Audit** | NATS JetStream | All message events | 365 days |
| **Metadata** | PostgreSQL (Marten projection) | Thread summaries, unread counts | Permanent |
| **Cold** | S3 (gzipped JSON-lines) | Archived messages (30+ days) | 365 days (lifecycle) |

## Subtasks

### MSG-001.1: Domain Events & Message Records

**Files:**
- `src/domain/Cena.Domain/Messaging/MessagingEvents.cs` — Marten domain events (for thread metadata only)
- `contracts/backend/messaging-contracts.cs` — public message types, DTOs, enums

**Acceptance:**
- [ ] Enums added to `contracts/backend/messaging-contracts.cs`:
  ```csharp
  public enum MessageRole { Teacher, Parent, Student, System }
  public enum MessageChannel { InApp, WhatsApp, Telegram, Push }
  public enum ThreadType { DirectMessage, ClassBroadcast, ParentThread }
  ```
- [ ] Message content value object:
  ```csharp
  public sealed record MessageContent(
      [MaxLength(2000)] string Text,
      string ContentType,  // "text" | "resource-link" | "encouragement"
      string? ResourceUrl,
      Dictionary<string, string>? Metadata);
  ```
- [ ] Domain events (thread-level only — individual messages go to Redis, not Marten):
  ```csharp
  public sealed record ThreadCreated_V1(
      string ThreadId, string ThreadType, string[] ParticipantIds,
      string[] ParticipantNames, string? ClassRoomId,
      string CreatedById, DateTimeOffset CreatedAt);

  public sealed record ThreadMuted_V1(
      string ThreadId, string UserId, DateTimeOffset? MutedUntil);
  ```
- [ ] NATS event records (published to JetStream for audit):
  ```csharp
  public sealed record MessageSent_V1(
      string ThreadId, string MessageId, string SenderId, MessageRole SenderRole,
      MessageContent Content, MessageChannel Channel,
      DateTimeOffset SentAt, string? ReplyToMessageId);

  public sealed record MessageRead_V1(
      string ThreadId, string MessageId, string ReadById, DateTimeOffset ReadAt);

  public sealed record MessageBlocked_V1(
      string ThreadId, string SenderId, string Reason, DateTimeOffset BlockedAt);
  ```
- [ ] All IDs are UUIDv7 (consistent with existing contracts)
- [ ] All timestamps are `DateTimeOffset` UTC

**Test:**
```csharp
using Cena.Domain.Messaging;
using System.ComponentModel.DataAnnotations;
using Xunit;

public class MessagingContractTests
{
    [Fact]
    public void MessageContent_MaxLength2000()
    {
        var content = new MessageContent(new string('x', 2001), "text", null, null);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(content, new ValidationContext(content), results, true);
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("Text"));
    }

    [Fact]
    public void MessageSent_V1_IsImmutableRecord()
    {
        var evt = new MessageSent_V1(
            ThreadId: "t-1", MessageId: "msg-1",
            SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            Content: new MessageContent("Hello!", "text", null, null),
            Channel: MessageChannel.InApp,
            SentAt: DateTimeOffset.UtcNow, ReplyToMessageId: null);

        // Records have structural equality
        var copy = evt with { };
        Assert.Equal(evt, copy);
    }

    [Fact]
    public void ThreadCreated_V1_CapturesAllParticipants()
    {
        var evt = new ThreadCreated_V1(
            ThreadId: "t-1", ThreadType: "DirectMessage",
            ParticipantIds: new[] { "teacher-1", "student-1" },
            ParticipantNames: new[] { "Mr. Levy", "Alice" },
            ClassRoomId: null, CreatedById: "teacher-1",
            CreatedAt: DateTimeOffset.UtcNow);

        Assert.Equal(2, evt.ParticipantIds.Length);
        Assert.Contains("student-1", evt.ParticipantIds);
    }

    [Theory]
    [InlineData(MessageRole.Teacher)]
    [InlineData(MessageRole.Parent)]
    [InlineData(MessageRole.Student)]
    [InlineData(MessageRole.System)]
    public void MessageRole_AllValuesAreDefined(MessageRole role)
    {
        Assert.True(Enum.IsDefined(role));
    }

    [Theory]
    [InlineData(MessageChannel.InApp)]
    [InlineData(MessageChannel.WhatsApp)]
    [InlineData(MessageChannel.Telegram)]
    [InlineData(MessageChannel.Push)]
    public void MessageChannel_AllValuesAreDefined(MessageChannel channel)
    {
        Assert.True(Enum.IsDefined(channel));
    }
}
```

---

### MSG-001.2: ConversationThreadActor

**Files:**
- `src/actors/Cena.Actors/Messaging/ConversationThreadActor.cs` — virtual actor, stateful via Redis
- `src/actors/Cena.Actors/Messaging/MessagingActorMessages.cs` — Proto.Actor command messages

**Acceptance:**
- [ ] Virtual grain: `ClusterIdentity(kind: "conversation-thread", identity: threadId)`
- [ ] Actor state reconstructed from **Redis** on activation (not Marten — see design doc Section 4.2):
  - Load thread metadata from `cena:thread:{threadId}:meta` Redis hash
  - Load `messageCount` from `XLEN cena:thread:{threadId}`
  - Load `lastMessageAt` from most recent stream entry
- [ ] Command handlers:
  - `SendMessage(threadId, senderId, senderRole, recipientId?, content, channel, replyToMessageId?)`
    - Validates `content.Text.Length <= 2000`
    - Delegates to `IContentModerator.Check(text)` → if blocked, returns error + publishes `MessageBlocked_V1`
    - Delegates to `IMessageThrottler.CheckAsync(senderId, senderRole)` → if exceeded, returns error
    - Delegates to `IMessageWriter.WriteMessageAsync()` (Redis — MSG-002)
    - Delegates to `IMessagingEventPublisher.PublishMessageSentAsync()` (NATS — MSG-003)
    - If thread doesn't exist yet, creates it (thread + first message are atomic)
    - Pushes `MessageReceived` to recipient's SignalR connections
    - Returns `ActorResult(Success: true)` or `ActorResult(Success: false, ErrorCode)` on validation failure
  - `AcknowledgeMessage(threadId, messageId, readById)`
    - Delegates to `IMessageReader.MarkReadAsync()` (Redis — MSG-002)
    - Pushes `MessageReadReceipt` to sender's SignalR connections
  - `GetThreadHistory(threadId, beforeTimestamp?, limit)`
    - Delegates to `ICompositeMessageReader.GetMessagesAsync()` (Redis + S3 — MSG-002 + MSG-004)
  - `MuteThread(threadId, userId, mutedUntil?)`
    - Publishes `ThreadMuted_V1` to Marten (thread metadata) and NATS (audit)
- [ ] Actor does NOT hold messages in memory — Redis is the store
- [ ] Actor passivation is lightweight (no snapshot needed)
- [ ] Error codes: `MESSAGE_TOO_LONG`, `MESSAGE_BLOCKED`, `RATE_LIMIT_EXCEEDED`, `UNAUTHORIZED`, `THREAD_NOT_FOUND`

**Test:**
```csharp
using Cena.Actors.Messaging;
using Proto;
using Proto.TestKit;
using Xunit;

public class ConversationThreadActorTests
{
    [Fact]
    public async Task SendMessage_WritesToRedisAndPublishesToNats()
    {
        var (actor, redis, nats) = CreateTestActorWithMocks();

        var result = await actor.RequestAsync<ActorResult>(new SendMessage(
            ThreadId: "t-1", SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            RecipientId: "student-1",
            Content: new MessageContent("Great work!", "text", null, null),
            Channel: MessageChannel.InApp, ReplyToMessageId: null));

        Assert.True(result.Success);
        Assert.Equal(1, redis.WriteCount);
        Assert.Equal(1, nats.PublishCount);
    }

    [Fact]
    public async Task SendMessage_ExceedsMaxLength_ReturnsError()
    {
        var (actor, _, _) = CreateTestActorWithMocks();
        var longText = new string('x', 2001);

        var result = await actor.RequestAsync<ActorResult>(new SendMessage(
            ThreadId: "t-1", SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            RecipientId: "student-1",
            Content: new MessageContent(longText, "text", null, null),
            Channel: MessageChannel.InApp, ReplyToMessageId: null));

        Assert.False(result.Success);
        Assert.Equal("MESSAGE_TOO_LONG", result.ErrorCode);
    }

    [Fact]
    public async Task SendMessage_ContentBlocked_ReturnsErrorAndPublishesBlockEvent()
    {
        var (actor, _, nats) = CreateTestActorWithMocks(
            moderationResult: new ModerationResult(false, "phone_number_detected"));

        var result = await actor.RequestAsync<ActorResult>(new SendMessage(
            ThreadId: "t-1", SenderId: "parent-1", SenderRole: MessageRole.Parent,
            RecipientId: "student-1",
            Content: new MessageContent("Call me at +972501234567", "text", null, null),
            Channel: MessageChannel.InApp, ReplyToMessageId: null));

        Assert.False(result.Success);
        Assert.Equal("MESSAGE_BLOCKED", result.ErrorCode);
        Assert.Equal(1, nats.BlockedPublishCount); // Audit event published
    }

    [Fact]
    public async Task SendMessage_ThrottleExceeded_ReturnsError()
    {
        var (actor, _, _) = CreateTestActorWithMocks(
            throttleResult: new ThrottleResult(false, RetryAfterSeconds: 3600));

        var result = await actor.RequestAsync<ActorResult>(new SendMessage(
            ThreadId: "t-1", SenderId: "parent-1", SenderRole: MessageRole.Parent,
            RecipientId: "student-1",
            Content: new MessageContent("Hello!", "text", null, null),
            Channel: MessageChannel.InApp, ReplyToMessageId: null));

        Assert.False(result.Success);
        Assert.Equal("RATE_LIMIT_EXCEEDED", result.ErrorCode);
    }

    [Fact]
    public async Task SendMessage_FirstMessage_CreatesThread()
    {
        var (actor, redis, nats) = CreateTestActorWithMocks();

        await actor.RequestAsync<ActorResult>(new SendMessage(
            ThreadId: "t-new", SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            RecipientId: "student-1",
            Content: new MessageContent("Welcome!", "text", null, null),
            Channel: MessageChannel.InApp, ReplyToMessageId: null));

        // Thread metadata created in Redis
        Assert.True(redis.ThreadMetaCreated.ContainsKey("t-new"));
        // ThreadCreated_V1 published to NATS
        Assert.Contains(nats.PublishedEvents, e => e is ThreadCreated_V1);
    }

    [Fact]
    public async Task AcknowledgeMessage_ResetsUnreadAndSendsReceipt()
    {
        var (actor, redis, _) = CreateTestActorWithMocks();

        await actor.RequestAsync<ActorResult>(new AcknowledgeMessage(
            ThreadId: "t-1", MessageId: "msg-1", ReadById: "student-1"));

        Assert.True(redis.MarkReadCalled);
    }

    [Fact]
    public async Task MuteThread_PublishesEvent()
    {
        var (actor, _, nats) = CreateTestActorWithMocks();

        await actor.RequestAsync<ActorResult>(new MuteThread(
            ThreadId: "t-1", UserId: "student-1",
            MutedUntil: DateTimeOffset.UtcNow.AddDays(7)));

        Assert.Contains(nats.PublishedEvents, e => e is ThreadMuted_V1);
    }
}
```

---

### MSG-001.3: MessageClassifier Service

**Files:**
- `src/domain/Cena.Domain/Messaging/MessageClassifier.cs` — stateless classifier
- `src/domain/Cena.Domain/Messaging/ClassificationResult.cs` — result type

**Acceptance:**
- [ ] `MessageClassifier.Classify(text, locale)` returns `ClassificationResult`:
  ```csharp
  public sealed record ClassificationResult(
      bool IsLearningSignal,
      string Intent,  // "quiz-answer", "confirmation", "concept-question", "greeting", "resource-share", "general"
      double Confidence);
  ```
- [ ] Classification rules (see design doc Section 5.2):
  - Numeric-only → `LearningSignal(intent: "quiz-answer", confidence: 0.95)`
  - Single letter a-d → `LearningSignal(intent: "quiz-answer", confidence: 0.90)`
  - Confirmation words (he/ar/en) → `LearningSignal(intent: "confirmation", confidence: 0.85)`
  - Question starters (he/ar/en) → `LearningSignal(intent: "concept-question", confidence: 0.75)`
  - URL detected → `Communication(intent: "resource-share", confidence: 0.95)`
  - Default → `Communication(intent: "general", confidence: 1.0)`
- [ ] Ambiguous (confidence < 0.7) → defaults to Communication (safe — never pollute concept graph)
- [ ] Stateless — no LLM call, no external dependency
- [ ] Supports Hebrew (`כן`, `לא`, `מה זה`), Arabic (`نعم`, `لا`, `كيف`, `اشرح`), English

**Test:**
```csharp
using Cena.Domain.Messaging;
using Xunit;

public class MessageClassifierTests
{
    private readonly MessageClassifier _classifier = new();

    [Theory]
    [InlineData("42", "quiz-answer", true)]
    [InlineData("3.14", "quiz-answer", true)]
    [InlineData("a", "quiz-answer", true)]
    [InlineData("B", "quiz-answer", true)]
    [InlineData("כן", "confirmation", true)]
    [InlineData("לא", "confirmation", true)]
    [InlineData("نعم", "confirmation", true)]
    [InlineData("yes", "confirmation", true)]
    [InlineData("no", "confirmation", true)]
    public void LearningSignals_ClassifiedCorrectly(string text, string intent, bool isLearning)
    {
        var result = _classifier.Classify(text, "en");
        Assert.Equal(isLearning, result.IsLearningSignal);
        Assert.Equal(intent, result.Intent);
        Assert.True(result.Confidence >= 0.7);
    }

    [Theory]
    [InlineData("Good morning!", "greeting")]
    [InlineData("Thanks teacher!", "general")]
    [InlineData("https://youtube.com/watch?v=abc", "resource-share")]
    [InlineData("כל הכבוד!", "general")]
    [InlineData("أحسنت!", "general")]
    public void Communication_ClassifiedCorrectly(string text, string intent)
    {
        var result = _classifier.Classify(text, "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal(intent, result.Intent);
    }

    [Theory]
    [InlineData("what is a fraction?", "concept-question")]
    [InlineData("מה זה שבר?", "concept-question")]
    [InlineData("كيف أحل هذا؟", "concept-question")]
    [InlineData("explain derivatives", "concept-question")]
    public void ConceptQuestions_AreLearningSignals(string text, string expectedIntent)
    {
        var result = _classifier.Classify(text, "en");
        Assert.True(result.IsLearningSignal);
        Assert.Equal(expectedIntent, result.Intent);
    }

    [Fact]
    public void MixedContent_DefaultsToCommunication()
    {
        var result = _classifier.Classify("42 thanks for the help!", "en");
        Assert.False(result.IsLearningSignal); // Not purely numeric
    }

    [Fact]
    public void EmptyString_DefaultsToCommunication()
    {
        var result = _classifier.Classify("", "en");
        Assert.False(result.IsLearningSignal);
        Assert.Equal("general", result.Intent);
    }
}
```

---

## Integration Test

```csharp
[Fact]
public async Task DomainModel_EndToEnd_ActorProcessesMessageAndPublishes()
{
    // Full actor + Redis + NATS integration
    var actor = CreateRealActor();

    // 1. Send message — should write to Redis, publish to NATS, create thread
    var result = await actor.RequestAsync<ActorResult>(new SendMessage(
        ThreadId: "t-e2e", SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
        RecipientId: "student-1",
        Content: new MessageContent("Study tonight!", "text", null, null),
        Channel: MessageChannel.InApp, ReplyToMessageId: null));

    Assert.True(result.Success);

    // 2. Verify Redis has the message
    var db = _redis.GetDatabase();
    var entries = await db.StreamRangeAsync("cena:thread:t-e2e");
    Assert.Single(entries);

    // 3. Verify NATS has the audit event
    Assert.Contains(CapturedNatsMessages, m =>
        m.Subject == "cena.messaging.events.MessageSent");

    // 4. Verify thread metadata exists
    Assert.Contains(CapturedNatsMessages, m =>
        m.Subject == "cena.messaging.events.ThreadCreated");

    // 5. Classifier routes "42" as learning signal
    var classifier = new MessageClassifier();
    var classification = classifier.Classify("42", "en");
    Assert.True(classification.IsLearningSignal);
    Assert.Equal("quiz-answer", classification.Intent);
}
```

## Rollback Criteria

- If `ConversationThreadActor` activation takes > 50ms: pre-warm with lazy loading (load metadata only, messages on demand)
- If classifier accuracy drops below 85%: add configurable regex patterns via configuration (not hardcoded)
- If thread creation race condition (two simultaneous first messages): Redis `HSETNX` on metadata hash guarantees idempotency

## Definition of Done

- [ ] All 3 subtasks pass their tests
- [ ] `dotnet test --filter MessagingDomain` → 0 failures
- [ ] Domain events, enums, and value objects defined in `contracts/backend/messaging-contracts.cs`
- [ ] `ConversationThreadActor` processes SendMessage, AcknowledgeMessage, MuteThread commands
- [ ] Actor delegates storage to Redis (MSG-002) and audit to NATS (MSG-003)
- [ ] `MessageClassifier` correctly routes learning signals vs communication
- [ ] Classifier supports Hebrew, Arabic, and English patterns
- [ ] All error codes documented and returned as `ActorResult`
- [ ] No circular dependencies between MSG-001 and MSG-002/003/004 (MSG-001 depends on interfaces, not implementations)
