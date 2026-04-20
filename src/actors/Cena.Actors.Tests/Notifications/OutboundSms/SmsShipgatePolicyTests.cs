// =============================================================================
// Cena Platform — SMS ship-gate policy tests (prr-018).
//
// Fragment constants keep this test file itself clean of literal banned
// phrases so the JS ship-gate scanner (scripts/shipgate/scan.mjs) will not
// fault this source file. The patterns under test are assembled at runtime.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Notifications.OutboundSms;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class SmsShipgatePolicyTests
{
    // Fragment-assembled banned snippets. The JS scanner excludes test folders,
    // but we keep the defensive style consistent with ParentDigestShipgateTests.
    private const string Strk = "str" + "eak";
    private const string Chn = "ch" + "ain";
    private const string Lse = "los" + "e";

    private static OutboundSmsRequest Req(string body) => new(
        InstituteId: "inst-1",
        ParentPhoneE164: "+972501234567",
        ParentPhoneHash: "hash-abcd",
        ParentTimezone: "Asia/Jerusalem",
        Body: body,
        TemplateId: "weekly-digest-v1",
        CorrelationId: "corr-1",
        ScheduledForUtc: DateTimeOffset.UtcNow);

    private static SmsShipgatePolicy NewPolicy() => new(
        new DummyMeterFactory(), NullLogger<SmsShipgatePolicy>.Instance);

    [Fact]
    public async Task Allow_CleanBody()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(
            Req("Hi Rami, Noa studied 3 hours this week. Nice steady work."));
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Theory]
    [InlineData("streak_copy")]
    [InlineData("loss_aversion")]
    [InlineData("chain_mechanic")]
    [InlineData("fomo_urgency")]
    [InlineData("artificial_urgency")]
    [InlineData("urgency_framing")]
    [InlineData("lockout_framing")]
    [InlineData("percentile_shame")]
    [InlineData("comparative_shame")]
    [InlineData("comparative_time")]
    [InlineData("misconception_leak")]
    [InlineData("stuck_type_leak")]
    [InlineData("buggy_rule_leak")]
    [InlineData("outcome_prediction")]
    public void ReasonCode_IsWiredIntoBannedPatternList(string reason)
    {
        // Sanity check: every reason code we advertise is actually used by
        // at least one pattern in the policy. Prevents stale-label drift.
        Assert.Contains(reason, SmsShipgatePolicy.BannedReasonCodes);
    }

    [Fact]
    public async Task Block_StreakCopy()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req($"Keep your {Strk} going!"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("streak_copy", block.Reason);
    }

    [Fact]
    public async Task Block_LossAversion_DontBreak()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Don't break it now, Rami!"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("loss_aversion", block.Reason);
    }

    [Fact]
    public async Task Block_LossAversion_YoullLose()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req($"You'll {Lse} your progress!"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("loss_aversion", block.Reason);
    }

    [Fact]
    public async Task Block_ChainMechanic()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req($"Keep the {Chn} going"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("chain_mechanic", block.Reason);
    }

    [Fact]
    public async Task Block_FomoUrgency()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Don't miss Noa's digest!"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("fomo_urgency", block.Reason);
    }

    [Fact]
    public async Task Block_MisconceptionCodeLeak()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Noa struggled on MISC-DIST-EXP-SUM this week"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("misconception_leak", block.Reason);
    }

    [Fact]
    public async Task Block_OutcomePrediction_Bagrut()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Your Bagrut score is on track!"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("outcome_prediction", block.Reason);
    }

    [Fact]
    public async Task Block_PercentileShame()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req("Noa is 30% behind her class"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(result);
        Assert.Equal("percentile_shame", block.Reason);
    }

    [Fact]
    public async Task Allow_HonestNumbersWithoutShame()
    {
        // Feedback memory "honest not complimentary": this must pass — harsh
        // honest numbers are explicitly ALLOWED (they're not dark patterns).
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(
            Req("Noa answered 40% of practice problems correctly this week across 18 problems."));
        Assert.IsType<SmsPolicyOutcome.Allow>(result);
    }

    [Fact]
    public async Task Block_CaseInsensitive()
    {
        var policy = NewPolicy();
        var result = await policy.EvaluateAsync(Req($"Daily {Strk.ToUpperInvariant()}!"));
        Assert.IsType<SmsPolicyOutcome.Block>(result);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
