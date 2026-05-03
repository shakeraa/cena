// =============================================================================
// Cena Platform — Variant Single-Flight Lock interface (PRR-260, ADR-0059 §15.5)
//
// Cohort write-lock around variant generation: when N students in the same
// classroom hit "generate similar" on the same Bagrut question, persona-finops
// flagged that without coordination we'd issue N parallel Tier-3 LLM calls
// (the #1 cost lever — 30× variance per cohort). This lock collapses all
// concurrent calls keyed on the same dedup key into 1 writer + (N-1) readers.
//
// Semantics ── informally:
//
//   • The first caller to <see cref="IVariantSingleFlightLock.ExecuteAsync"/>
//     for a given key acquires the writer slot, runs the supplied writer
//     delegate, and persists the result to a Redis result-cache key.
//   • Subsequent callers (readers) wait — polling the result-cache key —
//     until either (a) the writer publishes a result, or (b) the writer's
//     lock TTL elapses (writer crashed / hung). On (a) they return the
//     cached result tagged Reader; on (b) they take over as the new writer.
//   • A reader that observes neither a result nor a lock for the maximum
//     reader-wait budget returns Timeout.
//
// The lock is keyed on the variant dedup key from §15.5
// (sourceShailonCode, questionIndex, variationKind, track, stream,
// localeHint, parametricSeed?, payloadHashSafetyV1). Caller is
// responsible for canonicalizing the key — this primitive treats it as
// an opaque string.
//
// IMPORTANT ── result-cache freshness semantics:
//
//   The result-cache is keyed on the SAME dedup key as the lock; result
//   TTL outlives the lock TTL. A reader arriving long after a successful
//   write hits the cache directly without contending for the lock. This
//   is the production-cost win — repeat requests within the result TTL
//   are free.
//
// NOT a serializable / atomic write surface ── this is a single-flight
// optimisation, not a distributed consensus lock. Two writers DO converge
// in failure modes (e.g. lock TTL elapses while writer is still computing)
// — that's by design. Callers must not assume "exactly one write."
// =============================================================================

namespace Cena.Actors.Persistence;

/// <summary>
/// Cohort single-flight lock around variant generation. Implementations
/// must be safe for concurrent use across processes (the lock is the
/// primitive that makes that possible).
/// </summary>
public interface IVariantSingleFlightLock
{
    /// <summary>
    /// Run <paramref name="writer"/> as the single-flight writer for
    /// <paramref name="dedupKey"/>, OR return a previously cached result
    /// if one exists, OR wait for an in-flight writer to finish.
    /// </summary>
    /// <typeparam name="T">
    /// Result type. Must be JSON-serializable via System.Text.Json with
    /// the implementation's serializer options. Records and POCOs are
    /// the expected shape.
    /// </typeparam>
    /// <param name="dedupKey">
    /// Variant dedup key per ADR-0059 §15.5. Caller-canonicalized; this
    /// primitive treats it as opaque. Required, non-empty.
    /// </param>
    /// <param name="writer">
    /// The work to do as the single-flight writer. Called exactly once
    /// per (acquired-lock, lock-TTL) — the implementation guarantees one
    /// writer at a time per <paramref name="dedupKey"/> while the lock
    /// is held. Returning <c>default</c> is allowed but discouraged
    /// (the cache will hold the default value).
    /// </param>
    /// <param name="options">
    /// Optional override of TTLs and reader-wait budget. <see cref="VariantSingleFlightOptions.Default"/>
    /// when omitted.
    /// </param>
    /// <param name="ct">Cooperative cancellation.</param>
    /// <returns>
    /// <see cref="VariantSingleFlightOutcome{T}"/> tagged with the role
    /// the caller played (Writer / Reader / Timeout / Error).
    /// </returns>
    Task<VariantSingleFlightOutcome<T>> ExecuteAsync<T>(
        string dedupKey,
        Func<CancellationToken, Task<T>> writer,
        VariantSingleFlightOptions? options = null,
        CancellationToken ct = default)
        where T : class;
}

