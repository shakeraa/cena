// =============================================================================
// Cena Platform — Variant Rate Limiter (PRR-265, ADR-0059 §15.5)
//
// Redis-backed sliding-window rate limiter for variant generation. Models the
// counter primitive only; tier+entitlement decisions live in the composed
// IVariantGenerationGate (Cena.Actors). Multi-scope: per-(student,day),
// per-(institute,day), per-(institute,source,day), per-(student,source,30d).
//
// Why a dedicated primitive (not generic ASP.NET RateLimiter):
//   - Per-source dimension is dynamic (based on request body's
//     `source_paper_code`); ASP.NET's IRateLimiterPolicy<TPartition> can't
//     express "lookup-derived partition" without re-running the auth pipeline.
//   - The 30-day per-(student,source) window enforces cogsci spacing
//     (ADR-0059 §14.3.1) — too long for a typical fixed-window limiter.
//   - We need to surface deny reasons cleanly to 429 Retry-After + 403
//     `tier_does_not_permit_variants` paths, which is awkward inside the
//     ASP.NET RateLimiter rejection callback.
//
// What this primitive does NOT do (composed elsewhere):
//   - Tier check / payment-method gate / institute-SSO gate — see
//     IVariantGenerationGate. This primitive is a pure counter.
//   - Cohort single-flight (R11) — separate primitive (not PRR-265).
//
// Honest data shape: each window is an independent sliding-window counter.
// Denial of any window denies the request. The caller decides which windows
// to consult (e.g. a free-tier check might skip the per-institute window if
// the institute has no contract; a paid-tier check consults all four).
//
// Failure mode: Redis is the source of truth. If Redis is unreachable, the
// limiter MUST fail-closed by default (deny) to honor the cost-protection
// invariant — variant gen is expensive (Tier-3 LLM + Haiku second-pass +
// SymPy verify, ~$0.045/call per ADR-0059 §14.2). Tests can opt for
// fail-open via NullVariantRateLimiter.
// =============================================================================

namespace Cena.Infrastructure.RateLimiting;

/// <summary>
/// Variant generation kind, used to bin counters by tier (parametric vs
/// structural have different per-source caps per ADR-0059 §15.5).
/// </summary>
public enum VariantKind
{
    /// <summary>
    /// Structural variant — Tier-3 LLM-authored, surface-form-different,
    /// solution-method-equivalent. Default for free-tier-with-payment.
    /// </summary>
    Structural = 0,

    /// <summary>
    /// Parametric variant — deterministic parameter substitution + SymPy
    /// verify. Paid-tier only per ADR-0059 §15.5.
    /// </summary>
    Parametric = 1,
}

/// <summary>
/// One named counter window. The same student/institute/source identifier
/// can be checked against multiple windows of different durations.
/// </summary>
/// <param name="ScopeName">
/// Stable, low-cardinality identifier for the dashboard label
/// (e.g. <c>"student-day"</c>, <c>"institute-source-day"</c>). Embedded
/// into the Redis key namespace so windows do not collide.
/// </param>
/// <param name="PartitionKey">
/// Cardinality-bearing identifier (a single id like <c>"student-abc123"</c>
/// or a composite like <c>"institute-X|source-Y"</c>). The limiter does
/// NOT canonicalize this — callers MUST stable-format composites.
/// </param>
/// <param name="Window">
/// Duration of the sliding window. The limiter enforces a Redis TTL of
/// <c>Window * 1.5</c> on the underlying sorted set so abandoned partitions
/// reclaim space.
/// </param>
/// <param name="Limit">
/// Maximum count permitted within the window. Decision is "fewer than
/// limit ⇒ allow" — so a limit of 0 denies everything.
/// </param>
public sealed record VariantRateLimitScope(
    string ScopeName,
    string PartitionKey,
    TimeSpan Window,
    int Limit);

/// <summary>
/// Outcome of a CheckAsync call across one or more windows.
/// </summary>
/// <param name="Allowed">
/// True iff every requested window was below its limit. False iff at least
/// one window denied.
/// </param>
/// <param name="DeniedScopeName">
/// When <see cref="Allowed"/> is false, the <see cref="VariantRateLimitScope.ScopeName"/>
/// of the first denying window (deterministic ordering preserved). Used for
/// metric labels and the 429 deny-reason body.
/// </param>
/// <param name="CurrentCount">
/// When <see cref="Allowed"/> is false, the live count in the denying
/// window AFTER the denial (i.e. the count that would have been permitted
/// had no commit happened). Surface to callers for observability only —
/// never to students (could leak per-cohort heuristics).
/// </param>
/// <param name="Limit">
/// When <see cref="Allowed"/> is false, the configured cap on the
/// denying window. Surface to admins for diagnostics.
/// </param>
/// <param name="RetryAfter">
/// When <see cref="Allowed"/> is false, the time until the oldest
/// counted entry in the denying window expires (i.e. the soonest the
/// caller could reasonably retry). Suitable as the
/// <c>Retry-After</c> header value (rounded up to whole seconds).
/// </param>
public sealed record VariantRateLimitDecision(
    bool Allowed,
    string? DeniedScopeName,
    int CurrentCount,
    int Limit,
    TimeSpan? RetryAfter)
{
    /// <summary>Convenience factory for an unconditional allow.</summary>
    public static VariantRateLimitDecision Allow() =>
        new(true, null, 0, 0, null);
}

/// <summary>
/// Pure-counter primitive. Composed callers (gate / endpoint policies) layer
/// tier and entitlement checks on top of this.
/// </summary>
public interface IVariantRateLimiter
{
    /// <summary>
    /// Test all <paramref name="scopes"/>. The decision is the AND of every
    /// scope (any denial short-circuits the result), preserving the order
    /// of <paramref name="scopes"/> for deterministic <c>DeniedScopeName</c>
    /// reporting.
    ///
    /// This call is non-mutating — counts are not incremented. Use
    /// <see cref="CommitAsync"/> after the variant generation actually
    /// happens (so failed generations don't burn the cap).
    /// </summary>
    Task<VariantRateLimitDecision> CheckAsync(
        IReadOnlyList<VariantRateLimitScope> scopes,
        DateTimeOffset asOfUtc,
        CancellationToken ct);

    /// <summary>
    /// Increment every scope's counter by 1. Idempotent within a single
    /// generation attempt (the same <paramref name="commitId"/> deduplicates).
    /// Call AFTER a generation completes successfully.
    /// </summary>
    /// <param name="commitId">
    /// Caller-supplied dedup key. Pass the variant generation's
    /// quality-gated output id (or another stable per-generation token).
    /// </param>
    Task CommitAsync(
        IReadOnlyList<VariantRateLimitScope> scopes,
        string commitId,
        DateTimeOffset asOfUtc,
        CancellationToken ct);
}
