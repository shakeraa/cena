// =============================================================================
// Cena Platform — IPromptCache (prr-047, ADR-0026 §6 prompt_caching)
//
// Purpose: single seam every LLM call site MUST go through before firing a
// request at an upstream provider. The seam gives us three guarantees:
//
//   1. Consistent key hygiene per contracts/llm/routing-config.yaml §6.
//      Keys are namespaced by cache_type (sys/explain/ctx) + task name +
//      optional tenantId so entries from tenant A can never be served to
//      tenant B (ADR-0001 tenant isolation).
//
//   2. Hit/miss observability at the seam, not the call site. Every
//      implementation emits `cena.prompt_cache.hits_total` +
//      `cena.prompt_cache.misses_total` with the same label set regardless
//      of which service called it. The Grafana dashboard + Prometheus
//      alert at deploy/observability/ rely on this contract.
//
//   3. Enforcement hook for the architecture test
//      (`PromptCacheUsedTest`) — every `ILlmClient.CompleteAsync` call
//      site must sit inside a `TryGetAsync → (miss) → LLM → SetAsync`
//      guard, or the class must carry the [AllowsUncachedLlm] attribute
//      with a written justification. See allowlist doc on the attribute.
//
// The interface is deliberately narrow: get, set, nothing else. Invalidation
// is provider-specific (the question bank invalidator for explain:*, the
// session finisher for ctx:*) so it does not belong here.
//
// See:
//   - contracts/llm/routing-config.yaml §6 prompt_caching
//   - docs/adr/0026-llm-three-tier-routing.md
//   - src/actors/Cena.Actors.Tests/Architecture/PromptCacheUsedTest.cs
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Mandatory seam for any LLM call that has a repeatable prompt. Implementations
/// are Redis-backed; failures degrade to a cache miss, never an exception, so
/// Redis outages do not take down the student-facing explain path.
/// </summary>
public interface IPromptCache
{
    /// <summary>
    /// Attempt to fetch a previously-cached response.
    /// </summary>
    /// <param name="cacheKey">Fully-qualified key; build via <see cref="PromptCacheKeyBuilder"/>.</param>
    /// <param name="cacheType">One of "sys" | "explain" | "ctx". Used as a metric label only.</param>
    /// <param name="taskName">Task routing name per routing-config.yaml §2 (e.g. "answer_evaluation").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>(true, response) on cache hit; (false, empty) on miss or Redis failure.</returns>
    Task<(bool found, string response)> TryGetAsync(
        string cacheKey,
        string cacheType,
        string taskName,
        CancellationToken ct);

    /// <summary>
    /// Store a freshly-generated response for future hits. TTL is caller-supplied
    /// because the strategy differs by cache_type per routing-config.yaml §6:
    ///   - sys:     1 hour  (system prompts change rarely)
    ///   - ctx:     5 min   (per-session, stable within one session)
    ///   - explain: 30 days (per-question misconception explanation)
    /// </summary>
    Task SetAsync(
        string cacheKey,
        string response,
        TimeSpan ttl,
        string cacheType,
        string taskName,
        CancellationToken ct);
}

/// <summary>
/// Opt-out marker for the <c>PromptCacheUsedTest</c> architecture scan. Apply
/// to a class (never a method) that intentionally calls <c>ILlmClient.CompleteAsync</c>
/// without a cache guard, and justify in <see cref="Reason"/>.
///
/// Legitimate reasons (non-exhaustive):
///   - One-shot streaming tutor turn where the prompt is unique per student.
///   - OCR / content ingestion pipeline where each document page is unique.
///
/// Do NOT use this attribute to work around the hit-rate SLO. If the prompt
/// actually repeats, add caching instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowsUncachedLlmAttribute : Attribute
{
    public string Reason { get; }

    public AllowsUncachedLlmAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "AllowsUncachedLlm requires a written justification so the cache " +
                "bypass is reviewable in PR. Empty reasons are banned.",
                nameof(reason));
        }
        Reason = reason;
    }
}
