# MSG-004: Cold Message Archive — S3 Archival Worker & Read-Through

**Priority:** P2 — needed before Redis TTL expires (30 days post-launch)
**Blocked by:** INF-005 (S3/CDN), MSG-002 (Redis Streams)
**Estimated effort:** 2 days
**Design:** `docs/messaging-context-design.md` Section 3.5

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Messages live in Redis Streams for 30 days (hot tier). After TTL expiry, they're gone from Redis. For audit compliance (365 days) and historical thread browsing, messages must be archived to S3 before eviction. A nightly archival worker scans Redis for threads approaching TTL and writes them to S3 as compressed JSON-lines files. The message reader falls back to S3 when a pagination cursor points beyond the Redis window.

## Subtasks

### MSG-004.1: Archival Worker (ECS Scheduled Task)

**Files:**
- `src/workers/Cena.Workers.Archival/MessageArchivalWorker.cs` — nightly batch job
- `src/workers/Cena.Workers.Archival/S3MessageArchiver.cs` — S3 write logic
- `src/workers/Cena.Workers.Archival/ArchivalManifest.cs` — manifest tracking

**Acceptance:**
- [ ] Runs as ECS Scheduled Task at 02:00 UTC daily (off-peak for Israel time zone)
- [ ] Scans Redis for stream keys matching `cena:thread:*` with TTL < 48 hours (2-day grace period before expiry)
- [ ] For each expiring thread:
  1. `XRANGE cena:thread:{threadId} - +` — read ALL messages in the stream
  2. Serialize to JSON-lines (one JSON object per line, matching Redis Stream entry schema)
  3. Gzip compress the JSON-lines
  4. Upload to S3: `s3://cena-messages/{year}/{month}/thread-{threadId}-{startDate}-{endDate}.jsonl.gz`
  5. Compute SHA-256 checksum of the gzipped file
  6. Update month manifest: `s3://cena-messages/{year}/{month}/manifest.json`
  7. Publish `cena.messaging.events.MessagesArchived` to NATS with thread ID and S3 key
- [ ] Manifest format:
  ```json
  {
    "month": "2026-03",
    "archivedAt": "2026-03-28T02:15:00Z",
    "threads": [
      {
        "threadId": "t-123",
        "s3Key": "2026/03/thread-t-123-20260228-20260327.jsonl.gz",
        "messageCount": 47,
        "firstMessageAt": "2026-02-28T10:00:00Z",
        "lastMessageAt": "2026-03-27T14:30:00Z",
        "sizeBytes": 12480,
        "sha256": "abc123..."
      }
    ]
  }
  ```
