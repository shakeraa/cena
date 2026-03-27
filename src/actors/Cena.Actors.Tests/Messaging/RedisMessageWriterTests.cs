using Cena.Actors.Messaging;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Messaging;

/// <summary>
/// Tests RedisMessageWriter logic using mocked IConnectionMultiplexer.
/// Uses ReceivedWithAnyArgs for StackExchange.Redis methods that have
/// many overloads (StreamAddAsync, KeyExpireAsync, etc.).
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

        // Default: StreamAddAsync returns an entry ID (NameValueEntry[] overload)
        _db.StreamAddAsync(
            Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(), Arg.Any<long?>(), Arg.Any<bool>(),
            Arg.Any<long?>(), Arg.Any<StreamTrimMode>(),
            Arg.Any<CommandFlags>())
            .Returns("1234567890-0");

        _writer = new RedisMessageWriter(redis);
    }

    [Fact]
    public async Task WriteMessage_CallsStreamAdd()
    {
        var entry = CreateTestEntry();

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        await _db.Received(1).StreamAddAsync(
            (RedisKey)"cena:thread:t-1",
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(), Arg.Any<long?>(), Arg.Any<bool>(),
            Arg.Any<long?>(), Arg.Any<StreamTrimMode>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_RefreshesTtl()
    {
        var entry = CreateTestEntry();

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        await _db.Received(1).KeyExpireAsync(
            (RedisKey)"cena:thread:t-1",
            MessagingRedisKeys.MessageTtl,
            Arg.Any<ExpireWhen>(),
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
            (RedisKey)"cena:thread:t-1:unread:student-1",
            Arg.Any<long>(), Arg.Any<CommandFlags>());
        await _db.Received(1).StringIncrementAsync(
            (RedisKey)"cena:thread:t-1:unread:student-2",
            Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_DoesNotIncrementUnreadForSender()
    {
        var entry = CreateTestEntry(senderId: "teacher-1");

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        // Sender's unread is NOT incremented
        await _db.DidNotReceive().StringIncrementAsync(
            (RedisKey)"cena:thread:t-1:unread:teacher-1",
            Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WriteMessage_UpdatesUserThreadSortedSets()
    {
        var entry = CreateTestEntry(senderId: "teacher-1");

        await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        // Should have 2 SortedSetAddAsync calls (sender + recipient)
        await _db.ReceivedWithAnyArgs(2).SortedSetAddAsync(
            default, default, default, (SortedSetWhen)default, default);
    }

    [Fact]
    public async Task WriteMessage_ReturnsStreamEntryId()
    {
        var entry = CreateTestEntry();

        var entryId = await _writer.WriteMessageAsync("t-1", entry, new[] { "student-1" });

        Assert.Equal("1234567890-0", entryId);
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