/// <summary>
/// Tunables for <see cref="IVariantSingleFlightLock.ExecuteAsync{T}"/>.
/// Defaults chosen for cohort variant generation — Tier-3 LLM calls
/// take 2-15s end-to-end; the 60s lock TTL is the §15.5 R11 budget.
/// </summary>
public sealed record VariantSingleFlightOptions(
    /// <summary>How long the writer holds the exclusive lock. After
    /// this elapses (writer crashed / hung), the next reader becomes
    /// the new writer. Default: 60s (ADR-0059 §15.5 R11).</summary>
    TimeSpan LockTtl,
    /// <summary>How long the cached result lives. Repeat requests
    /// within this window are free. Default: 5 min — long enough to
    /// absorb a "just after class started, 30 phones load the same
    /// question" burst, short enough that stale-but-still-cached
    /// content doesn't outlive a same-day question revision.</summary>
    TimeSpan ResultTtl,
    /// <summary>Maximum time a reader will block waiting for the writer
    /// to publish a result. Beyond this, the reader returns Timeout
    /// (the caller is then free to fail the request, retry, or fall
    /// through to a degraded path). Default: 30s — half the LockTtl
    /// so a reader doesn't end up holding the user's request open
    /// across a full writer crash + takeover cycle.</summary>
    TimeSpan ReaderWaitBudget,
    /// <summary>How often a reader polls for the result. Default:
    /// 250ms — fast enough that a 2s writer doesn't measurably tax
    /// the user's perceived latency, slow enough that 30 readers
    /// don't hammer Redis for their own writer.</summary>
    TimeSpan ReaderPollInterval)
{
    /// <summary>
    /// Production defaults: 60s lock, 5min result cache, 30s reader
    /// budget, 250ms poll. Tuned for cohort-of-30 variant generation
    /// where writer latency is 2-15s (Tier-3 LLM) and the cache hit
    /// rate matters more than tail latency on the writer path.
    /// </summary>
    public static readonly VariantSingleFlightOptions Default = new(
        LockTtl: TimeSpan.FromSeconds(60),
        ResultTtl: TimeSpan.FromMinutes(5),
        ReaderWaitBudget: TimeSpan.FromSeconds(30),
        ReaderPollInterval: TimeSpan.FromMilliseconds(250));
}

/// <summary>
/// Role the caller played in a completed single-flight execution.
/// Tags the telemetry counter so dashboards can compute (readers /
/// writers) — the cohort-collapse ratio that justifies the lock.
/// </summary>
public enum VariantSingleFlightRole
{
    /// <summary>Caller acquired the lock and ran the writer delegate.</summary>
    Writer,
    /// <summary>Caller observed a cached result (fresh write or warm cache).</summary>
    Reader,
    /// <summary>Caller waited the full <see cref="VariantSingleFlightOptions.ReaderWaitBudget"/>
    /// without seeing a result and without acquiring the lock — the writer
    /// stalled or the lock TTL is misconfigured. Caller decides what to do.</summary>
    Timeout,
    /// <summary>The writer threw or the Redis backend errored. Caller
    /// must inspect <see cref="VariantSingleFlightOutcome{T}.Error"/>.</summary>
    Error,
}

/// <summary>
/// Result of <see cref="IVariantSingleFlightLock.ExecuteAsync{T}"/>.
/// Exactly one of <see cref="Result"/> or <see cref="Error"/> is non-null
/// when <see cref="Role"/> is Writer or Reader; both are null on Timeout.
/// </summary>
/// <typeparam name="T">The result payload type.</typeparam>
public sealed record VariantSingleFlightOutcome<T>(
    T? Result,
    VariantSingleFlightRole Role,
    string? Error)
    where T : class
{
    /// <summary>Convenience constructor for a successful Writer outcome.</summary>
    public static VariantSingleFlightOutcome<T> AsWriter(T result) =>
        new(result, VariantSingleFlightRole.Writer, null);

    /// <summary>Convenience constructor for a successful Reader outcome.</summary>
    public static VariantSingleFlightOutcome<T> AsReader(T result) =>
        new(result, VariantSingleFlightRole.Reader, null);

    /// <summary>Convenience constructor for a Timeout outcome.</summary>
    public static VariantSingleFlightOutcome<T> AsTimeout() =>
        new(null, VariantSingleFlightRole.Timeout, null);

    /// <summary>Convenience constructor for an Error outcome.</summary>
    public static VariantSingleFlightOutcome<T> AsError(string message) =>
        new(null, VariantSingleFlightRole.Error, message);
}
