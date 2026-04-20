// =============================================================================
// Cena Platform — Outbound SMS gateway tests (prr-018).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Notifications;
using Cena.Actors.Notifications.OutboundSms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class OutboundSmsGatewayTests
{
    private static OutboundSmsGatewayRequest Req(string body = "Hi Rami, Noa studied 3h.")
        => new(
            InstituteId: "inst-1",
            ParentPhoneE164: "+972501234567",
            ParentTimezone: "Asia/Jerusalem",
            Body: body,
            TemplateId: "weekly-digest-v1",
            CorrelationId: "corr-1",
            ScheduledForUtc: new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));

    private static OutboundSmsGateway NewGateway(
        IOutboundSmsPolicyChain chain,
        ISmsSender sender)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new OutboundSmsGateway(
            chain,
            sender,
            cfg,
            new DummyMeterFactory(),
            NullLogger<OutboundSmsGateway>.Instance);
    }

    [Fact]
    public async Task Sent_OnAllow_InvokesSender()
    {
        var chain = Substitute.For<IOutboundSmsPolicyChain>();
        chain.EvaluateAsync(Arg.Any<OutboundSmsRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new OutboundSmsDecision(
                new SmsPolicyOutcome.Allow(ci.Arg<OutboundSmsRequest>()),
                new[] { "sanitizer", "shipgate", "rate_limit", "quiet_hours" },
                null)));

        var sender = Substitute.For<ISmsSender>();
        sender.IsConfigured.Returns(true);
        sender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true));

        var gateway = NewGateway(chain, sender);
        var result = await gateway.SendAsync(Req());

        var sent = Assert.IsType<SmsGatewayResult.Sent>(result);
        Assert.True(sent.VendorResult.Success);
        await sender.Received(1).SendAsync(
            "+972501234567", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blocked_OnBlock_DoesNotInvokeSender()
    {
        var chain = Substitute.For<IOutboundSmsPolicyChain>();
        chain.EvaluateAsync(Arg.Any<OutboundSmsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OutboundSmsDecision(
                new SmsPolicyOutcome.Block("streak_copy", "banned copy"),
                new[] { "sanitizer", "shipgate" },
                "shipgate")));

        var sender = Substitute.For<ISmsSender>();
        var gateway = NewGateway(chain, sender);

        var result = await gateway.SendAsync(Req());
        var blocked = Assert.IsType<SmsGatewayResult.Blocked>(result);
        Assert.Equal("shipgate", blocked.Policy);
        Assert.Equal("streak_copy", blocked.Reason);

        await sender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deferred_OnDefer_DoesNotInvokeSender()
    {
        var earliest = new DateTimeOffset(2026, 7, 21, 4, 0, 0, TimeSpan.Zero);
        var chain = Substitute.For<IOutboundSmsPolicyChain>();
        chain.EvaluateAsync(Arg.Any<OutboundSmsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OutboundSmsDecision(
                new SmsPolicyOutcome.Defer("quiet_hours", earliest),
                new[] { "sanitizer", "shipgate", "rate_limit", "quiet_hours" },
                "quiet_hours")));

        var sender = Substitute.For<ISmsSender>();
        var gateway = NewGateway(chain, sender);

        var result = await gateway.SendAsync(Req());
        var deferred = Assert.IsType<SmsGatewayResult.Deferred>(result);
        Assert.Equal(earliest, deferred.EarliestSendAtUtc);

        await sender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HashPhone_IsStableAcrossCalls()
    {
        var chain = Substitute.For<IOutboundSmsPolicyChain>();
        var sender = Substitute.For<ISmsSender>();
        var gateway = NewGateway(chain, sender);

        var a = gateway.HashPhone("+972501234567");
        var b = gateway.HashPhone("+972501234567");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HashPhone_DiffersAcrossNumbers()
    {
        var chain = Substitute.For<IOutboundSmsPolicyChain>();
        var sender = Substitute.For<ISmsSender>();
        var gateway = NewGateway(chain, sender);

        var a = gateway.HashPhone("+972501234567");
        var b = gateway.HashPhone("+972501234568");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashPhone_DoesNotContainPlaintext()
    {
        var chain = Substitute.For<IOutboundSmsPolicyChain>();
        var sender = Substitute.For<ISmsSender>();
        var gateway = NewGateway(chain, sender);

        var h = gateway.HashPhone("+972501234567");
        Assert.DoesNotContain("972", h);
        Assert.DoesNotContain("+", h);
        // 16 lowercase hex chars expected.
        Assert.Equal(16, h.Length);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
