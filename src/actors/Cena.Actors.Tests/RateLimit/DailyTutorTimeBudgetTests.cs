// =============================================================================
// Cena Platform — DailyTutorTimeBudget Unit Tests (prr-048)
//
// Covers the prr-048 extensions layered on the prr-012 baseline:
//   - Percentage-based soft-limit nudge (default 80% of the cap)
//   - Per-institute configuration override of the cap value
//   - Hard-limit block with the ship-gate-compliant take-a-break copy
//   - Idempotent nudge emission (one per student-day)
//   - institute_id + cap_type labeling on the spec-named counters
//   - Fail-open behavior when Redis is unavailable
//   - RenderNudge honest-framing copy (no banned tokens)
//
// Redis is substituted so the tests are deterministic and do not require a
// live server. The collaborators match SocraticCallBudgetTests patterns.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.RateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.RateLimit;

public sealed class DailyTutorTimeBudgetTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();

    public DailyTutorTimeBudgetTests()
    {
        _redis.GetDatabase().Returns(_db);
    }

    private static IConfiguration ConfigWith(params (string Key, string Value)[] overrides)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in overrides) dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private DailyTutorTimeBudget NewSut(IConfiguration? config = null) =>
        new(
            _redis,
            config ?? ConfigWith(),
            NullLogger<DailyTutorTimeBudget>.Instance,
            new DummyMeterFactory());

    // -------------------------------------------------------------------------
    // CheckAsync — allowed/denied boundaries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_NoUsageYet_AllowedWithFullBudget()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var sut = NewSut();
        var result = await sut.CheckAsync("stu-1");

        Assert.True(result.Allowed);
        Assert.Equal(0, result.UsedSeconds);
        Assert.Equal(1800, result.DailyLimitSeconds); // 30 min default
        Assert.Equal(1800, result.RemainingSeconds);
        // Default nudge is 80% of 30 min = 1440 s (24 min).
        Assert.Equal(1440, result.NudgeThresholdSeconds);
    }

    [Fact]
    public async Task CheckAsync_UnderCap_Allowed()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)1200L); // 20 min used

        var sut = NewSut();
        var result = await sut.CheckAsync("stu-1");

        Assert.True(result.Allowed);
        Assert.Equal(1200, result.UsedSeconds);
        Assert.Equal(600, result.RemainingSeconds);
    }

    [Fact]
    public async Task CheckAsync_AtCap_Denied()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)1800L); // exactly at 30 min default

        var sut = NewSut();
        var result = await sut.CheckAsync("stu-1");

        Assert.False(result.Allowed);
        Assert.Equal(0, result.RemainingSeconds);
    }

    [Fact]
    public async Task CheckAsync_BeyondCap_Denied()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)2400L); // 40 min, way past cap

        var sut = NewSut();
        var result = await sut.CheckAsync("stu-1");

        Assert.False(result.Allowed);
        Assert.Equal(0, result.RemainingSeconds);
    }

    [Fact]
    public async Task CheckAsync_RedisDown_FailsOpen()
    {
        // A Redis outage must not block every student from tutoring. The
        // cost circuit breaker is the independent backstop for actual spend.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<RedisValue>(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));

        var sut = NewSut();
        var result = await sut.CheckAsync("stu-1");

        Assert.True(result.Allowed);
        Assert.Equal(0, result.UsedSeconds);
        Assert.Equal(1800, result.DailyLimitSeconds);
    }

    [Fact]
    public async Task CheckAsync_EmptyStudentId_Throws()
    {
        var sut = NewSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CheckAsync(""));
    }

    // -------------------------------------------------------------------------
    // Per-institute override
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_InstituteOverride_UsesOverrideCap()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)900L); // 15 min used

        // Institute "inst-strict" caps at 20 minutes instead of 30.
        var config = ConfigWith(
            ("Cena:Tutor:InstituteOverrides:inst-strict:DailyTimeMinutes", "20"));
        var sut = NewSut(config);

        var result = await sut.CheckAsync("stu-1", instituteId: "inst-strict");

        Assert.True(result.Allowed);
        Assert.Equal(1200, result.DailyLimitSeconds); // 20 min in seconds
        Assert.Equal(300, result.RemainingSeconds);
        // Nudge at 80% of 20 min = 16 min = 960 s
        Assert.Equal(960, result.NudgeThresholdSeconds);
    }

    [Fact]
    public async Task CheckAsync_InstituteOverrideMissing_FallsBackToDefault()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var sut = NewSut();
        var result = await sut.CheckAsync("stu-1", instituteId: "inst-unknown");

        // No override defined → platform default applies.
        Assert.Equal(1800, result.DailyLimitSeconds);
        Assert.Equal(1440, result.NudgeThresholdSeconds);
    }

    [Fact]
    public async Task CheckAsync_InstituteOverrideZero_IgnoredAsSafety()
    {
        // Zero/negative overrides would disable tutoring entirely — treat
        // those as misconfiguration and fall back to the platform default.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var config = ConfigWith(
            ("Cena:Tutor:InstituteOverrides:inst-bad:DailyTimeMinutes", "0"));
        var sut = NewSut(config);

        var result = await sut.CheckAsync("stu-1", instituteId: "inst-bad");

        Assert.Equal(1800, result.DailyLimitSeconds);
    }

    // -------------------------------------------------------------------------
    // Nudge threshold crossing in RecordUsageAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecordUsageAsync_CrossesNudgeThreshold_SetsNudgeKey()
    {
        // Default cap 30 min, nudge at 80% = 1440 s. Previous usage 1400 s,
        // record another 60 s → 1460 s. Crosses from below to above.
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), 60L, Arg.Any<CommandFlags>())
            .Returns(1460L);
        // SE.Redis resolves the 4-arg convenience overload to the 6-arg
        // IDatabase method: (RedisKey, RedisValue, TimeSpan?, bool keepTtl, When, CommandFlags).
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(),
                Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = NewSut();
        await sut.RecordUsageAsync("stu-1", 60, instituteId: "inst-1");

        // The nudge key was SET NX with the expected prefix. We assert the
        // nudge prefix appears in at least one received call because the
        // SUT also sets the legacy warning key separately when warning_min
        // = 25 is crossed at 1460 s.
        var nudgeCallCount = _db.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "StringSetAsync"
                        && c.GetArguments()[0] is RedisKey rk
                        && rk.ToString().StartsWith(DailyTutorTimeBudget.NudgeKeyPrefix));
        Assert.Equal(1, nudgeCallCount);
    }

    [Fact]
    public async Task RecordUsageAsync_AlreadyAboveNudge_DoesNotSetNudgeKey()
    {
        // Previous usage already 1500 (above nudge 1440). Incrementing by 60
        // yields 1560, but we are NOT crossing the boundary — no new nudge.
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), 60L, Arg.Any<CommandFlags>())
            .Returns(1560L);

        var sut = NewSut();
        await sut.RecordUsageAsync("stu-1", 60, instituteId: "inst-1");

        var nudgeCallCount = _db.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "StringSetAsync"
                        && c.GetArguments()[0] is RedisKey rk
                        && rk.ToString().StartsWith(DailyTutorTimeBudget.NudgeKeyPrefix));
        Assert.Equal(0, nudgeCallCount);
    }

    [Fact]
    public async Task RecordUsageAsync_JumpsPastCap_DoesNotNudge_CapHitPath()
    {
        // Single record that shoots from 0 past the entire cap. Per prr-048
        // the nudge fires only when the new usage lands BELOW the cap, so
        // this path skips the nudge and records the hard-cap crossing instead.
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), 2000L, Arg.Any<CommandFlags>())
            .Returns(2000L);

        var sut = NewSut();
        await sut.RecordUsageAsync("stu-1", 2000, instituteId: "inst-1");

        var nudgeCallCount = _db.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "StringSetAsync"
                        && c.GetArguments()[0] is RedisKey rk
                        && rk.ToString().StartsWith(DailyTutorTimeBudget.NudgeKeyPrefix));
        Assert.Equal(0, nudgeCallCount);
    }

    [Fact]
    public async Task RecordUsageAsync_ZeroSeconds_NoOp()
    {
        var sut = NewSut();
        await sut.RecordUsageAsync("stu-1", 0);

        await _db.DidNotReceive().StringIncrementAsync(
            Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RecordUsageAsync_RedisDown_Swallowed()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<long>(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));

        var sut = NewSut();
        // Must not throw — a downed Redis cannot cause the tutor turn to
        // fail post-hoc (the student has already been served).
        await sut.RecordUsageAsync("stu-1", 30);
    }

    [Fact]
    public async Task RecordUsageAsync_EmptyStudentId_Throws()
    {
        var sut = NewSut();
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RecordUsageAsync("", 30));
    }

    // -------------------------------------------------------------------------
    // Integration: nudge → hard-limit transition within a single student-day
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NudgeThenHardLimit_TransitionsCleanly()
    {
        // Scenario: student is at 1400 s. We record a 60 s turn that lands
        // them at 1460 s (past the 1440 s nudge, still under cap). Then
        // another turn at 380 s that crosses the 1800 s cap. The check
        // after the second turn returns Allowed=false.
        //
        // Two sequential INCR calls are modeled with successive return values.
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), 60L, Arg.Any<CommandFlags>())
            .Returns(1460L);
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), 380L, Arg.Any<CommandFlags>())
            .Returns(1840L);
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(),
                Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = NewSut();

        // Turn 1 — crosses nudge threshold.
        await sut.RecordUsageAsync("stu-1", 60, instituteId: "inst-1");
        // Turn 2 — crosses hard cap.
        await sut.RecordUsageAsync("stu-1", 380, instituteId: "inst-1");

        // Check after hard-cap crossing returns Allowed=false.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)1840L);
        var after = await sut.CheckAsync("stu-1", instituteId: "inst-1");
        Assert.False(after.Allowed);
        Assert.Equal(0, after.RemainingSeconds);
    }

    // -------------------------------------------------------------------------
    // Honest-framing copy — no banned tokens, reports real numbers
    // -------------------------------------------------------------------------

    [Fact]
    public void TakeBreakMessage_IsShipGateCompliant()
    {
        // The hard-limit copy must not contain any banned dark-pattern tokens.
        var msg = DailyTutorTimeBudget.TakeBreakMessage;
        foreach (var banned in BannedTokens)
        {
            Assert.DoesNotContain(banned, msg, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("tomorrow", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderNudge_QuotesRealNumbers_NoBannedTokens()
    {
        // 1440 s used, 360 s remaining → "24 minutes … 6 minutes left".
        var copy = DailyTutorTimeBudget.RenderNudge(usedSeconds: 1440, remainingSeconds: 360);

        Assert.Contains("24 minutes", copy);
        Assert.Contains("6 minutes", copy);
        foreach (var banned in BannedTokens)
        {
            Assert.DoesNotContain(banned, copy, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RenderNudge_ZeroRemaining_StillHonest()
    {
        // Edge: nudge fires and the student pushes right up against the cap.
        // We render "0 minutes left" honestly rather than rounding up.
        var copy = DailyTutorTimeBudget.RenderNudge(usedSeconds: 1800, remainingSeconds: 0);
        Assert.Contains("0 minutes left", copy);
    }

    /// <summary>
    /// Ship-gate banned tokens that appear in the dark-pattern scanner
    /// (scripts/shipgate/scan.mjs and banned-mechanics.yml). Asserted here so
    /// that any regression in the user-facing copy fails the unit tests BEFORE
    /// the ship-gate CI runs — honours the "fail loudly" rule.
    /// </summary>
    private static readonly string[] BannedTokens =
    [
        "streak",
        "don't break",
        "don't miss",
        "keep the chain",
        "you'll lose",
        "don't waste",
        "running out of time",
        "hurry",
        "only",          // "only X minutes left" — FOMO framing
        "out of time",
        "last chance",
        "countdown",
    ];

    // -------------------------------------------------------------------------
    // Key formatting — load-bearing for metrics/alerting
    // -------------------------------------------------------------------------

    [Fact]
    public void UsageKey_UsesStudentScopedPrefix()
    {
        var key = DailyTutorTimeBudget.BuildUsageKey("abc-123");
        Assert.StartsWith("cena:tutor:daily:abc-123:", key);
    }

    [Fact]
    public void NudgeKey_UsesDistinctPrefix()
    {
        // The nudge key prefix must NOT collide with either the usage
        // counter prefix or the legacy warning prefix, or idempotency breaks.
        var usage = DailyTutorTimeBudget.BuildUsageKey("abc-123");
        var warning = DailyTutorTimeBudget.BuildWarningKey("abc-123");
        var nudge = DailyTutorTimeBudget.BuildNudgeKey("abc-123");

        Assert.NotEqual(usage, warning);
        Assert.NotEqual(usage, nudge);
        Assert.NotEqual(warning, nudge);
        Assert.StartsWith("cena:tutor:daily:nudge:abc-123:", nudge);
    }

    [Fact]
    public void NormalizeInstituteLabel_Null_ReturnsUnknown()
    {
        Assert.Equal("unknown", DailyTutorTimeBudget.NormalizeInstituteLabel(null));
        Assert.Equal("unknown", DailyTutorTimeBudget.NormalizeInstituteLabel(""));
        Assert.Equal("unknown", DailyTutorTimeBudget.NormalizeInstituteLabel("   "));
        Assert.Equal("inst-1", DailyTutorTimeBudget.NormalizeInstituteLabel("inst-1"));
    }

    // -------------------------------------------------------------------------
    // Infrastructure: dummy meter factory (NSubstitute cannot stub sealed Meter)
    // -------------------------------------------------------------------------

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
