using Cena.Actors.Events;
using Cena.Actors.Messaging;

namespace Cena.Actors.Tests.Messaging;

/// <summary>
/// Tests the ThreadSummaryProjection event handlers directly
/// (no Marten infrastructure needed — just method calls).
/// </summary>
public sealed class ThreadSummaryProjectionTests
{
    private readonly ThreadSummaryProjection _projection = new();

    [Fact]
    public void Create_FromThreadCreated_SetsAllFields()
    {
        var evt = new ThreadCreated_V1(
            ThreadId: "t-1",
            ThreadType: "DirectMessage",
            ParticipantIds: new[] { "teacher-1", "student-1" },
            ParticipantNames: new[] { "Mr. Levy", "Alice" },
            ClassRoomId: null,
            CreatedById: "teacher-1",
            CreatedAt: DateTimeOffset.Parse("2026-03-27T10:00:00Z"));

        var summary = _projection.Create(evt);

        Assert.Equal("t-1", summary.Id);
        Assert.Equal("DirectMessage", summary.ThreadType);
        Assert.Equal(2, summary.ParticipantIds.Length);
        Assert.Contains("teacher-1", summary.ParticipantIds);
        Assert.Contains("student-1", summary.ParticipantIds);
        Assert.Null(summary.ClassRoomId);
        Assert.Equal("teacher-1", summary.CreatedById);
        Assert.Equal(0, summary.MessageCount);
        Assert.Equal("", summary.LastMessagePreview);
    }

    [Fact]
    public void Apply_MessageSent_UpdatesPreviewAndCount()
    {
        var summary = CreateTestSummary();

        _projection.Apply(new MessageSent_V1(
            ThreadId: "t-1", MessageId: "msg-1",
            SenderId: "teacher-1", SenderRole: MessageRole.Teacher,
            Content: new MessageContent("Review fractions tonight!", "text", null, null),
            Channel: MessageChannel.InApp,
            SentAt: DateTimeOffset.Parse("2026-03-27T14:00:00Z"),
            ReplyToMessageId: null), summary);

        Assert.Equal(1, summary.MessageCount);
        Assert.Equal("Review fractions tonight!", summary.LastMessagePreview);
        Assert.Equal(DateTimeOffset.Parse("2026-03-27T14:00:00Z"), summary.LastMessageAt);
    }

    [Fact]
    public void Apply_MultipleMessages_IncrementsCount()
    {
        var summary = CreateTestSummary();

        for (int i = 0; i < 5; i++)
        {
            _projection.Apply(new MessageSent_V1(
                "t-1", $"msg-{i}", "teacher-1", MessageRole.Teacher,
                new MessageContent($"Message {i}", "text", null, null),
                MessageChannel.InApp, DateTimeOffset.UtcNow.AddMinutes(i),
                null), summary);
        }

        Assert.Equal(5, summary.MessageCount);
        Assert.Equal("Message 4", summary.LastMessagePreview);
    }

    [Fact]
    public void Apply_LongMessage_TruncatesPreviewTo100Chars()
    {
        var summary = CreateTestSummary();
        var longText = new string('x', 200);

        _projection.Apply(new MessageSent_V1(
            "t-1", "msg-1", "teacher-1", MessageRole.Teacher,
            new MessageContent(longText, "text", null, null),
            MessageChannel.InApp, DateTimeOffset.UtcNow, null), summary);

        Assert.Equal(100, summary.LastMessagePreview.Length);
    }

    [Fact]
    public void Create_ClassBroadcast_IncludesClassRoomId()
    {
        var evt = new ThreadCreated_V1(
            "t-class", "ClassBroadcast",
            new[] { "teacher-1", "student-1", "student-2" },
            new[] { "Mr. Levy", "Alice", "Bob" },
            ClassRoomId: "class-1",
            "teacher-1", DateTimeOffset.UtcNow);

        var summary = _projection.Create(evt);

        Assert.Equal("ClassBroadcast", summary.ThreadType);
        Assert.Equal("class-1", summary.ClassRoomId);
        Assert.Equal(3, summary.ParticipantIds.Length);
    }

    private static ThreadSummary CreateTestSummary() => new()
    {
        Id = "t-1",
        ThreadType = "DirectMessage",
        ParticipantIds = new[] { "teacher-1", "student-1" },
        ParticipantNames = new[] { "Mr. Levy", "Alice" },
        MessageCount = 0,
        CreatedById = "teacher-1",
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
