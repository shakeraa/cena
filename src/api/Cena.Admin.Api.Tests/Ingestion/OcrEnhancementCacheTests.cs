// Cena Platform — OcrEnhancementCache unit tests (ADR-0062 Phase 1.5)
//
// Pin tests for the cache contract. NSubstitute fakes Marten's
// IDocumentStore + IDocumentSession; we capture every Store() call and
// model LoadAsync return values so the absolute-TTL semantics can be
// driven by a FakeTimeProvider without sleeping.
//
// What these tests pin
// --------------------
//   1. ComputeKey is sha256(input), lower-hex, deterministic across calls.
//   2. Cache hit returns the EnhancedText that was stored.
//   3. Cache miss after TTL: row exists in store but ExpiresAt <= now,
//      so TryGetAsync returns null (treated as miss; the row is left
//      for the janitor sweep — read-side does not delete).
//   4. StoreAsync writes Id = sha256(input), ExpiresAt = ComputedAt + ttl.
//   5. Empty input + empty key paths short-circuit cleanly.
//
// These pin the bug class "we shipped a sliding-TTL cache and the curator
// can never see fresh OCR" (the brief explicitly forbids sliding TTL).

using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Documents;
using Marten;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

// Local FakeTimeProvider — the Microsoft.Extensions.TimeProvider.Testing
// package is not referenced from this test project; the codebase
// convention (matched in PostReflectionMasteryServiceTests +
// MockExamRunServiceTests + MashovSyncCircuitBreakerTests) is a tiny
// per-test-class shim that overrides GetUtcNow() and exposes Advance().
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    public FakeTimeProvider(DateTimeOffset start) { _now = start; }
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) { _now = _now + delta; }
}

