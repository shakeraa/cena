# MSG-002: Hot Message Store — Redis Streams Integration

**Priority:** P1 — foundation for all messaging reads/writes
**Blocked by:** INF-004 (Redis/ElastiCache), MSG-001 (domain model)
**Estimated effort:** 3 days
**Design:** `docs/messaging-context-design.md` Section 3

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Chat messages are stored in Redis Streams (not PostgreSQL/Marten) because they have fundamentally different access patterns than mastery events: high-volume, reverse-chronological pagination, multi-writer, and rarely replayed in full. Redis Streams provide O(log N) range queries, built-in consumer groups, and natural message ordering via auto-generated IDs.

Messages live in Redis for 30 days (hot tier). After 30 days, a nightly archival worker moves them to S3 (cold tier, see MSG-004). Thread metadata and unread counts stay in PostgreSQL (Marten projection) because they're small, queryable, and permanent.

## Subtasks

### MSG-002.1: Redis Stream Key Schema & Connection Setup

**Files:**
- `src/infrastructure/Cena.Infrastructure/Redis/MessagingRedisKeys.cs` — key naming constants
- `src/infrastructure/Cena.Infrastructure/Redis/MessagingRedisConnection.cs` — connection factory with retry policy
- `config/redis-messaging.json` — configuration (key prefixes, TTLs, pool size)

**Acceptance:**
- [ ] Key schema constants (no magic strings in application code):
  ```
  cena:thread:{threadId}                 # Stream: messages in chronological order
  cena:thread:{threadId}:meta            # Hash: threadType, participantIds (CSV), createdAt, createdById
  cena:thread:{threadId}:unread:{userId} # String (counter): unread message count per participant
  cena:user:{userId}:threads             # Sorted set: threadIds scored by lastMessageAt (epoch ms)
  cena:webhook:dedup:{source}:{externalId} # String with 5min TTL: idempotency guard
  ```
- [ ] All keys under `cena:thread:*` have TTL set to 30 days on first write
- [ ] TTL is refreshed on every `XADD` to the stream (active threads stay hot)
- [ ] Connection uses `StackExchange.Redis` with `IConnectionMultiplexer` from DI
- [ ] Retry policy: 3 retries with exponential backoff (100ms, 200ms, 400ms) using Polly
- [ ] Configuration loaded from `config/redis-messaging.json`:
  ```json
  {
    "messageTtlDays": 30,
    "deduplicationTtlMinutes": 5,
    "maxStreamLength": 10000,
    "connectionPoolSize": 10
  }
  ```
- [ ] Stream trimming: `MAXLEN ~ 10000` on each `XADD` (approximate trim, prevents unbounded growth)

**Test:**
```csharp
using Cena.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

public class MessagingRedisKeysTests
{
    [Fact]
    public void ThreadStreamKey_FormatsCorrectly()
    {
        Assert.Equal("cena:thread:t-123", MessagingRedisKeys.ThreadStream("t-123"));
    }

    [Fact]
    public void ThreadMetaKey_FormatsCorrectly()
    {
        Assert.Equal("cena:thread:t-123:meta", MessagingRedisKeys.ThreadMeta("t-123"));
    }

    [Fact]
    public void UnreadKey_IncludesUserId()
    {
        Assert.Equal("cena:thread:t-123:unread:u-456",
            MessagingRedisKeys.Unread("t-123", "u-456"));
    }

    [Fact]
    public void UserThreadsKey_FormatsCorrectly()
    {
        Assert.Equal("cena:user:u-456:threads", MessagingRedisKeys.UserThreads("u-456"));
    }

    [Fact]
    public void DeduplicationKey_IncludesSourceAndId()
    {
        Assert.Equal("cena:webhook:dedup:whatsapp:SM123",
            MessagingRedisKeys.WebhookDedup("whatsapp", "SM123"));
    }
}
```

---

### MSG-002.2: Message Write Service (XADD)

**Files:**
- `src/infrastructure/Cena.Infrastructure/Redis/RedisMessageWriter.cs` — writes messages to Redis Streams
- `src/infrastructure/Cena.Infrastructure/Redis/IMessageWriter.cs` — interface for DI

