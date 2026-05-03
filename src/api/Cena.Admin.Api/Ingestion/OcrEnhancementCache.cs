// Cena Platform — OCR enhancement cache (ADR-0062 Phase 1.5 cache layer)
//
// Why this exists, in three sentences
// -----------------------------------
// `OcrTextEnhancer` is deterministic at temperature=0. Identical input
// must not pay for the LLM twice. This cache reads/writes the cleaned
// text by `sha256(input)` so a refresh, a sibling curator, or any other
// caller-with-the-same-bytes returns instantly without an Anthropic call.
//
// Choice of backing store: Marten document (option B in the brief)
// ----------------------------------------------------------------
// The brief offered three: in-memory, Marten document, Redis. The
// senior-architect cut against IMemoryCache:
//
//   1. Curator dev loop hits restarts. With IMemoryCache every restart
//      throws the cache away — the dev cost of "I just enhanced this and
//      now I have to wait again" is paid by the engineer, not just the
//      finops dashboard.
//   2. Ops inspectability. `psql -c 'select id, computed_at, expires_at
//      from mt_doc_ocrenhancementcachedocument'` is a real-during-an-
//      incident verb that IMemoryCache can't offer.
//   3. The volume is tiny. Curator hits ≤100 enhances/day. A Marten
//      LoadAsync round-trip is ~5ms. The cost of "Marten is heavier than
//      a hash table" is invisible at this volume.
//
// Redis was rejected outright — the project doesn't run Redis for this
// use case and adding it would cost more than it saves at the current
// volume.
//
// Concurrency model
// -----------------
// Two curators clicking Enhance simultaneously on the same input race
// to compute the same value (deterministic at temperature=0). Both will
// write the same row; Marten's `Upsert` semantics make last-write-wins
// safe. There is intentionally no advisory-lock dance here — the cost
// of the duplicate Anthropic call (~$0.005) is cheaper than the
// orchestration cost of preventing it.
//
// TTL — absolute, not sliding
// ---------------------------
// `ExpiresAt = ComputedAt + 24h`. Sliding TTL would lock the cache state
// in a way that creates "why is my fresh OCR not picked up" debugging.
// 24h was chosen because the failure mode of a stale cache is
// "Anthropic returned a worse cleanup the first time"; that's a quality
// problem worth re-running for after a day. Tunable via constructor.
//
// Cascade-delete behavior
// -----------------------
// The cache is keyed by sha256(input), NOT by draft id, because items
// with genuinely identical OCR text legitimately share a hit. That
// also means deleting a draft does NOT delete its cache row — a sibling
// draft with identical input may still need it. This is correct. The
// 24h TTL handles abandoned rows; for explicit test cleanup, drop both
// `mt_doc_pipelineitemdocument` AND `mt_doc_ocrenhancementcachedocument`.

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Admin.Api.Ingestion;

public interface IOcrEnhancementCache
{
    /// <summary>
    /// Compute the canonical cache key for an input string. Stable across
    /// processes and platforms (lower-hex sha256 of the UTF-8 bytes).
    /// Exposed for callers that want to log or persist the hash alongside
    /// non-cache data (e.g. a draft's <c>EnhancedInputHash</c>).
    /// </summary>
    string ComputeKey(string input);

    /// <summary>
    /// Look up a cached enhancement. Returns null on miss OR on expired-
    /// row hit (treated as a miss; the row stays put for the janitor
    /// sweep, the caller will overwrite on its successful LLM call).
    /// </summary>
    Task<EnhancedOcrCacheEntry?> TryGetAsync(string inputKey, CancellationToken ct = default);

    /// <summary>
    /// Persist a successful LLM enhancement. Last-write-wins semantics
    /// (see file header for why that is correct).
    /// </summary>
    Task StoreAsync(
        string inputKey,
        string enhancedText,
        string modelUsed,
        CancellationToken ct = default);
}

public sealed record EnhancedOcrCacheEntry(
    string EnhancedText,
    string ModelUsed,
    DateTimeOffset ComputedAt,
    DateTimeOffset ExpiresAt);

public sealed class OcrEnhancementCache : IOcrEnhancementCache
{
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Default TTL — 24h. The brief locked this; surfaces as a
    /// constructor parameter ONLY so unit tests can verify the
    /// "cache miss after TTL" path without sleeping for a day.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public OcrEnhancementCache(
        IDocumentStore store,
        TimeProvider? clock = null,
        TimeSpan? ttl = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
        _ttl = ttl ?? DefaultTtl;
    }

    public string ComputeKey(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        // Lower-hex; allocation-light. ToHexString() returns upper-case,
        // we lower for stable Marten document ids across processes.
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<EnhancedOcrCacheEntry?> TryGetAsync(
        string inputKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(inputKey))
            return null;

        await using var session = _store.QuerySession();
        var row = await session.LoadAsync<OcrEnhancementCacheDocument>(inputKey, ct)
            .ConfigureAwait(false);
        if (row is null)
            return null;

        var now = _clock.GetUtcNow();
        if (row.ExpiresAt <= now)
        {
            // Expired. Treat as miss; do NOT delete here — that would
            // turn a read into a write under contention. Janitor sweep
            // (or the next StoreAsync that overwrites the row) handles
            // the cleanup. The caller will overwrite this row's id on
            // its successful LLM call so stale data never lingers under
            // the same key.
            return null;
        }

        return new EnhancedOcrCacheEntry(
            EnhancedText: row.EnhancedText,
            ModelUsed:    row.ModelUsed,
            ComputedAt:   row.ComputedAt,
            ExpiresAt:    row.ExpiresAt);
    }

    public async Task StoreAsync(
        string inputKey,
        string enhancedText,
        string modelUsed,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(inputKey))
            return;
        if (string.IsNullOrEmpty(enhancedText))
            return;

        var now = _clock.GetUtcNow();
        var doc = new OcrEnhancementCacheDocument
        {
            Id           = inputKey,
            EnhancedText = enhancedText,
            ModelUsed    = modelUsed ?? "",
            ComputedAt   = now,
            ExpiresAt    = now + _ttl,
        };

        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