public sealed class OcrEnhancementCacheTests
{
    // Fixed start time so every test gets identical clock semantics.
    private static readonly DateTimeOffset Now = new(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

    private static (OcrEnhancementCache cache,
                    IDocumentStore store,
                    IDocumentSession session,
                    IQuerySession query,
                    FakeTimeProvider clock,
                    List<OcrEnhancementCacheDocument> stored)
        BuildCache(TimeSpan? ttl = null)
    {
        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var query = Substitute.For<IQuerySession>();
        store.LightweightSession().Returns(session);
        store.QuerySession().Returns(query);

        var stored = new List<OcrEnhancementCacheDocument>();
        session.WhenForAnyArgs(s => s.Store<OcrEnhancementCacheDocument>(default!))
            .Do(ci =>
            {
                foreach (var arg in ci.Args())
                {
                    if (arg is OcrEnhancementCacheDocument doc) stored.Add(doc);
                    else if (arg is OcrEnhancementCacheDocument[] docs) stored.AddRange(docs);
                }
            });

        var clock = new FakeTimeProvider(Now);
        var cache = new OcrEnhancementCache(store, clock, ttl);
        return (cache, store, session, query, clock, stored);
    }

    [Fact]
    public void ComputeKey_IsLowerHexSha256_AndDeterministic()
    {
        var (cache, _, _, _, _, _) = BuildCache();

        const string input = "Some OCR text — חשב את האינטגרל \\(x^2 + 1\\)";
        var key1 = cache.ComputeKey(input);
        var key2 = cache.ComputeKey(input);

        Assert.Equal(key1, key2);
        Assert.Equal(64, key1.Length);
        Assert.Matches("^[0-9a-f]{64}$", key1);
    }

    [Fact]
    public void ComputeKey_DifferentInputs_DifferentKeys()
    {
        var (cache, _, _, _, _, _) = BuildCache();

        var keyA = cache.ComputeKey("alpha");
        var keyB = cache.ComputeKey("beta");
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void ComputeKey_EmptyInput_ReturnsEmpty()
    {
        var (cache, _, _, _, _, _) = BuildCache();
        Assert.Equal(string.Empty, cache.ComputeKey(""));
    }

    [Fact]
    public async Task TryGetAsync_CacheHit_ReturnsStoredEnhancement()
    {
        var (cache, _, _, query, _, _) = BuildCache();

        var key = cache.ComputeKey("hello");
        query.LoadAsync<OcrEnhancementCacheDocument>(key, Arg.Any<CancellationToken>())
            .Returns(new OcrEnhancementCacheDocument
            {
                Id = key,
                EnhancedText = "ENHANCED",
                ModelUsed = "claude-sonnet-4-6",
                ComputedAt = Now,
                // ExpiresAt > now: still alive.
                ExpiresAt = Now.AddHours(12),
            });

        var hit = await cache.TryGetAsync(key);

        Assert.NotNull(hit);
        Assert.Equal("ENHANCED", hit!.EnhancedText);
        Assert.Equal("claude-sonnet-4-6", hit.ModelUsed);
        Assert.Equal(Now, hit.ComputedAt);
    }

    [Fact]
    public async Task TryGetAsync_AfterTtl_ReturnsNull()
    {
        // Row exists but expired — must be treated as a miss. This is the
        // "cache miss after TTL → fresh LLM call" path the brief calls
        // for. The expired row is intentionally NOT deleted by the read
        // (read-side mutation is a deadlock vector under contention);
        // the next StoreAsync overwrites it, or a janitor sweeps it.
        var (cache, _, _, query, clock, _) = BuildCache(ttl: TimeSpan.FromHours(24));

        var key = cache.ComputeKey("stale-input");
        var staleRow = new OcrEnhancementCacheDocument
        {
            Id = key,
            EnhancedText = "OLD",
            ModelUsed = "claude-sonnet-4-6",
            ComputedAt = Now,
            ExpiresAt = Now.AddHours(24),
        };
        query.LoadAsync<OcrEnhancementCacheDocument>(key, Arg.Any<CancellationToken>())
            .Returns(staleRow);

        // Advance past the TTL — row is now expired.
        clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        var hit = await cache.TryGetAsync(key);

        Assert.Null(hit);
    }

    [Fact]
    public async Task TryGetAsync_MissingRow_ReturnsNull()
    {
        var (cache, _, _, query, _, _) = BuildCache();
        query.LoadAsync<OcrEnhancementCacheDocument>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((OcrEnhancementCacheDocument?)null);

        var hit = await cache.TryGetAsync(cache.ComputeKey("never-stored"));
        Assert.Null(hit);
    }

    [Fact]
    public async Task TryGetAsync_EmptyKey_ReturnsNullWithoutTouchingStore()
    {
        var (cache, _, _, query, _, _) = BuildCache();

        var hit = await cache.TryGetAsync("");

        Assert.Null(hit);
        // String overload — disambiguate from the int overload that
        // Marten's IQuerySession also exposes.
        await query.DidNotReceive().LoadAsync<OcrEnhancementCacheDocument>(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WritesRowWithAbsoluteExpiry()
    {
        var ttl = TimeSpan.FromHours(24);
        var (cache, _, session, _, _, stored) = BuildCache(ttl);

        var key = cache.ComputeKey("input-A");
        await cache.StoreAsync(key, "ENHANCED-A", "claude-sonnet-4-6");

        Assert.Single(stored);
        var row = stored[0];
        Assert.Equal(key, row.Id);
        Assert.Equal("ENHANCED-A", row.EnhancedText);
        Assert.Equal("claude-sonnet-4-6", row.ModelUsed);
        Assert.Equal(Now, row.ComputedAt);
        // Absolute, not sliding — ExpiresAt = now + ttl, computed once
        // at write-time.
        Assert.Equal(Now + ttl, row.ExpiresAt);

        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_EmptyKey_DoesNothing()
    {
        var (cache, _, session, _, _, stored) = BuildCache();
        await cache.StoreAsync("", "ENHANCED", "claude-sonnet-4-6");

        Assert.Empty(stored);
        await session.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task StoreAsync_EmptyEnhancedText_DoesNothing()
    {
        // Caching an empty string would mask a real "Anthropic returned
        // an empty body" failure on the next call. Refuse the write.
        var (cache, _, session, _, _, stored) = BuildCache();
        var key = cache.ComputeKey("input-X");
        await cache.StoreAsync(key, "", "claude-sonnet-4-6");

        Assert.Empty(stored);
        await session.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task StoreAsync_ThenTryGet_RoundTrips()
    {
        // End-to-end: write a row, advance the clock under the TTL, read
        // it back. Wires Marten's LoadAsync to return whatever Store()
        // last captured for that id.
        var (cache, _, _, query, clock, stored) = BuildCache(ttl: TimeSpan.FromHours(24));
        const string input = "round-trip input";

        var key = cache.ComputeKey(input);
        await cache.StoreAsync(key, "ENHANCED", "claude-sonnet-4-6");
        Assert.Single(stored);

        // Wire the LoadAsync stub from the captured store.
        query.LoadAsync<OcrEnhancementCacheDocument>(key, Arg.Any<CancellationToken>())
            .Returns(stored[0]);

        // Advance 23h — under TTL.
        clock.Advance(TimeSpan.FromHours(23));

        var hit = await cache.TryGetAsync(key);

        Assert.NotNull(hit);
        Assert.Equal("ENHANCED", hit!.EnhancedText);
    }
}