**Acceptance:**
- [ ] `WriteMessageAsync(threadId, message)` executes as a Redis pipeline (single round-trip):
  1. `XADD cena:thread:{threadId} MAXLEN ~ 10000 * messageId {id} senderId {sid} senderRole {role} senderName {name} contentText {text} contentType {type} resourceUrl {url} replyTo {replyId} channel {channel} sentAt {iso}`
  2. `EXPIRE cena:thread:{threadId} 2592000` (30 days in seconds — refresh TTL)
  3. For each `recipientId` in thread: `INCR cena:thread:{threadId}:unread:{recipientId}`
  4. `ZADD cena:user:{senderId}:threads {epochMs} {threadId}` (update sender's thread list)
  5. For each `recipientId`: `ZADD cena:user:{recipientId}:threads {epochMs} {threadId}`
- [ ] All 5 operations execute in a single `ITransaction` (Redis pipeline, not Lua — pipeline is sufficient since all operations are independent)
- [ ] Returns the Redis Stream entry ID (used as ordering key)
- [ ] `contentText` is truncated to 2000 chars at this layer (defense in depth — actor also validates)
- [ ] `sentAt` is always UTC ISO 8601

**Test:**
```csharp
using Cena.Infrastructure.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

public class RedisMessageWriterTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private IConnectionMultiplexer _conn = null!;
    private RedisMessageWriter _writer = null!;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();
        _conn = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _writer = new RedisMessageWriter(_conn, TestConfig);
    }

    [Fact]
    public async Task WriteMessage_AddsToStream()
    {
        var msg = CreateTestMessage(threadId: "t-1", senderId: "teacher-1", text: "Great work!");

        var entryId = await _writer.WriteMessageAsync("t-1", msg);

        Assert.NotNull(entryId);
        var db = _conn.GetDatabase();
        var entries = await db.StreamRangeAsync("cena:thread:t-1");
        Assert.Single(entries);
        Assert.Equal("Great work!", entries[0]["contentText"].ToString());
    }

    [Fact]
    public async Task WriteMessage_IncrementsUnreadForRecipients()
    {
        var msg = CreateTestMessage(threadId: "t-1", senderId: "teacher-1", text: "Review tonight");

        await _writer.WriteMessageAsync("t-1", msg, recipientIds: new[] { "student-1", "student-2" });

        var db = _conn.GetDatabase();
        Assert.Equal(1, (int)await db.StringGetAsync("cena:thread:t-1:unread:student-1"));
        Assert.Equal(1, (int)await db.StringGetAsync("cena:thread:t-1:unread:student-2"));
        // Sender's unread is NOT incremented
        Assert.True((await db.StringGetAsync("cena:thread:t-1:unread:teacher-1")).IsNull);
    }

    [Fact]
    public async Task WriteMessage_UpdatesUserThreadSortedSet()
    {
        var msg = CreateTestMessage(threadId: "t-1", senderId: "teacher-1", text: "Hello");

        await _writer.WriteMessageAsync("t-1", msg, recipientIds: new[] { "student-1" });

        var db = _conn.GetDatabase();
        var teacherThreads = await db.SortedSetRangeByScoreAsync("cena:user:teacher-1:threads",
            order: Order.Descending);
        Assert.Contains(teacherThreads, t => t.ToString() == "t-1");
    }

    [Fact]
    public async Task WriteMessage_RefreshesTtl()
    {
        var db = _conn.GetDatabase();
        var msg = CreateTestMessage(threadId: "t-1", senderId: "teacher-1", text: "Test");

        await _writer.WriteMessageAsync("t-1", msg);

        var ttl = await db.KeyTimeToLiveAsync("cena:thread:t-1");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalDays, 29, 31);
    }

    [Fact]
    public async Task WriteMessage_TrimsStreamAt10000()
    {
        var db = _conn.GetDatabase();
        // Pre-fill with 10000 entries
        for (int i = 0; i < 10000; i++)
            await db.StreamAddAsync("cena:thread:t-1", "x", $"msg-{i}");

        var msg = CreateTestMessage(threadId: "t-1", senderId: "teacher-1", text: "Overflow");
        await _writer.WriteMessageAsync("t-1", msg);

        var length = await db.StreamLengthAsync("cena:thread:t-1");
        // MAXLEN ~ 10000 is approximate, so within tolerance
        Assert.InRange(length, 9900, 10100);
    }

    public async Task DisposeAsync()
    {
        _conn?.Dispose();
        await _redis.DisposeAsync();
    }
}
```

---

### MSG-002.3: Message Read Service (XREVRANGE + Pagination)

**Files:**
- `src/infrastructure/Cena.Infrastructure/Redis/RedisMessageReader.cs` — reads messages with cursor pagination
- `src/infrastructure/Cena.Infrastructure/Redis/IMessageReader.cs` — interface for DI

**Acceptance:**
- [ ] `GetMessagesAsync(threadId, before?, limit)` returns messages in reverse-chronological order:
  - Uses `XREVRANGE cena:thread:{threadId} {before} - COUNT {limit}`
  - `before` is a Redis Stream entry ID (cursor) — `null` means latest
  - `limit` defaults to 20, max 50
  - Returns `MessagePage { messages: MessageView[], nextCursor: string?, hasMore: bool }`
- [ ] `GetUnreadCountAsync(threadId, userId)` returns `int` from `GET cena:thread:{threadId}:unread:{userId}`
- [ ] `MarkReadAsync(threadId, userId)` resets unread counter: `SET cena:thread:{threadId}:unread:{userId} 0`
- [ ] `GetUserThreadsAsync(userId, offset, limit)` returns thread IDs sorted by recency:
  - Uses `ZREVRANGE cena:user:{userId}:threads {offset} {offset+limit-1}`
  - Returns `string[]` of thread IDs
- [ ] All read operations handle key-not-found gracefully (empty results, not errors)
- [ ] If `before` cursor points to a Redis entry that was trimmed/evicted, fall back to S3 reader (see MSG-004)

**Test:**
```csharp
using Cena.Infrastructure.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

public class RedisMessageReaderTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private IConnectionMultiplexer _conn = null!;
    private RedisMessageReader _reader = null!;
    private RedisMessageWriter _writer = null!;

    [Fact]
    public async Task GetMessages_ReturnsReverseChronological()
    {
        await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: "First"));
        await Task.Delay(10); // ensure different timestamps
        await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: "Second"));
        await Task.Delay(10);
        await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: "Third"));

        var page = await _reader.GetMessagesAsync("t-1", before: null, limit: 10);

        Assert.Equal(3, page.Messages.Length);
        Assert.Equal("Third", page.Messages[0].ContentText);
        Assert.Equal("Second", page.Messages[1].ContentText);
        Assert.Equal("First", page.Messages[2].ContentText);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetMessages_PaginatesWithCursor()
    {
        for (int i = 0; i < 5; i++)
            await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: $"msg-{i}"));

        var page1 = await _reader.GetMessagesAsync("t-1", before: null, limit: 2);
        Assert.Equal(2, page1.Messages.Length);
        Assert.True(page1.HasMore);
        Assert.NotNull(page1.NextCursor);

        var page2 = await _reader.GetMessagesAsync("t-1", before: page1.NextCursor, limit: 2);
        Assert.Equal(2, page2.Messages.Length);
        Assert.True(page2.HasMore);

        var page3 = await _reader.GetMessagesAsync("t-1", before: page2.NextCursor, limit: 2);
        Assert.Single(page3.Messages);
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task GetMessages_EmptyThread_ReturnsEmpty()
    {
        var page = await _reader.GetMessagesAsync("nonexistent", before: null, limit: 10);

        Assert.Empty(page.Messages);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsCorrectCount()
    {
        await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: "msg1"),
            recipientIds: new[] { "student-1" });
        await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: "msg2"),
            recipientIds: new[] { "student-1" });

        var count = await _reader.GetUnreadCountAsync("t-1", "student-1");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MarkRead_ResetsUnreadCounter()
    {
        await _writer.WriteMessageAsync("t-1", CreateTestMessage(text: "msg1"),
            recipientIds: new[] { "student-1" });

        await _reader.MarkReadAsync("t-1", "student-1");

        var count = await _reader.GetUnreadCountAsync("t-1", "student-1");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetUserThreads_SortedByRecency()
    {
        await _writer.WriteMessageAsync("t-old", CreateTestMessage(text: "old"));
        await Task.Delay(10);
        await _writer.WriteMessageAsync("t-new", CreateTestMessage(text: "new"));

        var threads = await _reader.GetUserThreadsAsync("teacher-1", offset: 0, limit: 10);

        Assert.Equal(2, threads.Length);
        Assert.Equal("t-new", threads[0]);
        Assert.Equal("t-old", threads[1]);
    }

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();
        _conn = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _writer = new RedisMessageWriter(_conn, TestConfig);
        _reader = new RedisMessageReader(_conn);
    }

    public async Task DisposeAsync()
    {
        _conn?.Dispose();
        await _redis.DisposeAsync();
    }
}
```

---

### MSG-002.4: Thread Metadata Projection (Marten)

**Files:**
- `src/data/Cena.Data/Projections/ThreadSummaryProjection.cs` — Marten async projection for thread metadata
- `src/data/Cena.Data/ReadModels/ThreadSummary.cs` — read model document

**Acceptance:**
- [ ] `ThreadSummary` Marten document:
  ```csharp
  public class ThreadSummary
  {
      public string Id { get; set; }               // threadId
      public string ThreadType { get; set; }        // DirectMessage, ClassBroadcast, ParentThread
      public string[] ParticipantIds { get; set; }
      public string[] ParticipantNames { get; set; }
      public string? ClassRoomId { get; set; }       // null for DirectMessage
      public string LastMessagePreview { get; set; } // first 100 chars
      public DateTimeOffset LastMessageAt { get; set; }
      public int MessageCount { get; set; }
      public string CreatedById { get; set; }
      public DateTimeOffset CreatedAt { get; set; }
  }
  ```
- [ ] Projection consumes `ThreadCreated_V1` and `MessageSent_V1` domain events from NATS
- [ ] On `ThreadCreated_V1`: insert new `ThreadSummary` document
- [ ] On `MessageSent_V1`: update `LastMessagePreview`, `LastMessageAt`, `MessageCount++`
- [ ] `LastMessagePreview` truncated to 100 characters
- [ ] Queryable by `ParticipantIds` (GIN index for array containment: `WHERE participant_ids @> ARRAY['user-1']`)
- [ ] Sorted by `LastMessageAt DESC` for thread list endpoint

**Test:**
```csharp
using Cena.Data.Projections;
using Cena.Data.ReadModels;
using Marten;
using Marten.Testing.Harness;
using Xunit;

public class ThreadSummaryProjectionTests : IntegrationContext
{
    [Fact]
    public async Task ThreadCreated_InsertsNewSummary()
    {
        var evt = new ThreadCreated_V1(
            ThreadId: "t-1", ThreadType: "DirectMessage",
            ParticipantIds: new[] { "teacher-1", "student-1" },
            ParticipantNames: new[] { "Mr. Levy", "Alice" },
            ClassRoomId: null, CreatedById: "teacher-1",
            CreatedAt: DateTimeOffset.UtcNow);

        theSession.Events.Append("t-1", evt);
        await theSession.SaveChangesAsync();
        await WaitForProjection();

        var summary = await theSession.LoadAsync<ThreadSummary>("t-1");
        Assert.NotNull(summary);
        Assert.Equal("DirectMessage", summary.ThreadType);
        Assert.Contains("teacher-1", summary.ParticipantIds);
        Assert.Contains("student-1", summary.ParticipantIds);
    }

    [Fact]
    public async Task MessageSent_UpdatesLastMessage()
    {
        // Create thread first
        theSession.Events.Append("t-1", CreateThreadEvent());
        await theSession.SaveChangesAsync();

        // Send message
        var msg = new MessageSent_V1(
            ThreadId: "t-1", MessageId: "msg-1",
            SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            Content: new MessageContent("Review fractions tonight!", "text", null, null),
            Channel: MessageChannel.InApp,
            SentAt: DateTimeOffset.UtcNow, ReplyToMessageId: null);
        theSession.Events.Append("t-1", msg);
        await theSession.SaveChangesAsync();
        await WaitForProjection();

        var summary = await theSession.LoadAsync<ThreadSummary>("t-1");
        Assert.Equal("Review fractions tonight!", summary.LastMessagePreview);
        Assert.Equal(1, summary.MessageCount);
    }

    [Fact]
    public async Task QueryByParticipant_ReturnsMatchingThreads()
    {
        theSession.Events.Append("t-1", CreateThreadEvent(participants: new[] { "teacher-1", "student-1" }));
        theSession.Events.Append("t-2", CreateThreadEvent(participants: new[] { "teacher-1", "student-2" }));
        theSession.Events.Append("t-3", CreateThreadEvent(participants: new[] { "teacher-2", "student-3" }));
        await theSession.SaveChangesAsync();
        await WaitForProjection();

        var teacherThreads = await theSession.Query<ThreadSummary>()
            .Where(t => t.ParticipantIds.Contains("teacher-1"))
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync();

        Assert.Equal(2, teacherThreads.Count);
    }

    [Fact]
    public async Task LastMessagePreview_TruncatedTo100Chars()
    {
        theSession.Events.Append("t-1", CreateThreadEvent());
        var longMessage = new string('x', 200);
        theSession.Events.Append("t-1", CreateMessageEvent(text: longMessage));
        await theSession.SaveChangesAsync();
        await WaitForProjection();

        var summary = await theSession.LoadAsync<ThreadSummary>("t-1");
        Assert.Equal(100, summary.LastMessagePreview.Length);
    }
}
```

---

## Integration Test

```csharp
[Fact]
public async Task FullWriteReadCycle_RedisStreams()
{
    var writer = CreateWriter();
    var reader = CreateReader();

    // 1. Write 3 messages to a thread
    await writer.WriteMessageAsync("t-1",
        CreateTestMessage(senderId: "teacher-1", text: "Hello Alice!"),
        recipientIds: new[] { "student-1" });
    await writer.WriteMessageAsync("t-1",
        CreateTestMessage(senderId: "teacher-1", text: "How was the quiz?"),
        recipientIds: new[] { "student-1" });
    await writer.WriteMessageAsync("t-1",
        CreateTestMessage(senderId: "teacher-1", text: "Review fractions tonight"),
        recipientIds: new[] { "student-1" });

    // 2. Read page 1 (limit 2)
    var page1 = await reader.GetMessagesAsync("t-1", before: null, limit: 2);
    Assert.Equal(2, page1.Messages.Length);
    Assert.Equal("Review fractions tonight", page1.Messages[0].ContentText);
    Assert.True(page1.HasMore);

    // 3. Read page 2
    var page2 = await reader.GetMessagesAsync("t-1", before: page1.NextCursor, limit: 2);
    Assert.Single(page2.Messages);
    Assert.Equal("Hello Alice!", page2.Messages[0].ContentText);
    Assert.False(page2.HasMore);

    // 4. Check unread count
    var unread = await reader.GetUnreadCountAsync("t-1", "student-1");
    Assert.Equal(3, unread);

    // 5. Mark as read
    await reader.MarkReadAsync("t-1", "student-1");
    Assert.Equal(0, await reader.GetUnreadCountAsync("t-1", "student-1"));

    // 6. Thread appears in user's thread list
    var threads = await reader.GetUserThreadsAsync("teacher-1", 0, 10);
    Assert.Contains("t-1", threads);
}
```

## Rollback Criteria

- If Redis memory exceeds 80% cluster capacity: reduce `maxStreamLength` from 10000 to 5000 and `messageTtlDays` from 30 to 14
- If `XREVRANGE` latency exceeds 5ms p99: add a read replica for messaging queries
- If `ZADD` contention on user thread sorted sets causes latency: shard by userId hash

## Definition of Done

- [ ] All 4 subtasks pass their tests
- [ ] `dotnet test --filter RedisMessaging` → 0 failures
- [ ] Redis Stream write pipeline executes in single round-trip (< 2ms p99)
- [ ] Pagination with cursor returns correct order and handles empty/full streams
- [ ] Unread counts increment on write, reset on mark-read
- [ ] User thread sorted sets stay sorted by recency
- [ ] TTLs refresh on activity, expire after 30 days of inactivity
- [ ] Thread metadata projection queryable by participant with GIN index
- [ ] Testcontainers used for integration tests (no shared Redis dependency)
