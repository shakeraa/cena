// =============================================================================
// Cena Platform — Prompt cache behavioural tests (prr-047)
//
// These tests pin the behaviour of RedisPromptCache + PromptCacheKeyBuilder
// without depending on a live Redis instance. The approach mirrors
// Cena.Actors.Tests/Services/ExplanationCacheServiceTests — NSubstitute on
// IConnectionMultiplexer/IDatabase — so the CI container doesn't need Redis.
//
// Coverage:
//   1. Warm cache: first GET miss + SET populates; second GET hit.
//   2. TTL: SET is invoked with the exact TimeSpan the caller supplied.
//   3. Cross-tenant isolation: same (questionId, errorType) in two tenants
//      produces distinct keys, so a hit for tenant A cannot serve tenant B.
//   4. Key-builder guards: empty segments, colons, tenant prefix shape.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Llm;

public sealed class PromptCacheIntegrationTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly IMeterFactory _meterFactory = new TestMeterFactory();
    private readonly IPromptCacheKeyContext _keyContext = new AsyncLocalPromptCacheKeyContext();
    private readonly RedisPromptCache _cache;

    public PromptCacheIntegrationTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);

        _cache = new RedisPromptCache(
            _redis,
            NullLogger<RedisPromptCache>.Instance,
            _meterFactory,
            _keyContext);
    }

    public void Dispose() => _meterFactory.Dispose();

    // ── Warm-cache behaviour ──────────────────────────────────────────────

    [Fact]
    public async Task WarmCache_FirstCallMisses_SecondCallHits()
    {
        var key = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", "school-42");

        // First call: Redis returns empty → miss.
        _db.StringGetAsync(key, Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var first = await _cache.TryGetAsync(key, "explain", "answer_evaluation", CancellationToken.None);
        Assert.False(first.found);
        Assert.Equal(string.Empty, first.response);

        // Populate. Verify the TTL is exactly what the caller passed (30 days
        // for explain per routing-config.yaml §6). We inspect ReceivedCalls
        // rather than Received().StringSetAsync() because the impl uses the
        // 3-arg overload whose signature-match against a 6-arg matcher is
        // brittle; the existing ExplanationCacheServiceTests use the same
        // pattern for the same reason.
        var ttl = TimeSpan.FromDays(30);
        await _cache.SetAsync(key, "cached-response-body", ttl, "explain", "answer_evaluation", CancellationToken.None);

        var setCalls = _db.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "StringSetAsync")
            .ToList();
        Assert.Single(setCalls);
        var args = setCalls[0].GetArguments();
        Assert.Equal(key, args[0]!.ToString());
        Assert.Equal("cached-response-body", args[1]!.ToString());
        // TTL travels as StackExchange.Redis.Expiration which prints as
        // "EX <seconds>". The round-trip proves the TTL our caller asked for
        // reached the Redis driver unchanged.
        Assert.Equal($"EX {(int)ttl.TotalSeconds}", args[2]!.ToString());

        // Second call: Redis returns the value → hit.
        _db.StringGetAsync(key, Arg.Any<CommandFlags>())
            .Returns(new RedisValue("cached-response-body"));

        var second = await _cache.TryGetAsync(key, "explain", "answer_evaluation", CancellationToken.None);
        Assert.True(second.found);
        Assert.Equal("cached-response-body", second.response);
    }

    // ── TTL propagation ───────────────────────────────────────────────────

    [Theory]
    [InlineData(3600, "sys")]     // 1h system prompt per routing-config §6
    [InlineData(300, "ctx")]      // 5min student context per routing-config §6
    [InlineData(2_592_000, "explain")] // 30d explanation per SAI-003
    public async Task SetAsync_PropagatesExactTtl(int ttlSeconds, string cacheType)
    {
        var key = cacheType switch
        {
            "sys" => PromptCacheKeyBuilder.ForSystemPrompt("explain-math-v3"),
            "ctx" => PromptCacheKeyBuilder.ForStudentContext("anon-1", "ctxhash", "school-42"),
            _ => PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", "school-42"),
        };
        var ttl = TimeSpan.FromSeconds(ttlSeconds);

        await _cache.SetAsync(key, "body", ttl, cacheType, "answer_evaluation", CancellationToken.None);

        var setCalls = _db.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "StringSetAsync")
            .ToList();
        Assert.Single(setCalls);
        var args = setCalls[0].GetArguments();
        Assert.Equal(key, args[0]!.ToString());
        Assert.Equal("body", args[1]!.ToString());
        // Driver renders TimeSpan as Expiration "EX <seconds>".
        Assert.Equal($"EX {(int)ttl.TotalSeconds}", args[2]!.ToString());
    }

    [Fact]
    public async Task SetAsync_RejectsZeroOrNegativeTtl()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _cache.SetAsync("cena:x", "body", TimeSpan.Zero, "sys", "t", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _cache.SetAsync("cena:x", "body", TimeSpan.FromSeconds(-1), "sys", "t", CancellationToken.None));
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────

    [Fact]
    public async Task CrossTenant_SameQuestionAndError_ProducesDistinctKeys()
    {
        var keyA = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", "school-a");
        var keyB = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", "school-b");

        Assert.NotEqual(keyA, keyB);
        Assert.Contains("school-a", keyA, StringComparison.Ordinal);
        Assert.Contains("school-b", keyB, StringComparison.Ordinal);

        // Tenant A stores, tenant B reads → miss. Prove it: wire up the
        // per-key fakes and assert the tenant-B lookup does NOT return
        // tenant-A's payload.
        _db.StringGetAsync(keyA, Arg.Any<CommandFlags>())
            .Returns(new RedisValue("tenant-a-payload"));
        _db.StringGetAsync(keyB, Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var hitA = await _cache.TryGetAsync(keyA, "explain", "answer_evaluation", CancellationToken.None);
        var hitB = await _cache.TryGetAsync(keyB, "explain", "answer_evaluation", CancellationToken.None);

        Assert.True(hitA.found);
        Assert.Equal("tenant-a-payload", hitA.response);

        Assert.False(hitB.found);
        Assert.Equal(string.Empty, hitB.response);
    }

    // ── Redis failure resilience ──────────────────────────────────────────

    [Fact]
    public async Task TryGetAsync_RedisThrows_TreatsAsMiss()
    {
        var key = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError");
        _db.StringGetAsync(key, Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var result = await _cache.TryGetAsync(key, "explain", "answer_evaluation", CancellationToken.None);

        Assert.False(result.found);
        Assert.Equal(string.Empty, result.response);
    }

    [Fact]
    public async Task SetAsync_RedisThrows_DoesNotBubble()
    {
        var key = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError");
        _db.StringSetAsync(key, Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        // Should not throw — the cache never blocks the LLM call.
        await _cache.SetAsync(key, "body", TimeSpan.FromMinutes(5), "explain", "answer_evaluation", CancellationToken.None);
    }

    // ── Key-builder contract ──────────────────────────────────────────────

    [Fact]
    public void KeyBuilder_ExplanationWithoutTenant_ShapeMatches()
    {
        Assert.Equal(
            "cena:explain:q-123:ProceduralError",
            PromptCacheKeyBuilder.ForExplanation("q-123", "ProceduralError"));
    }

    [Fact]
    public void KeyBuilder_ExplanationWithTenant_IncludesTenantSegmentBeforeDomain()
    {
        Assert.Equal(
            "cena:t:school-42:explain:q-123:ProceduralError",
            PromptCacheKeyBuilder.ForExplanation("q-123", "ProceduralError", "school-42"));
    }

    [Fact]
    public void KeyBuilder_SystemPrompt_HasNoTenantSegment()
    {
        Assert.Equal(
            "cena:sys:explain-math-v3",
            PromptCacheKeyBuilder.ForSystemPrompt("explain-math-v3"));
    }

    [Fact]
    public void KeyBuilder_StudentContext_WithTenant_ShapeMatches()
    {
        Assert.Equal(
            "cena:t:school-42:ctx:anon-9f3a:a7b3c1d",
            PromptCacheKeyBuilder.ForStudentContext("anon-9f3a", "a7b3c1d", "school-42"));
    }

    [Fact]
    public void KeyBuilder_RejectsEmptySegments()
    {
        Assert.Throws<ArgumentException>(() => PromptCacheKeyBuilder.ForExplanation("", "err"));
        Assert.Throws<ArgumentException>(() => PromptCacheKeyBuilder.ForExplanation("q1", ""));
        Assert.Throws<ArgumentException>(() => PromptCacheKeyBuilder.ForSystemPrompt(""));
        Assert.Throws<ArgumentException>(() => PromptCacheKeyBuilder.ForStudentContext("", "h"));
        Assert.Throws<ArgumentException>(() => PromptCacheKeyBuilder.ForStudentContext("a", ""));
    }

    [Fact]
    public void KeyBuilder_RejectsColonsInSegments()
    {
        Assert.Throws<ArgumentException>(() =>
            PromptCacheKeyBuilder.ForExplanation("q:1", "err"));
        Assert.Throws<ArgumentException>(() =>
            PromptCacheKeyBuilder.ForStudentContext("stu", "h", "school:42"));
    }

    // ── Allowlist attribute contract ──────────────────────────────────────

    [Fact]
    public void AllowsUncachedLlmAttribute_RequiresReason()
    {
        Assert.Throws<ArgumentException>(() => new AllowsUncachedLlmAttribute(""));
        Assert.Throws<ArgumentException>(() => new AllowsUncachedLlmAttribute("   "));
        // A non-empty reason must round-trip.
        var attr = new AllowsUncachedLlmAttribute("content unique per document page");
        Assert.Equal("content unique per document page", attr.Reason);
    }

    // ── Meter factory ────────────────────────────────────────────────────

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();
        public Meter Create(MeterOptions options)
        {
            var m = new Meter(options);
            _meters.Add(m);
            return m;
        }
        public void Dispose()
        {
            foreach (var m in _meters) m.Dispose();
            _meters.Clear();
        }
    }
}
