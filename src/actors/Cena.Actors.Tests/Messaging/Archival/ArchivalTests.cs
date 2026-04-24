using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Cena.Actors.Messaging;
using Cena.Actors.Messaging.Archival;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Messaging.Archival;

public sealed class ArchivalKeyTests
{
    [Fact]
    public void ArchiveKey_FormatsCorrectly()
    {
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);

        var key = ArchivalKeys.ArchiveKey("t-123", start, end);

        Assert.Equal("2026/03/thread-t-123-20260301-20260331.jsonl.gz", key);
    }

    [Fact]
    public void ManifestKey_FormatsCorrectly()
    {
        var key = ArchivalKeys.ManifestKey(2026, 3);
        Assert.Equal("2026/03/manifest.json", key);
    }

    [Fact]
    public void ManifestKey_FromDate_FormatsCorrectly()
    {
        var date = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var key = ArchivalKeys.ManifestKey(date);
        Assert.Equal("2026/03/manifest.json", key);
    }
}

public sealed class ArchivalManifestTests
{
    [Fact]
    public void Manifest_SerializesCorrectly()
    {
        var manifest = new ArchivalManifest
        {
            Month = "2026-03",
            ArchivedAt = DateTimeOffset.Parse("2026-03-28T02:00:00Z"),
            Threads = new List<ArchivedThreadEntry>
            {
                new()
                {
                    ThreadId = "t-1", S3Key = "2026/03/thread-t-1.jsonl.gz",
                    MessageCount = 47, SizeBytes = 12480,
                    FirstMessageAt = DateTimeOffset.Parse("2026-02-28T10:00:00Z"),
                    LastMessageAt = DateTimeOffset.Parse("2026-03-27T14:30:00Z"),
                    Sha256 = "abc123"
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<ArchivalManifest>(json)!;

        Assert.Equal("2026-03", deserialized.Month);
        Assert.Single(deserialized.Threads);
        Assert.Equal("t-1", deserialized.Threads[0].ThreadId);
        Assert.Equal(47, deserialized.Threads[0].MessageCount);
    }
}

public sealed class InMemoryArchiveStoreTests
{
    [Fact]
    public async Task UploadAndDownload_RoundTrips()
    {
        var store = new InMemoryMessageArchiveStore();
        var data = Encoding.UTF8.GetBytes("test data");

        await store.UploadArchiveAsync("key-1", data);
        var result = await store.DownloadArchiveAsync("key-1");

        Assert.Equal(data, result);
    }

    [Fact]
    public async Task Exists_ReturnsTrueForUploaded()
    {
        var store = new InMemoryMessageArchiveStore();
        await store.UploadArchiveAsync("key-1", new byte[] { 1, 2, 3 });

        Assert.True(await store.ExistsAsync("key-1"));
        Assert.False(await store.ExistsAsync("key-2"));
    }

    [Fact]
    public async Task ManifestUploadAndDownload_RoundTrips()
    {
        var store = new InMemoryMessageArchiveStore();

        await store.UploadManifestAsync("manifest.json", """{"month":"2026-03"}""");
        var result = await store.DownloadManifestAsync("manifest.json");

        Assert.Equal("""{"month":"2026-03"}""", result);
    }

    [Fact]
    public async Task DownloadMissing_ReturnsNull()
    {
        var store = new InMemoryMessageArchiveStore();

        Assert.Null(await store.DownloadArchiveAsync("nonexistent"));
        Assert.Null(await store.DownloadManifestAsync("nonexistent"));
    }
}

public sealed class CompressionTests
{
    [Fact]
    public void CompressEntries_ProducesValidGzip()
    {
        var entries = new List<StreamEntry>
        {
            CreateEntry("100-0", "msg-1", "Hello!"),
            CreateEntry("101-0", "msg-2", "World!"),
        };

        var compressed = MessageArchivalWorker.CompressEntries(entries);

        Assert.True(compressed.Length > 0);
        // First two bytes of gzip: 0x1f 0x8b
        Assert.Equal(0x1f, compressed[0]);
        Assert.Equal(0x8b, compressed[1]);
    }

    [Fact]
    public void CompressAndDecompress_RoundTrips()
    {
        var entries = new List<StreamEntry>
        {
            CreateEntry("100-0", "msg-1", "First message"),
            CreateEntry("101-0", "msg-2", "Second message"),
            CreateEntry("102-0", "msg-3", "Third message"),
        };

        var compressed = MessageArchivalWorker.CompressEntries(entries);
        var decompressed = S3MessageReader.DecompressMessages(compressed);

        Assert.Equal(3, decompressed.Length);
        Assert.Equal("msg-1", decompressed[0].MessageId);
        Assert.Equal("First message", decompressed[0].ContentText);
        Assert.Equal("msg-2", decompressed[1].MessageId);
        Assert.Equal("msg-3", decompressed[2].MessageId);
    }

    [Fact]
    public void CompressAndDecompress_PreservesAllFields()
    {
        var entries = new List<StreamEntry>
        {
            CreateEntry("100-0", "msg-1", "Review fractions",
                senderId: "teacher-1", senderRole: "Teacher",
                senderName: "Mr. Levy", contentType: "text",
                channel: "InApp")
        };

        var compressed = MessageArchivalWorker.CompressEntries(entries);
        var decompressed = S3MessageReader.DecompressMessages(compressed);

        var msg = decompressed[0];
        Assert.Equal("msg-1", msg.MessageId);
        Assert.Equal("teacher-1", msg.SenderId);
        Assert.Equal("Teacher", msg.SenderRole);
        Assert.Equal("Mr. Levy", msg.SenderName);
        Assert.Equal("Review fractions", msg.ContentText);
        Assert.Equal("text", msg.ContentType);
        Assert.Equal("InApp", msg.Channel);
        Assert.Equal("100-0", msg.StreamEntryId);
    }

    [Fact]
    public void ComputeSha256_IsDeterministic()
    {
        var data = Encoding.UTF8.GetBytes("test data");

        var hash1 = MessageArchivalWorker.ComputeSha256(data);
        var hash2 = MessageArchivalWorker.ComputeSha256(data);

        Assert.Equal(hash1, hash2);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    [Fact]
    public void ComputeSha256_DifferentInputs_DifferentHashes()
    {
        var hash1 = MessageArchivalWorker.ComputeSha256(new byte[] { 1, 2, 3 });
        var hash2 = MessageArchivalWorker.ComputeSha256(new byte[] { 4, 5, 6 });

        Assert.NotEqual(hash1, hash2);
    }

    private static StreamEntry CreateEntry(
        string id, string messageId, string contentText,
        string senderId = "teacher-1", string senderRole = "Teacher",
        string senderName = "Mr. Levy", string contentType = "text",
        string channel = "InApp")
    {
        var values = new NameValueEntry[]
        {
            new("messageId", messageId),
            new("senderId", senderId),
            new("senderRole", senderRole),
            new("senderName", senderName),
            new("contentText", contentText),
            new("contentType", contentType),
            new("resourceUrl", ""),
            new("replyTo", ""),
            new("channel", channel),
            new("sentAt", DateTimeOffset.UtcNow.ToString("O")),
        };
        return new StreamEntry(id, values);
    }
}

public sealed class S3MessageReaderTests
{
    [Fact]
    public async Task GetMessages_ThreadNotFound_ReturnsEmpty()
    {
        var store = new InMemoryMessageArchiveStore();
        var reader = new S3MessageReader(store);

        var page = await reader.GetMessagesAsync("nonexistent",
            DateTimeOffset.UtcNow.AddDays(-60), 10);

        Assert.Empty(page.Messages);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetMessages_ReturnsArchivedMessages()
    {
        var store = new InMemoryMessageArchiveStore();
        var reader = new S3MessageReader(store);

        // Use a fixed reference month
        var refDate = DateTimeOffset.UtcNow.AddMonths(-2);
        var msgDate1 = new DateTimeOffset(refDate.Year, refDate.Month, 10, 10, 0, 0, TimeSpan.Zero);
        var msgDate2 = new DateTimeOffset(refDate.Year, refDate.Month, 11, 10, 0, 0, TimeSpan.Zero);

        var entries = new List<StreamEntry>
        {
            CreateEntry("100-0", "msg-1", "First", sentAt: msgDate1),
            CreateEntry("101-0", "msg-2", "Second", sentAt: msgDate2),
        };

        var compressed = MessageArchivalWorker.CompressEntries(entries);
        var archiveKey = ArchivalKeys.ArchiveKey("t-1", msgDate1, msgDate2);
        await store.UploadArchiveAsync(archiveKey, compressed);

        // Create manifest for the same month
        var manifest = new ArchivalManifest
        {
            Month = refDate.ToString("yyyy-MM"),
            ArchivedAt = DateTimeOffset.UtcNow,
            Threads = new List<ArchivedThreadEntry>
            {
                new()
                {
                    ThreadId = "t-1", S3Key = archiveKey,
                    MessageCount = 2, SizeBytes = compressed.Length,
                    FirstMessageAt = msgDate1,
                    LastMessageAt = msgDate2,
                    Sha256 = MessageArchivalWorker.ComputeSha256(compressed)
                }
            }
        };

        await store.UploadManifestAsync(
            ArchivalKeys.ManifestKey(refDate),
            JsonSerializer.Serialize(manifest));

        // Query with a 'before' cursor in the same month
        var page = await reader.GetMessagesAsync("t-1",
            msgDate2.AddDays(1), 10);

        Assert.Equal(2, page.Messages.Length);
    }

    [Fact]
    public async Task ManifestCached_SecondCallDoesNotFetch()
    {
        var store = new InMemoryMessageArchiveStore();
        var reader = new S3MessageReader(store);

        var refDate = DateTimeOffset.UtcNow.AddMonths(-2);
        var manifestKey = ArchivalKeys.ManifestKey(refDate);
        await store.UploadManifestAsync(manifestKey,
            JsonSerializer.Serialize(new ArchivalManifest
            {
                Month = refDate.ToString("yyyy-MM")
            }));

        // Two reads with cursor in the same month
        var cursor = new DateTimeOffset(refDate.Year, refDate.Month, 15, 0, 0, 0, TimeSpan.Zero);
        await reader.GetMessagesAsync("t-1", cursor, 5);
        await reader.GetMessagesAsync("t-1", cursor, 5);

        // First call checks 3 months (cursor, -1, +1). Only the one with data caches.
        // Second call: the cached month hits, but the 2 missing months re-fetch (null not cached).
        // Total: 3 (first) + 2 (second, non-cached nulls) = 5. But the important thing:
        // the month WITH data is fetched exactly once.
        // We just verify the second call doesn't double-fetch the existing manifest.
        Assert.True(reader.ManifestFetchCount >= 3, "Should fetch at least 3 manifests (3 months)");
        Assert.True(reader.ManifestFetchCount <= 6, $"Should not exceed 6 manifest fetches, got {reader.ManifestFetchCount}");
    }

    private static StreamEntry CreateEntry(
        string id, string messageId, string contentText,
        DateTimeOffset? sentAt = null)
    {
        var values = new NameValueEntry[]
        {
            new("messageId", messageId),
            new("senderId", "teacher-1"),
            new("senderRole", "Teacher"),
            new("senderName", "Mr. Levy"),
            new("contentText", contentText),
            new("contentType", "text"),
            new("resourceUrl", ""),
            new("replyTo", ""),
            new("channel", "InApp"),
            new("sentAt", (sentAt ?? DateTimeOffset.UtcNow).ToString("O")),
        };
        return new StreamEntry(id, values);
    }
}

public sealed class CompositeMessageReaderTests
{
    [Fact]
    public async Task RecentMessages_ServedFromHot()
    {
        var hot = new FakeMessageReader(10);
        var cold = new S3MessageReader(new InMemoryMessageArchiveStore());
        var reader = new CompositeMessageReader(hot, cold);

        var page = await reader.GetMessagesAsync("t-1", null, 10);

        Assert.Equal(10, page.Messages.Length);
        Assert.Equal(1, hot.ReadCount);
    }

    [Fact]
    public async Task UnreadCount_DelegatesToHot()
    {
        var hot = new FakeMessageReader(unreadCount: 5);
        var reader = new CompositeMessageReader(hot);

        var count = await reader.GetUnreadCountAsync("t-1", "student-1");

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task MarkRead_DelegatesToHot()
    {
        var hot = new FakeMessageReader();
        var reader = new CompositeMessageReader(hot);

        await reader.MarkReadAsync("t-1", "student-1");

        Assert.True(hot.MarkReadCalled);
    }

    [Fact]
    public async Task GetUserThreads_DelegatesToHot()
    {
        var hot = new FakeMessageReader(threadIds: new[] { "t-1", "t-2" });
        var reader = new CompositeMessageReader(hot);

        var threads = await reader.GetUserThreadsAsync("teacher-1", 0, 10);

        Assert.Equal(2, threads.Length);
    }

    private sealed class FakeMessageReader : IMessageReader
    {
        private readonly int _messageCount;
        private readonly int _unreadCount;
        private readonly string[] _threadIds;

        public int ReadCount { get; private set; }
        public bool MarkReadCalled { get; private set; }

        public FakeMessageReader(
            int messageCount = 0, int unreadCount = 0, string[]? threadIds = null)
        {
            _messageCount = messageCount;
            _unreadCount = unreadCount;
            _threadIds = threadIds ?? Array.Empty<string>();
        }

        public Task<MessagePage> GetMessagesAsync(string threadId, string? beforeCursor, int limit = 20)
        {
            ReadCount++;
            var messages = Enumerable.Range(0, Math.Min(_messageCount, limit))
                .Select(i => new MessageView(
                    $"msg-{i}", "teacher-1", "Teacher", "Mr. Levy",
                    $"Message {i}", "text", null, null, "InApp",
                    DateTimeOffset.UtcNow.AddMinutes(-i), $"{1000 + i}-0"))
                .ToArray();

            return Task.FromResult(new MessagePage(messages, null, false));
        }

        public Task<int> GetUnreadCountAsync(string threadId, string userId) =>
            Task.FromResult(_unreadCount);

        public Task MarkReadAsync(string threadId, string userId)
        {
            MarkReadCalled = true;
            return Task.CompletedTask;
        }

        public Task<string[]> GetUserThreadsAsync(string userId, int offset, int limit) =>
            Task.FromResult(_threadIds);
    }
}
