using Cena.Actors.Events;
using Cena.Actors.Messaging;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Messaging;

public sealed class ConversationThreadActorTests
{
    private readonly IMessageWriter _writer;
    private readonly IMessageReader _reader;
    private readonly IContentModerator _moderator;
    private readonly IMessageThrottler _throttler;
    private readonly IMessagingEventPublisher _publisher;
    private readonly ConversationThreadActor _actor;

    public ConversationThreadActorTests()
    {
        _writer = Substitute.For<IMessageWriter>();
        _reader = Substitute.For<IMessageReader>();
        _moderator = Substitute.For<IContentModerator>();
        _throttler = Substitute.For<IMessageThrottler>();
        _publisher = Substitute.For<IMessagingEventPublisher>();
        var logger = Substitute.For<ILogger<ConversationThreadActor>>();

        _actor = new ConversationThreadActor(
            _writer, _reader, _moderator, _throttler, _publisher, logger);

        // Default: allow everything
        _moderator.Check(Arg.Any<string>()).Returns(new ModerationResult(true, null));
        _throttler.Check(Arg.Any<string>(), Arg.Any<MessageRole>()).Returns(new ThrottleResult(true));
        _writer.WriteMessageAsync(Arg.Any<string>(), Arg.Any<MessageEntry>(), Arg.Any<string[]>())
            .Returns("1234567890-0");
    }

    [Fact]
    public async Task SendMessage_Success_WritesToRedisAndPublishesToNats()
    {
        var context = CreateMockContext();
        var msg = CreateSendMessage();

        await _actor.ReceiveAsync(context);

        await _writer.Received(1).WriteMessageAsync(
            "t-1", Arg.Any<MessageEntry>(), Arg.Any<string[]>());
        await _publisher.Received(1).PublishMessageSentAsync(
            Arg.Any<MessageSent_V1>());
        await _publisher.Received(1).PublishThreadCreatedAsync(
            Arg.Any<ThreadCreated_V1>());
        context.Received(1).Respond(Arg.Is<MessagingResult>(r => r.Success));
    }

    [Fact]
    public async Task SendMessage_ExceedsMaxLength_ReturnsError()
    {
        var context = CreateMockContext(new SendMessage(
            "t-1", "teacher-1", MessageRole.Teacher, "student-1",
            new MessageContent(new string('x', 2001), "text", null, null),
            MessageChannel.InApp, null));

        await _actor.ReceiveAsync(context);

        context.Received(1).Respond(Arg.Is<MessagingResult>(r =>
            !r.Success && r.ErrorCode == "MESSAGE_TOO_LONG"));
        await _writer.DidNotReceive().WriteMessageAsync(
            Arg.Any<string>(), Arg.Any<MessageEntry>(), Arg.Any<string[]>());
    }

    [Fact]
    public async Task SendMessage_ContentBlocked_ReturnsErrorAndPublishesBlock()
    {
        _moderator.Check(Arg.Any<string>())
            .Returns(new ModerationResult(false, "phone_number_detected"));

        var context = CreateMockContext(new SendMessage(
            "t-1", "parent-1", MessageRole.Parent, "student-1",
            new MessageContent("Call +972501234567", "text", null, null),
            MessageChannel.InApp, null));

        await _actor.ReceiveAsync(context);

        context.Received(1).Respond(Arg.Is<MessagingResult>(r =>
            !r.Success && r.ErrorCode == "MESSAGE_BLOCKED"));
        await _publisher.Received(1).PublishMessageBlockedAsync(
            Arg.Any<MessageBlocked_V1>());
        await _writer.DidNotReceive().WriteMessageAsync(
            Arg.Any<string>(), Arg.Any<MessageEntry>(), Arg.Any<string[]>());
    }

    [Fact]
    public async Task SendMessage_ThrottleExceeded_ReturnsError()
    {
        _throttler.Check(Arg.Any<string>(), Arg.Any<MessageRole>())
            .Returns(new ThrottleResult(false, RetryAfterSeconds: 3600));

        var context = CreateMockContext();

        await _actor.ReceiveAsync(context);

        context.Received(1).Respond(Arg.Is<MessagingResult>(r =>
            !r.Success && r.ErrorCode == "RATE_LIMIT_EXCEEDED"));
    }

    [Fact]
    public async Task SendMessage_RecordsThrottleSend()
    {
        var context = CreateMockContext();

        await _actor.ReceiveAsync(context);

        _throttler.Received(1).RecordSend("teacher-1", MessageRole.Teacher);
    }

    [Fact]
    public async Task AcknowledgeMessage_MarksReadAndPublishes()
    {
        var ack = new AcknowledgeMessage("t-1", "msg-1", "student-1");
        var context = CreateMockContext(ack);

        await _actor.ReceiveAsync(context);

        await _reader.Received(1).MarkReadAsync("t-1", "student-1");
        await _publisher.Received(1).PublishMessageReadAsync(
            Arg.Any<MessageRead_V1>());
        context.Received(1).Respond(Arg.Is<MessagingResult>(r => r.Success));
    }

    [Fact]
    public async Task GetThreadHistory_DelegatesToReader()
    {
        var expected = new MessagePage(Array.Empty<MessageView>(), null, false);
        _reader.GetMessagesAsync("t-1", null, 20).Returns(expected);

        var query = new GetThreadHistory("t-1", null, 20);
        var context = CreateMockContext(query);

        await _actor.ReceiveAsync(context);

        context.Received(1).Respond(expected);
    }

    [Fact]
    public async Task MuteThread_PublishesEvent()
    {
        var mute = new MuteThread("t-1", "student-1", DateTimeOffset.UtcNow.AddDays(7));
        var context = CreateMockContext(mute);

        await _actor.ReceiveAsync(context);

        await _publisher.Received(1).PublishThreadMutedAsync(
            Arg.Any<ThreadMuted_V1>());
        context.Received(1).Respond(Arg.Is<MessagingResult>(r => r.Success));
    }

    // ── Helpers ──

    private SendMessage CreateSendMessage() => new(
        "t-1", "teacher-1", MessageRole.Teacher, "student-1",
        new MessageContent("Great work on fractions!", "text", null, null),
        MessageChannel.InApp, null);

    private Proto.IContext CreateMockContext(object? message = null)
    {
        var ctx = Substitute.For<Proto.IContext>();
        ctx.Message.Returns(message ?? CreateSendMessage());
        ctx.Parent.Returns(Proto.PID.FromAddress("test", "parent"));
        return ctx;
    }
}
