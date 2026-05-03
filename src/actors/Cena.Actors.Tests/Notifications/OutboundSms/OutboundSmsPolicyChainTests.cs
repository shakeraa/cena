// =============================================================================
// Cena Platform — Policy chain composition tests (prr-018).
// =============================================================================

using Cena.Actors.Notifications.OutboundSms;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class OutboundSmsPolicyChainTests
{
    private static OutboundSmsRequest Req() => new(
        InstituteId: "inst-1",
        ParentPhoneE164: "+972501234567",
        ParentPhoneHash: "hash-abcd",
        ParentTimezone: "Asia/Jerusalem",
        Body: "clean",
        TemplateId: "tpl",
        CorrelationId: "corr",
        ScheduledForUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void EmptyChain_ThrowsAtConstruction()
    {
        // Fail-fast discipline: you cannot configure a zero-policy chain.
        Assert.Throws<InvalidOperationException>(() =>
            new OutboundSmsPolicyChain(
                Array.Empty<IOutboundSmsPolicy>(),
                NullLogger<OutboundSmsPolicyChain>.Instance));
    }

    [Fact]
    public async Task AllAllow_ReturnsFinalAllow()
    {
        var chain = new OutboundSmsPolicyChain(
            new IOutboundSmsPolicy[]
            {
                new AllowingPolicy("a"),
                new AllowingPolicy("b"),
            },
            NullLogger<OutboundSmsPolicyChain>.Instance);

        var decision = await chain.EvaluateAsync(Req());
        Assert.IsType<SmsPolicyOutcome.Allow>(decision.FinalOutcome);
        Assert.Equal(new[] { "a", "b" }, decision.EvaluatedPolicies.ToArray());
        Assert.Null(decision.TerminatingPolicy);
    }

    [Fact]
    public async Task FirstBlock_TerminatesChain()
    {
        var chain = new OutboundSmsPolicyChain(
            new IOutboundSmsPolicy[]
            {
                new AllowingPolicy("a"),
                new BlockingPolicy("b", "evil-body"),
                new AllowingPolicy("c"),
            },
            NullLogger<OutboundSmsPolicyChain>.Instance);

        var decision = await chain.EvaluateAsync(Req());
        var block = Assert.IsType<SmsPolicyOutcome.Block>(decision.FinalOutcome);
        Assert.Equal("evil-body", block.Reason);
        Assert.Equal(new[] { "a", "b" }, decision.EvaluatedPolicies.ToArray());
        Assert.Equal("b", decision.TerminatingPolicy);
    }

    [Fact]
    public async Task FirstDefer_TerminatesChain()
    {
        var earliest = DateTimeOffset.UtcNow.AddHours(8);
        var chain = new OutboundSmsPolicyChain(
            new IOutboundSmsPolicy[]
            {
                new AllowingPolicy("a"),
                new DeferringPolicy("b", "quiet", earliest),
            },
            NullLogger<OutboundSmsPolicyChain>.Instance);

        var decision = await chain.EvaluateAsync(Req());
        var defer = Assert.IsType<SmsPolicyOutcome.Defer>(decision.FinalOutcome);
        Assert.Equal(earliest, defer.EarliestSendAtUtc);
        Assert.Equal("b", decision.TerminatingPolicy);
    }

    [Fact]
    public async Task Allow_BodyRewriteFlowsDownChain()
    {
        // AllowingPolicy("a") rewrites the body to "rewritten-a". The next
        // policy must see the rewritten body, not the original.
        var seenByB = "";
        var chain = new OutboundSmsPolicyChain(
            new IOutboundSmsPolicy[]
            {
                new RewritingPolicy("a", "rewritten-a"),
                new CapturingPolicy("b", body => seenByB = body),
            },
            NullLogger<OutboundSmsPolicyChain>.Instance);

        await chain.EvaluateAsync(Req());
        Assert.Equal("rewritten-a", seenByB);
    }

    // Test doubles ----------------------------------------------------------

    private sealed class AllowingPolicy : IOutboundSmsPolicy
    {
        public AllowingPolicy(string name) => Name = name;
        public string Name { get; }
        public Task<SmsPolicyOutcome> EvaluateAsync(OutboundSmsRequest r, CancellationToken ct = default)
            => Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Allow(r));
    }

    private sealed class BlockingPolicy : IOutboundSmsPolicy
    {
        private readonly string _reason;
        public BlockingPolicy(string name, string reason) { Name = name; _reason = reason; }
        public string Name { get; }
        public Task<SmsPolicyOutcome> EvaluateAsync(OutboundSmsRequest r, CancellationToken ct = default)
            => Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Block(_reason, "blocked by test"));
    }

    private sealed class DeferringPolicy : IOutboundSmsPolicy
    {
        private readonly string _reason;
        private readonly DateTimeOffset _earliest;
        public DeferringPolicy(string name, string reason, DateTimeOffset earliest)
        {
            Name = name; _reason = reason; _earliest = earliest;
        }
        public string Name { get; }
        public Task<SmsPolicyOutcome> EvaluateAsync(OutboundSmsRequest r, CancellationToken ct = default)
            => Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Defer(_reason, _earliest));
    }

    private sealed class RewritingPolicy : IOutboundSmsPolicy
    {
        private readonly string _newBody;
        public RewritingPolicy(string name, string newBody) { Name = name; _newBody = newBody; }
        public string Name { get; }
        public Task<SmsPolicyOutcome> EvaluateAsync(OutboundSmsRequest r, CancellationToken ct = default)
            => Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Allow(r.WithBody(_newBody)));
    }

    private sealed class CapturingPolicy : IOutboundSmsPolicy
    {
        private readonly Action<string> _capture;
        public CapturingPolicy(string name, Action<string> capture) { Name = name; _capture = capture; }
        public string Name { get; }
        public Task<SmsPolicyOutcome> EvaluateAsync(OutboundSmsRequest r, CancellationToken ct = default)
        {
            _capture(r.Body);
            return Task.FromResult<SmsPolicyOutcome>(new SmsPolicyOutcome.Allow(r));
        }
    }
}
