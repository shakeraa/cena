using Cena.Actors.Messaging;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Messaging;

/// <summary>
/// Tests RedisMessageWriter logic using mocked IConnectionMultiplexer.
/// Validates that correct Redis commands are issued in the right order.
/// </summary>
public sealed class RedisMessageWriterTests
{
    private readonly IDatabase _db;
    private readonly RedisMessageWriter _writer;

    public RedisMessageWriterTests()
    {
        _db = Substitute.For<IDatabase>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);

        _db.StreamAddAsync(
            Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(), Arg.Any<int?>(),
            Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns("1234567890-0");

        _writer = new RedisMessageWriter(redis);
    }

    [Fact]
    public async Task WriteMessage_CallsStreamAdd()
    {
        var entry = CreateTestEntry();

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        await _db.Received(1).StreamAddAsync(
            "cena:thread:t-1",
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            MessagingRedisKeys.MaxStreamLength,
            true,
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_RefreshesTtl()
    {
        var entry = CreateTestEntry();

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        await _db.Received(1).KeyExpireAsync(
            "cena:thread:t-1",
            MessagingRedisKeys.MessageTtl,
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_IncrementsUnreadForRecipients_NotSender()
    {
        var entry = CreateTestEntry(senderId: "teacher-1");

        await _writer.WriteMessageAsync("t-1", entry,
            new[] { "student-1", "student-2" });

        // Unread incremented for both students
        await _db.Received(1).StringIncrementAsync(
            "cena:thread:t-1:unread:student-1", Arg.Any<long>(), Arg.Any<CommandFlags>());
        await _db.Received(1).StringIncrementAsync(
            "cena:thread:t-1:unread:student-2", Arg.Any<long>(), Arg.Any<CommandFlags>());

        // NOT incremented for sender
        await _db.DidNotReceive().StringIncrementAsync(
            "cena:thread:t-1:unread:teacher-1", Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_UpdatesSenderThreadSortedSet()
    {
        var entry = CreateTestEntry(senderId: "teacher-1");

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        await _db.Received(1).SortedSetAddAsync(
            "cena:user:teacher-1:threads",
            "t-1",
            Arg.Any<double>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_UpdatesRecipientThreadSortedSets()
    {
        var entry = CreateTestEntry(senderId: "teacher-1");

        await _writer.WriteMessageAsync("t-1", entry,
            new[] { "student-1", "student-2" });

        await _db.Received(1).SortedSetAddAsync(
            "cena:user:student-1:threads", "t-1",
            Arg.Any<double>(), Arg.Any<CommandFlags>());
        await _db.Received(1).SortedSetAddAsync(
            "cena:user:student-2:threads", "t-1",
            Arg.Any<double>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_ReturnsStreamEntryId()
    {
        var entry = CreateTestEntry();

        var entryId = await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        Assert.Equal("1234567890-0", entryId);
    }

    [Fact]
    public async Task WriteMessage_TruncatesContentAt2000Chars()
    {
        var longText = new string('x', 3000);
        var entry = CreateTestEntry(contentText: longText);

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        await _db.Received(1).StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Is<NameValueEntry[]>(fields =>
                fields.Any(f => f.Name == "contentText" &&
                    f.Value.ToString().Length == 2000)),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>());
    }

    private static MessageEntry CreateTestEntry(
        string senderId = "teacher-1",
        string contentText = "Great work!") =>
        new(
            MessageId: Guid.NewGuid().ToString("N"),
            SenderId: senderId,
            SenderRole: MessageRole.Teacher,
            SenderName: "Mr. Levy",
            ContentText: contentText,
            ContentType: "text",
            ResourceUrl: null,
            ReplyToMessageId: null,
            Channel: MessageChannel.InApp,
            SentAt: DateTimeOffset.UtcNow);
}
