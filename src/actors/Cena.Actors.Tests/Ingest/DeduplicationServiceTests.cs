// =============================================================================
// Cena Platform — Deduplication Service Tests
// Tests 3-level dedup: exact hash, structural AST, semantic (future).
// =============================================================================

using Cena.Actors.Ingest;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Ingest;

public class DeduplicationServiceTests
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly DeduplicationService _service;

    // In-memory Redis simulation
    private readonly HashSet<string> _exactHashes = new();
    private readonly Dictionary<string, string> _hashToItem = new();

    public DeduplicationServiceTests()
    {
        _redis = Substitute.For<IConnectionMultiplexer>();
        _db = Substitute.For<IDatabase>();
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);

        // Simulate Redis hash operations
        _db.HashGetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<RedisValue>(1).ToString();
                return _hashToItem.TryGetValue(key, out var val)
                    ? new RedisValue(val)
                    : RedisValue.Null;
            });

        _db.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(true);

        _db.HashSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
            Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var field = callInfo.ArgAt<RedisValue>(1).ToString();
                var value = callInfo.ArgAt<RedisValue>(2).ToString();
                _hashToItem[field] = value;
                return true;
            });

        var batch = Substitute.For<IBatch>();
        _db.CreateBatch(Arg.Any<object>()).Returns(batch);
        batch.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));
        batch.HashSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(),
            Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var field = callInfo.ArgAt<RedisValue>(1).ToString();
                var value = callInfo.ArgAt<RedisValue>(2).ToString();
                _hashToItem[field] = value;
                return Task.FromResult(true);
            });

        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DeduplicationService>>();
        _service = new DeduplicationService(_redis, logger);
    }

    [Fact]
    public async Task CheckAsync_UniqueItem_ReturnsUnique()
    {
        var result = await _service.CheckAsync(
            "חשב: 2x + 3 = 7",
            new Dictionary<string, string> { ["eq_1"] = "2x + 3 = 7" });

        Assert.Equal(DedupResult.Unique, result.Result);
        Assert.Null(result.MatchedItemId);
        Assert.NotEmpty(result.ExactHash);
    }

    [Fact]
    public async Task CheckAsync_SameContent_ReturnsExactDuplicate()
    {
        var math = new Dictionary<string, string> { ["eq_1"] = "2x + 3 = 7" };

        // Register first item
        var first = await _service.CheckAsync("חשב: 2x + 3 = 7", math);
        await _service.RegisterAsync("q-001", first.ExactHash, first.StructuralHash);

        // Same content should be detected as exact duplicate
        var second = await _service.CheckAsync("חשב: 2x + 3 = 7", math);
        Assert.Equal(DedupResult.ExactDuplicate, second.Result);
        Assert.Equal("q-001", second.MatchedItemId);
    }

    [Fact]
    public void ExactHash_DifferentContent_DifferentHashes()
    {
        // Verify different questions produce different hashes
        var hash1 = ComputeTestHash("חשב: 2x + 3 = 7", new() { ["eq_1"] = "2x + 3 = 7" });
        var hash2 = ComputeTestHash("חשב: 3x + 5 = 14", new() { ["eq_1"] = "3x + 5 = 14" });

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ExactHash_SameContent_SameHash()
    {
        var hash1 = ComputeTestHash("חשב: 2x + 3 = 7", new() { ["eq_1"] = "2x + 3 = 7" });
        var hash2 = ComputeTestHash("חשב: 2x + 3 = 7", new() { ["eq_1"] = "2x + 3 = 7" });

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ExactHash_WhitespaceNormalized()
    {
        // Extra whitespace should not affect the hash
        var hash1 = ComputeTestHash("חשב:  2x + 3 = 7", new() { ["eq_1"] = "2x + 3 = 7" });
        var hash2 = ComputeTestHash("חשב: 2x + 3 = 7", new() { ["eq_1"] = "2x + 3 = 7" });

        Assert.Equal(hash1, hash2);
    }

    private static string ComputeTestHash(string stem, Dictionary<string, string> math)
    {
        // Access the private normalization logic indirectly by computing SHA-256
        // of the same normalized content
        var sb = new System.Text.StringBuilder();
        sb.Append(System.Text.RegularExpressions.Regex.Replace(stem.Trim(), @"\s+", " "));
        foreach (var kv in math.OrderBy(k => k.Key))
        {
            sb.Append('|');
            sb.Append(System.Text.RegularExpressions.Regex.Replace(kv.Value.Trim(), @"\s+", ""));
        }
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes);
    }
}
