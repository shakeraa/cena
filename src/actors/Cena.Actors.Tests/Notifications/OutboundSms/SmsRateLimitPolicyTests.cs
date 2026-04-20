// =============================================================================
// Cena Platform — SMS rate-limit policy tests (prr-018).
//
// These tests stub StackExchange.Redis with NSubstitute. The policy issues
// two IDatabase calls per cap check (remove-expired + zcard) and one pair
// per cap on the record path (zadd + expire).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Infrastructure;
using Cena.Actors.Notifications.OutboundSms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Notifications.OutboundSms;

public sealed class SmsRateLimitPolicyTests
{
    private static OutboundSmsRequest Req(
        string instituteId = "inst-1",
        string phoneHash = "hash-abcd",
        DateTimeOffset? scheduledForUtc = null) => new(
        InstituteId: instituteId,
        ParentPhoneE164: "+972501234567",
        ParentPhoneHash: phoneHash,
        ParentTimezone: "Asia/Jerusalem",
        Body: "Clean body within limits.",
        TemplateId: "weekly-digest-v1",
        CorrelationId: "corr-1",
        ScheduledForUtc: scheduledForUtc ?? new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero));

    private static (SmsRateLimitPolicy Policy, IDatabase Db) NewPolicy(
        Dictionary<string, string?>? config = null,
        long defaultZcardResponse = 0)
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.SortedSetLengthAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<Exclude>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(defaultZcardResponse));
        db.SortedSetRemoveRangeByScoreAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<Exclude>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));
        db.SortedSetAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<double>(),
            Arg.Any<SortedSetWhen>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));
        db.KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<ExpireWhen>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new())
            .Build();

        var policy = new SmsRateLimitPolicy(
            redis,
            cfg,
            new FakeClock(new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero)),
            new DummyMeterFactory(),
            NullLogger<SmsRateLimitPolicy>.Instance);

        return (policy, db);
    }

    [Fact]
    public async Task Allow_WhenAllCountsBelowCap()
    {
        var (policy, db) = NewPolicy(defaultZcardResponse: 0);
        var outcome = await policy.EvaluateAsync(Req());
        Assert.IsType<SmsPolicyOutcome.Allow>(outcome);

        // Allow path records against each of three keys (phone, institute, global).
        await db.Received(3).SortedSetAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<double>(),
            Arg.Any<SortedSetWhen>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Block_PerPhoneCapAtBoundary()
    {
        // With PerPhonePer24h=10 (default), a count of 10 is AT the cap → block.
        var (policy, _) = NewPolicy(defaultZcardResponse: 10);
        var outcome = await policy.EvaluateAsync(Req());
        var block = Assert.IsType<SmsPolicyOutcome.Block>(outcome);
        Assert.Equal("per_phone", block.Reason);
    }

    [Fact]
    public async Task Allow_PerPhoneCapOneBelowBoundary()
    {
        var (policy, _) = NewPolicy(defaultZcardResponse: 9);
        var outcome = await policy.EvaluateAsync(Req());
        Assert.IsType<SmsPolicyOutcome.Allow>(outcome);
    }

    [Fact]
    public async Task Block_PerPhoneRespectsPerInstituteOverride()
    {
        var (policy, _) = NewPolicy(
            config: new()
            {
                ["Cena:Sms:RateLimit:PerInstitutePhoneOverrides:inst-9"] = "2",
            },
            defaultZcardResponse: 2);
        var outcome = await policy.EvaluateAsync(Req(instituteId: "inst-9"));
        var block = Assert.IsType<SmsPolicyOutcome.Block>(outcome);
        Assert.Equal("per_phone", block.Reason);
    }

    [Fact]
    public async Task FailOpen_OnRedisOutage()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase()
            .Returns(ci => { throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "simulated outage"); });

        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var policy = new SmsRateLimitPolicy(
            redis, cfg,
            new FakeClock(DateTimeOffset.UtcNow),
            new DummyMeterFactory(),
            NullLogger<SmsRateLimitPolicy>.Instance);

        var outcome = await policy.EvaluateAsync(Req());
        // Policy documents fail-OPEN: Redis outage allows the chain to continue.
        Assert.IsType<SmsPolicyOutcome.Allow>(outcome);
    }

    [Fact]
    public void PhoneKey_IncludesHashOnly()
    {
        // The rate-limit keys MUST use the salted phone hash, never the raw
        // E.164 number. This is what makes the Redis keyspace log-safe.
        var key = SmsRateLimitPolicy.BuildPhoneKey("hash-abcd");
        Assert.Equal("sms:limit:phone:hash-abcd", key.ToString());
        Assert.DoesNotContain("+", key.ToString());
    }

    [Fact]
    public void InstituteKey_PrefixIsStable()
    {
        var key = SmsRateLimitPolicy.BuildInstituteKey("inst-1");
        Assert.Equal("sms:limit:institute:inst-1", key.ToString());
    }

    // ---------------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------------

    private sealed class FakeClock : IClock
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
        public DateTime UtcDateTime => _now.UtcDateTime;
        public DateTime LocalDateTime => _now.LocalDateTime;
        public string FormatUtc(string format) => _now.UtcDateTime.ToString(format);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
