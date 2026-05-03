using Cena.Actors.Events;
using Cena.Actors.Messaging;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Actors.Tests.Messaging;

public sealed class MessagingNatsPublisherTests
{
    private readonly INatsConnection _nats;
    private readonly MessagingNatsPublisher _publisher;
    private readonly ILogger<MessagingNatsPublisher> _logger;

    public MessagingNatsPublisherTests()
    {
        _nats = Substitute.For<INatsConnection>();
        _logger = Substitute.For<ILogger<MessagingNatsPublisher>>();
        _publisher = new MessagingNatsPublisher(_nats, _logger);
    }

    [Fact]
    public async Task PublishMessageSent_CallsNatsPublish()
    {
        var evt = new MessageSent_V1(
            "t-1", "msg-1", "teacher-1", MessageRole.Teacher,
            new MessageContent("Hello!", "text", null, null),
            MessageChannel.InApp, DateTimeOffset.UtcNow, null);

        await _publisher.PublishMessageSentAsync(evt);

        // Verify PublishAsync was called (any overload)
        await _nats.ReceivedWithAnyArgs(1).PublishAsync(
            default(string)!, default(byte[])!, default, default, default, default, default);
    }

    [Fact]
    public async Task PublishThreadCreated_CallsNatsPublish()
    {
        var evt = new ThreadCreated_V1(
            "t-1", "DirectMessage",
            new[] { "teacher-1", "student-1" },
            new[] { "Mr. Levy", "Alice" },
            null, "teacher-1", DateTimeOffset.UtcNow);

        await _publisher.PublishThreadCreatedAsync(evt);

        await _nats.ReceivedWithAnyArgs(1).PublishAsync(
            default(string)!, default(byte[])!, default, default, default, default, default);
    }

    [Fact]
    public async Task PublishMessageBlocked_CallsNatsPublish()
    {
        var evt = new MessageBlocked_V1(
            "t-1", "parent-1", "phone_number_detected", DateTimeOffset.UtcNow);

        await _publisher.PublishMessageBlockedAsync(evt);

        await _nats.ReceivedWithAnyArgs(1).PublishAsync(
            default(string)!, default(byte[])!, default, default, default, default, default);
    }

    [Fact]
    public async Task PublishFailure_DoesNotThrow()
    {
        _nats.WhenForAnyArgs(x => x.PublishAsync(
            default(string)!, default(byte[])!, default, default, default, default, default))
            .Throw(new Exception("NATS connection lost"));

        // Should NOT throw
        var ex = await Record.ExceptionAsync(() =>
            _publisher.PublishMessageSentAsync(new MessageSent_V1(
                "t-1", "msg-1", "teacher-1", MessageRole.Teacher,
                new MessageContent("Test", "text", null, null),
                MessageChannel.InApp, DateTimeOffset.UtcNow, null)));

        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishInboundReceived_CallsNatsPublish()
    {
        await _publisher.PublishInboundReceivedAsync("whatsapp", "SM001", "Hello!");

        await _nats.ReceivedWithAnyArgs(1).PublishAsync(
            default(string)!, default(byte[])!, default, default, default, default, default);
    }
}