- [ ] Idempotent: if S3 object already exists with matching SHA-256, skip (don't re-upload)
- [ ] Worker logs to structured logging: thread count, total messages archived, duration, errors
- [ ] On S3 upload failure for a single thread: log error, continue with remaining threads (don't fail the batch)
- [ ] Memory-safe: stream reads in chunks of 1000 entries (don't load entire stream into memory for large threads)
- [ ] S3 storage class: `STANDARD_IA` (infrequent access — most archived messages are never read)
- [ ] S3 lifecycle policy: delete objects older than 365 days (configured via INF-005 bucket policy)

**Test:**
```csharp
using Cena.Workers.Archival;
using Amazon.S3;
using Amazon.S3.Model;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.LocalStack;
using Xunit;

public class MessageArchivalWorkerTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private LocalStackContainer _localstack = null!;
    private IAmazonS3 _s3 = null!;
    private IConnectionMultiplexer _conn = null!;

    [Fact]
    public async Task ArchivesThreadsApproachingTtl()
    {
        var db = _conn.GetDatabase();
        // Create a thread with messages
        for (int i = 0; i < 5; i++)
            await db.StreamAddAsync("cena:thread:t-1", "contentText", $"message-{i}");
        // Set TTL to 36 hours (< 48h threshold)
        await db.KeyExpireAsync("cena:thread:t-1", TimeSpan.FromHours(36));

        var worker = new MessageArchivalWorker(_conn, _s3, "cena-messages", TestLogger);
        await worker.RunAsync();

        // Verify S3 object exists
        var objects = await _s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "cena-messages",
            Prefix = $"{DateTime.UtcNow:yyyy/MM}/thread-t-1"
        });
        Assert.Single(objects.S3Objects);
        Assert.EndsWith(".jsonl.gz", objects.S3Objects[0].Key);
    }

    [Fact]
    public async Task SkipsThreadsWithLongTtl()
    {
        var db = _conn.GetDatabase();
        await db.StreamAddAsync("cena:thread:t-fresh", "contentText", "recent");
        await db.KeyExpireAsync("cena:thread:t-fresh", TimeSpan.FromDays(25)); // 25 days left

        var worker = new MessageArchivalWorker(_conn, _s3, "cena-messages", TestLogger);
        await worker.RunAsync();

        var objects = await _s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "cena-messages",
            Prefix = $"{DateTime.UtcNow:yyyy/MM}/thread-t-fresh"
        });
        Assert.Empty(objects.S3Objects);
    }

    [Fact]
    public async Task IdempotentUpload_SkipsExistingObject()
    {
        var db = _conn.GetDatabase();
        for (int i = 0; i < 3; i++)
            await db.StreamAddAsync("cena:thread:t-dup", "contentText", $"msg-{i}");
        await db.KeyExpireAsync("cena:thread:t-dup", TimeSpan.FromHours(24));

        var worker = new MessageArchivalWorker(_conn, _s3, "cena-messages", TestLogger);
        await worker.RunAsync();
        await worker.RunAsync(); // Second run

        var objects = await _s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "cena-messages"
        });
        // Only one object despite two runs
        Assert.Single(objects.S3Objects.Where(o => o.Key.Contains("t-dup")));
    }

    [Fact]
    public async Task ManifestUpdated_WithThreadEntry()
    {
        var db = _conn.GetDatabase();
        await db.StreamAddAsync("cena:thread:t-man", "contentText", "test");
        await db.KeyExpireAsync("cena:thread:t-man", TimeSpan.FromHours(12));

        var worker = new MessageArchivalWorker(_conn, _s3, "cena-messages", TestLogger);
        await worker.RunAsync();

        var manifest = await _s3.GetObjectAsync("cena-messages",
            $"{DateTime.UtcNow:yyyy/MM}/manifest.json");
        var content = await new StreamReader(manifest.ResponseStream).ReadToEndAsync();
        var parsed = JsonSerializer.Deserialize<ArchivalManifest>(content);

        Assert.Contains(parsed.Threads, t => t.ThreadId == "t-man");
        Assert.NotNull(parsed.Threads.First(t => t.ThreadId == "t-man").Sha256);
    }

    [Fact]
    public async Task LargeThread_StreamsInChunks()
    {
        var db = _conn.GetDatabase();
        // 2500 messages — should be read in 3 chunks of 1000
        for (int i = 0; i < 2500; i++)
            await db.StreamAddAsync("cena:thread:t-big", "contentText", $"msg-{i}");
        await db.KeyExpireAsync("cena:thread:t-big", TimeSpan.FromHours(12));

        var worker = new MessageArchivalWorker(_conn, _s3, "cena-messages", TestLogger);
        await worker.RunAsync();

        // Download and verify all 2500 messages are in the archive
        var obj = await _s3.GetObjectAsync("cena-messages",
            (await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = "cena-messages",
                Prefix = $"{DateTime.UtcNow:yyyy/MM}/thread-t-big"
            })).S3Objects.First().Key);

        using var gzip = new GZipStream(obj.ResponseStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        int lineCount = 0;
        while (await reader.ReadLineAsync() != null) lineCount++;
        Assert.Equal(2500, lineCount);
    }

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().Build();
        _localstack = new LocalStackBuilder().Build();
        await Task.WhenAll(_redis.StartAsync(), _localstack.StartAsync());
        _conn = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _s3 = CreateLocalStackS3Client();
        await _s3.PutBucketAsync("cena-messages");
    }

    public async Task DisposeAsync()
    {
        _conn?.Dispose();
        await _redis.DisposeAsync();
        await _localstack.DisposeAsync();
    }
}
```

---

### MSG-004.2: S3 Read-Through for Cold Messages

**Files:**
- `src/infrastructure/Cena.Infrastructure/S3/S3MessageReader.cs` — reads archived messages from S3
- `src/infrastructure/Cena.Infrastructure/Redis/CompositeMessageReader.cs` — combines Redis + S3 readers

**Acceptance:**
- [ ] `S3MessageReader.GetMessagesAsync(threadId, before, limit)`:
  1. Determine which month to query from `before` timestamp
  2. Fetch manifest for that month: `s3://cena-messages/{year}/{month}/manifest.json`
  3. Find the thread's archive file from manifest
  4. Download and decompress the `.jsonl.gz` file
  5. Parse JSON-lines, filter to messages before `before` timestamp
  6. Return `MessagePage` with correct ordering and pagination
- [ ] Caches downloaded manifests in memory for 1 hour (manifests are immutable once written)
- [ ] Caches decompressed thread data in memory for 5 minutes (LRU, max 100 threads)
- [ ] Returns empty result if thread not found in any manifest (not an error)
- [ ] `CompositeMessageReader` orchestrates:
  1. First query Redis (`RedisMessageReader`)
  2. If Redis returns `hasMore: false` AND `before` cursor is older than 30 days → query S3
  3. If Redis returns empty AND thread exists in metadata → query S3 directly
  4. Merge results maintaining reverse-chronological order
- [ ] S3 read latency logged as histogram: `cena.messaging.s3.read_duration_ms`

**Test:**
```csharp
using Cena.Infrastructure.S3;
using Cena.Infrastructure.Redis;
using Xunit;

public class CompositeMessageReaderTests
{
    [Fact]
    public async Task RecentMessages_ServedFromRedis()
    {
        var redis = CreateRedisWithMessages("t-1", 10, daysAgo: 5);
        var s3 = CreateEmptyS3Reader();
        var reader = new CompositeMessageReader(redis, s3);

        var page = await reader.GetMessagesAsync("t-1", before: null, limit: 10);

        Assert.Equal(10, page.Messages.Length);
        Assert.Equal(0, s3.ReadCount); // S3 never called
    }

    [Fact]
    public async Task OldMessages_FallbackToS3()
    {
        var redis = CreateEmptyRedisReader();
        var s3 = CreateS3WithArchivedMessages("t-1", 50, monthsAgo: 2);
        var reader = new CompositeMessageReader(redis, s3);

        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-60);
        var page = await reader.GetMessagesAsync("t-1",
            before: thirtyDaysAgo.ToString("O"), limit: 20);

        Assert.Equal(20, page.Messages.Length);
        Assert.Equal(1, s3.ReadCount); // S3 was called
    }

    [Fact]
    public async Task CrossBoundary_MergesRedisAndS3()
    {
        var redis = CreateRedisWithMessages("t-1", 5, daysAgo: 28); // Near TTL
        var s3 = CreateS3WithArchivedMessages("t-1", 10, monthsAgo: 1);
        var reader = new CompositeMessageReader(redis, s3);

        // Read all messages across both tiers
        var page1 = await reader.GetMessagesAsync("t-1", before: null, limit: 5);
        Assert.Equal(5, page1.Messages.Length); // From Redis

        var page2 = await reader.GetMessagesAsync("t-1", before: page1.NextCursor, limit: 10);
        Assert.Equal(10, page2.Messages.Length); // From S3

        // Verify ordering is consistent across boundary
        Assert.True(page1.Messages.Last().SentAt > page2.Messages.First().SentAt);
    }

    [Fact]
    public async Task ThreadNotInEitherStore_ReturnsEmpty()
    {
        var redis = CreateEmptyRedisReader();
        var s3 = CreateEmptyS3Reader();
        var reader = new CompositeMessageReader(redis, s3);

        var page = await reader.GetMessagesAsync("nonexistent", before: null, limit: 10);

        Assert.Empty(page.Messages);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task S3ManifestCached_SecondCallDoesNotFetch()
    {
        var s3 = CreateS3WithArchivedMessages("t-1", 5, monthsAgo: 2);
        var reader = new CompositeMessageReader(CreateEmptyRedisReader(), s3);

        var cursor = DateTimeOffset.UtcNow.AddDays(-60).ToString("O");
        await reader.GetMessagesAsync("t-1", before: cursor, limit: 5);
        await reader.GetMessagesAsync("t-1", before: cursor, limit: 5);

        Assert.Equal(1, s3.ManifestFetchCount); // Cached on second call
    }
}
```

---

## Integration Test

```csharp
[Fact]
public async Task FullLifecycle_WriteArchiveRead()
{
    // 1. Write messages to Redis (simulating 30 days ago)
    var writer = CreateRedisWriter();
    for (int i = 0; i < 10; i++)
        await writer.WriteMessageAsync("t-lifecycle",
            CreateTestMessage(text: $"Day 1 message {i}"));

    // 2. Set TTL to trigger archival
    var db = _conn.GetDatabase();
    await db.KeyExpireAsync("cena:thread:t-lifecycle", TimeSpan.FromHours(12));

    // 3. Run archival worker
    var archiver = new MessageArchivalWorker(_conn, _s3, "cena-messages", TestLogger);
    await archiver.RunAsync();

    // 4. Verify S3 archive exists
    var objects = await _s3.ListObjectsV2Async(new ListObjectsV2Request
    {
        BucketName = "cena-messages",
        Prefix = $"{DateTime.UtcNow:yyyy/MM}/thread-t-lifecycle"
    });
    Assert.Single(objects.S3Objects);

    // 5. Simulate Redis TTL expiry
    await db.KeyDeleteAsync("cena:thread:t-lifecycle");

    // 6. Read through composite reader — should serve from S3
    var reader = new CompositeMessageReader(
        new RedisMessageReader(_conn),
        new S3MessageReader(_s3, "cena-messages"));

    var page = await reader.GetMessagesAsync("t-lifecycle", before: null, limit: 20);
    Assert.Equal(10, page.Messages.Length);
    Assert.Equal("Day 1 message 9", page.Messages[0].ContentText); // Most recent first
}
```

## Rollback Criteria

- If S3 read latency exceeds 500ms p99: add CloudFront distribution for `cena-messages` bucket
- If archival worker takes > 30 minutes: parallelize thread processing (max 10 concurrent uploads)
- If S3 costs exceed $5/month: switch to S3 Glacier Instant Retrieval (compatible with on-demand reads)

## Definition of Done

- [ ] All 2 subtasks pass their tests
- [ ] `dotnet test --filter S3Archival` → 0 failures
- [ ] Archival worker runs nightly, archives threads with TTL < 48h
- [ ] S3 objects are gzipped JSON-lines with SHA-256 checksums
- [ ] Manifest tracks all archived threads per month
- [ ] Archival is idempotent (re-runs don't create duplicates)
- [ ] Large threads (2500+ messages) stream in chunks, don't OOM
- [ ] CompositeMessageReader seamlessly serves from Redis or S3 based on age
- [ ] S3 manifest cached in memory (no redundant fetches)
- [ ] Testcontainers + LocalStack used for integration tests
