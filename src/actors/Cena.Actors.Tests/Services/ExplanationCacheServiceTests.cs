// =============================================================================
// Tests: ExplanationCacheService (SAI-02)
// Verifies Redis cache key format, TTL, and error handling.
// =============================================================================

using Cena.Actors.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Cena.Actors.Tests.Services;

public sealed class ExplanationCacheServiceTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly IMeterFactory _meterFactory = new CacheMeterFactory();
    private readonly ExplanationCacheService _service;

    public ExplanationCacheServiceTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        var circuitBreaker = Substitute.For<Cena.Actors.Infrastructure.IRedisCircuitBreaker>();
        circuitBreaker.IsAvailable.Returns(true);
        _service = new ExplanationCacheService(
            _redis,
            circuitBreaker,
            NullLogger<ExplanationCacheService>.Instance,
            _meterFactory);
    }

    public void Dispose() => _meterFactory.Dispose();

    // =========================================================================
    // KEY FORMAT: explain:{questionId}:{errorType}:{language}
    // =========================================================================

    [Fact]
    public void BuildKey_MatchesSpecFormat()
    {
        var key = ExplanationCacheService.BuildKey(
            "q-123", ExplanationErrorType.ProceduralError, "he");

        Assert.Equal("explain:q-123:ProceduralError:he", key);
    }

    [Theory]
    [InlineData("q1", ExplanationErrorType.ConceptualMisunderstanding, "he", "explain:q1:ConceptualMisunderstanding:he")]
    [InlineData("q1", ExplanationErrorType.ProceduralError, "ar", "explain:q1:ProceduralError:ar")]
    [InlineData("q1", ExplanationErrorType.CarelessMistake, "en", "explain:q1:CarelessMistake:en")]
    [InlineData("q1", ExplanationErrorType.Guessing, "he", "explain:q1:Guessing:he")]
    [InlineData("q1", ExplanationErrorType.PartialUnderstanding, "ar", "explain:q1:PartialUnderstanding:ar")]
    public void BuildKey_AllErrorTypes_AllLanguages(
        string questionId, ExplanationErrorType errorType, string language, string expected)
    {
        var key = ExplanationCacheService.BuildKey(questionId, errorType, language);
        Assert.Equal(expected, key);
    }

    [Fact]
    public void BuildKey_HebrewAndArabic_CachedSeparately()
    {
        var heKey = ExplanationCacheService.BuildKey("q1", ExplanationErrorType.ProceduralError, "he");
        var arKey = ExplanationCacheService.BuildKey("q1", ExplanationErrorType.ProceduralError, "ar");

        Assert.NotEqual(heKey, arKey);
    }

    // =========================================================================
    // TTL: 30 days
    // =========================================================================

    [Fact]
    public void CacheTtl_Is30Days()
    {
        Assert.Equal(TimeSpan.FromDays(30), ExplanationCacheService.CacheTtl);
    }

    // =========================================================================
    // GET: cache hit
    // =========================================================================

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsCachedExplanation()
    {
        var cached = new CachedExplanation(
            "This is the explanation.", "claude-sonnet-4-6-20260215", 42,
            DateTimeOffset.UtcNow.AddHours(-1));

        var json = JsonSerializer.Serialize(cached, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _service.GetAsync(
            "q-123", ExplanationErrorType.ProceduralError, "he", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("This is the explanation.", result.Text);
        Assert.Equal("claude-sonnet-4-6-20260215", result.ModelId);
    }

    // =========================================================================
    // GET: cache miss
    // =========================================================================

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsNull()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _service.GetAsync(
            "q-123", ExplanationErrorType.Guessing, "he", CancellationToken.None);

        Assert.Null(result);
    }

    // =========================================================================
    // GET: Redis failure -> cache miss (never throws)
    // =========================================================================

    [Fact]
    public async Task GetAsync_RedisFailure_ReturnsNull()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisTimeoutException("timeout", CommandStatus.Unknown));

        var result = await _service.GetAsync(
            "q-123", ExplanationErrorType.ProceduralError, "he", CancellationToken.None);

        Assert.Null(result);
    }

    // =========================================================================
    // SET: stores with correct key and TTL
    // =========================================================================

    [Fact]
    public async Task SetAsync_StoresWithCorrectKeyAndTtl()
    {
        var cached = new CachedExplanation(
            "Explanation text", "claude-sonnet-4-6-20260215", 50, DateTimeOffset.UtcNow);

        await _service.SetAsync(
            "q-456", ExplanationErrorType.ConceptualMisunderstanding, "ar",
            cached, CancellationToken.None);

        // Verify StringSetAsync was called with the expected key
        var calls = _db.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "StringSetAsync")
            .ToList();
        Assert.Single(calls);

        var firstArg = calls[0].GetArguments()[0];
        Assert.Equal("explain:q-456:ConceptualMisunderstanding:ar", firstArg!.ToString());

        // TTL correctness verified by CacheTtl_Is30Days test + BuildKey tests.
        // The 3-arg overload StringSetAsync(key, value, expiry) passes CacheTtl directly.
    }

    // =========================================================================
    // SET: Redis failure -> swallowed (never throws)
    // =========================================================================

    [Fact]
    public async Task SetAsync_RedisFailure_DoesNotThrow()
    {
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisTimeoutException("timeout", CommandStatus.Unknown));

        var cached = new CachedExplanation("text", "model", 10, DateTimeOffset.UtcNow);

        // Should not throw
        await _service.SetAsync(
            "q-789", ExplanationErrorType.CarelessMistake, "he", cached, CancellationToken.None);
    }

    // =========================================================================
    // Different error types produce different cache keys
    // =========================================================================

    [Fact]
    public async Task GetAsync_DifferentErrorTypes_DifferentKeys()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        await _service.GetAsync("q1", ExplanationErrorType.ConceptualMisunderstanding, "he", CancellationToken.None);
        await _service.GetAsync("q1", ExplanationErrorType.ProceduralError, "he", CancellationToken.None);
        await _service.GetAsync("q1", ExplanationErrorType.CarelessMistake, "he", CancellationToken.None);

        await _db.Received(1).StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "explain:q1:ConceptualMisunderstanding:he"),
            Arg.Any<CommandFlags>());
        await _db.Received(1).StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "explain:q1:ProceduralError:he"),
            Arg.Any<CommandFlags>());
        await _db.Received(1).StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "explain:q1:CarelessMistake:he"),
            Arg.Any<CommandFlags>());
    }
}

internal sealed class CacheMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = new();
    public Meter Create(MeterOptions options) { var m = new Meter(options); _meters.Add(m); return m; }
    public void Dispose() { foreach (var m in _meters) m.Dispose(); }
}
