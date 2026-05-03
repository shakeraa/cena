using Cena.Actors.Events;
using Cena.Actors.Messaging;

namespace Cena.Actors.Tests.Messaging;

public sealed class MessagingContractTests
{
    [Fact]
    public void MessageSent_V1_IsImmutableRecord()
    {
        var evt = new MessageSent_V1(
            "t-1", "msg-1", "teacher-1", MessageRole.Teacher,
            new MessageContent("Hello!", "text", null, null),
            MessageChannel.InApp, DateTimeOffset.UtcNow, null);

        var copy = evt with { };
        Assert.Equal(evt, copy);
        Assert.NotSame(evt, copy);
    }

    [Fact]
    public void ThreadCreated_V1_CapturesAllParticipants()
    {
        var evt = new ThreadCreated_V1(
            "t-1", "DirectMessage",
            new[] { "teacher-1", "student-1" },
            new[] { "Mr. Levy", "Alice" },
            null, "teacher-1", DateTimeOffset.UtcNow);

        Assert.Equal(2, evt.ParticipantIds.Length);
        Assert.Contains("student-1", evt.ParticipantIds);
        Assert.Contains("teacher-1", evt.ParticipantIds);
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

    [Theory]
    [InlineData(ThreadType.DirectMessage)]
    [InlineData(ThreadType.ClassBroadcast)]
    [InlineData(ThreadType.ParentThread)]
    public void ThreadType_AllValuesAreDefined(ThreadType type)
    {
        Assert.True(Enum.IsDefined(type));
    }

    [Fact]
    public void MessageContent_HasCorrectProperties()
    {
        var content = new MessageContent(
            "Test message", "text", "https://example.com",
            new Dictionary<string, string> { ["key"] = "value" });

        Assert.Equal("Test message", content.Text);
        Assert.Equal("text", content.ContentType);
        Assert.Equal("https://example.com", content.ResourceUrl);
        Assert.Single(content.Metadata!);
    }

    [Fact]
    public void MessageContent_NullOptionalFields()
    {
        var content = new MessageContent("Hello", "text", null, null);
        Assert.Null(content.ResourceUrl);
        Assert.Null(content.Metadata);
    }

    [Fact]
    public void ClassificationResult_LearningSignalProperties()
    {
        var result = new ClassificationResult(true, "quiz-answer", 0.95);
        Assert.True(result.IsLearningSignal);
        Assert.Equal("quiz-answer", result.Intent);
        Assert.Equal(0.95, result.Confidence);
    }

    [Fact]
    public void ModerationResult_Safe()
    {
        var result = new ModerationResult(true, null);
        Assert.True(result.Safe);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void ModerationResult_Blocked()
    {
        var result = new ModerationResult(false, "phone_number_detected");
        Assert.False(result.Safe);
        Assert.Equal("phone_number_detected", result.Reason);
    }

    [Fact]
    public void ModerationResult_FlaggedButSafe()
    {
        var result = new ModerationResult(true, null, "excessive_caps");
        Assert.True(result.Safe);
        Assert.Equal("excessive_caps", result.Flag);
    }

    [Fact]
    public void ThrottleResult_Allowed()
    {
        var result = new ThrottleResult(true);
        Assert.True(result.Allowed);
        Assert.Equal(0, result.RetryAfterSeconds);
    }

    [Fact]
    public void ThrottleResult_Blocked_WithRetry()
    {
        var result = new ThrottleResult(false, 3600);
        Assert.False(result.Allowed);
        Assert.Equal(3600, result.RetryAfterSeconds);
    }

    [Fact]
    public void MessagingResult_Success()
    {
        var result = new MessagingResult(true, MessageId: "msg-1", ThreadId: "t-1");
        Assert.True(result.Success);
        Assert.Equal("msg-1", result.MessageId);
    }

    [Fact]
    public void MessagingResult_Failure()
    {
        var result = new MessagingResult(false, "MESSAGE_TOO_LONG", "Exceeds 2000 chars");
        Assert.False(result.Success);
        Assert.Equal("MESSAGE_TOO_LONG", result.ErrorCode);
    }

    [Fact]
    public void MessageBlocked_V1_CapturesReason()
    {
        var evt = new MessageBlocked_V1("t-1", "parent-1", "email_detected", DateTimeOffset.UtcNow);
        Assert.Equal("email_detected", evt.Reason);
    }

    [Fact]
    public void NatsSubjects_AreCorrectFormat()
    {
        Assert.Equal("cena.messaging.events.MessageSent", MessagingNatsSubjects.MessageSent);
        Assert.Equal("cena.messaging.events.MessageRead", MessagingNatsSubjects.MessageRead);
        Assert.Equal("cena.messaging.events.ThreadCreated", MessagingNatsSubjects.ThreadCreated);
        Assert.Equal("cena.messaging.events.MessageBlocked", MessagingNatsSubjects.MessageBlocked);
        Assert.Equal("cena.messaging.commands.SendMessage", MessagingNatsSubjects.CmdSendMessage);
        Assert.Equal("cena.messaging.commands.RouteInboundReply", MessagingNatsSubjects.CmdRouteInboundReply);
    }
}
