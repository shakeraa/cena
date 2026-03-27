using Cena.Actors.Messaging;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Messaging;

/// <summary>
/// Tests RedisMessageReader logic using mocked IConnectionMultiplexer.
/// </summary>
public sealed class RedisMessageReaderTests
{
    private readonly IDatabase _db;
    private readonly RedisMessageReader _reader;

    public RedisMessageReaderTests()
    {
        _db = Substitute.For<IDatabase>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        _reader = new RedisMessageReader(redis);
    }

    [Fact]
    public async Task GetMessages_EmptyStream_ReturnsEmptyPage()
    {
        _db.StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            Arg.Any<int?>(), Arg.Any<Order>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<StreamEntry>());

        var page = await _reader.GetMessagesAsync("t-nonexistent", null, 20);

        Assert.Empty(page.Messages);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetMessages_LessThanLimit_HasMoreFalse()
    {
        var entries = CreateStreamEntries(3);
        _db.StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            Arg.Any<int?>(), Arg.Any<Order>(), Arg.Any<CommandFlags>())
            .Returns(entries);

        var page = await _reader.GetMessagesAsync("t-1", null, 20);

        Assert.Equal(3, page.Messages.Length);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetMessages_MoreThanLimit_HasMoreTrue()
    {
        // Return limit+1 entries to signal there's more
        var entries = CreateStreamEntries(6);
        _db.StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            Arg.Any<int?>(), Arg.Any<Order>(), Arg.Any<CommandFlags>())
            .Returns(entries);

        var page = await _reader.GetMessagesAsync("t-1", null, 5);

        Assert.Equal(5, page.Messages.Length);
        Assert.True(page.HasMore);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task GetMessages_ClampsLimitTo50()
    {
        _db.StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            Arg.Any<int?>(), Arg.Any<Order>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<StreamEntry>());

        await _reader.GetMessagesAsync("t-1", null, 100);

        // Should request 51 (clamped 50 + 1 for hasMore check)
        await _db.Received(1).StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            51, Arg.Any<Order>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetMessages_ClampsLimitMinTo1()
    {
        _db.StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            Arg.Any<int?>(), Arg.Any<Order>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<StreamEntry>());

        await _reader.GetMessagesAsync("t-1", null, -5);

        await _db.Received(1).StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            2, Arg.Any<Order>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsValue()
    {
        _db.StringGetAsync("cena:thread:t-1:unread:student-1", Arg.Any<CommandFlags>())
            .Returns((RedisValue)3);

        var count = await _reader.GetUnreadCountAsync("t-1", "student-1");

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetUnreadCount_MissingKey_ReturnsZero()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var count = await _reader.GetUnreadCountAsync("t-1", "student-1");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task MarkRead_SetsCounterToZero()
    {
        await _reader.MarkReadAsync("t-1", "student-1");

        await _db.Received(1).StringSetAsync(
            "cena:thread:t-1:unread:student-1",
            0,
            Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetUserThreads_ReturnsSortedSetDescending()
    {
        _db.SortedSetRangeByRankAsync(
            "cena:user:teacher-1:threads",
            0, 9, Order.Descending, Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "t-new", "t-old" });

        var threads = await _reader.GetUserThreadsAsync("teacher-1", 0, 10);

        Assert.Equal(2, threads.Length);
        Assert.Equal("t-new", threads[0]);
        Assert.Equal("t-old", threads[1]);
    }

    [Fact]
    public async Task GetMessages_ParsesStreamEntryFields()
    {
        var entries = new[]
        {
            CreateStreamEntry("100-0", "msg-1", "teacher-1", "Teacher",
                "Mr. Levy", "Hello!", "text", "InApp")
        };

        _db.StreamRangeAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue?>(), Arg.Any<RedisValue?>(),
            Arg.Any<int?>(), Arg.Any<Order>(), Arg.Any<CommandFlags>())
            .Returns(entries);

        var page = await _reader.GetMessagesAsync("t-1", null, 10);

        Assert.Single(page.Messages);
        var msg = page.Messages[0];
        Assert.Equal("msg-1", msg.MessageId);
        Assert.Equal("teacher-1", msg.SenderId);
        Assert.Equal("Teacher", msg.SenderRole);
        Assert.Equal("Mr. Levy", msg.SenderName);
        Assert.Equal("Hello!", msg.ContentText);
        Assert.Equal("text", msg.ContentType);
        Assert.Equal("InApp", msg.Channel);
        Assert.Equal("100-0", msg.StreamEntryId);
    }

    // ── Helpers ──

    private static StreamEntry[] CreateStreamEntries(int count)
    {
        var entries = new StreamEntry[count];
        for (int i = 0; i < count; i++)
        {
            entries[i] = CreateStreamEntry(
                $"{1000 + i}-0", $"msg-{i}", "teacher-1", "Teacher",
                "Mr. Levy", $"Message {i}", "text", "InApp");
        }
        return entries;
    }

    private static StreamEntry CreateStreamEntry(
        string id, string messageId, string senderId, string senderRole,
        string senderName, string contentText, string contentType, string channel)
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
