// =============================================================================
// Cena Platform — Misconception PII Store Registry (prr-015, ADR-0003)
//
// Single source of truth for every component that persists any form of
// misconception-adjacent PII. Every persistence seam (event store stream,
// Marten document projection, Redis key, in-memory cache, materialized
// projection document, etc.) that stores a misconception tag, a buggy-rule
// id attached to a student, or a free-text student answer used in
// misconception detection MUST register itself here.
//
// Why a central registry?
//
//   ADR-0003 locks misconception state to the session aggregate with a 30-day
//   default retention and a 90-day hard legal cap (COPPA data minimization).
//   The ban is only enforceable if we can enumerate every store that holds a
//   copy. The existing RetentionWorker handles the canonical Marten event
//   stream; the registry covers secondary stores (projection documents,
//   caches, replicas) so a new feature cannot silently add a misconception
//   cache without anyone noticing until audit time.
//
// Pre-flight invariants every registered store must declare:
//
//   store_name             — stable, human-readable identifier (used in
//                            metrics labels and audit logs)
//   retention_days         — ≤ 90 (ADR-0003 hard cap). Worker will clamp.
//   purge_strategy         — Delete | Anonymize | HashRedact
//   session_scope_verified — has the author proven this store holds
//                            session-scoped data only, not student-profile
//                            data? (manual gate, fail-closed default)
//
// See RegisteredMisconceptionStoreAttribute + MisconceptionRetentionWorker.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// How a registered misconception PII store purges expired records.
/// Choice is a property of the store's shape, not a policy decision — an
/// append-only event stream cannot "delete" a row so it must crypto-shred
/// or hash-redact instead.
/// </summary>
public enum MisconceptionPurgeStrategy
{
    /// <summary>
    /// Remove the row outright. Appropriate for projection documents, Redis
    /// keys, and other mutable stores where row-level DELETE is cheap and
    /// leaves no residue.
    /// </summary>
    Delete,

    /// <summary>
    /// Replace PII fields with a fixed anonymous value (e.g. "[redacted]")
    /// while keeping the row shape intact. Appropriate for analytics
    /// aggregates that retain per-bucket counts but must drop the PII tails.
    /// </summary>
    Anonymize,

    /// <summary>
    /// Rewrite PII fields in place to a SHA-256 hash of the original value,
    /// producing a deterministic identifier that is no longer reversible to
    /// the student. Appropriate for append-only stores where the row cannot
    /// be deleted (e.g. Marten event streams) and anonymization would break
    /// the aggregate join shape.
    /// </summary>
    HashRedact,
}

/// <summary>
/// Declarative metadata for a single misconception PII store. One row per
/// distinct persistence seam.
/// </summary>
/// <param name="StoreName">
/// Stable identifier used in metrics and audit logs. Must match the
/// <c>Name</c> argument on any <see cref="RegisteredMisconceptionStoreAttribute"/>
/// that marks a file with this same seam.
/// </param>
/// <param name="RetentionDays">
/// Declared retention window. Clamped to
/// <see cref="DataRetentionPolicy.SessionMisconceptionHardCap"/> (90 days)
/// by the worker — declaring a higher value does not raise the cap, it is
/// silently lowered. Declaring a lower value is honored (tenant may set
/// stricter retention).
/// </param>
/// <param name="PurgeStrategy">Purge semantics (see <see cref="MisconceptionPurgeStrategy"/>).</param>
/// <param name="SessionScopeVerified">
/// Author asserts this store only holds session-scoped data (ADR-0003
/// Decision 1). Registering a store without verification is a policy
/// violation — the worker warns and metrics expose the gap.
/// </param>
/// <param name="OwningModule">
/// Free-form tag for the owning module (e.g. <c>"Cena.Actors.Sessions"</c>).
/// Used in audit logs so a finding can be routed to the right team.
/// </param>
public sealed record RegisteredMisconceptionStore(
    string StoreName,
    int RetentionDays,
    MisconceptionPurgeStrategy PurgeStrategy,
    bool SessionScopeVerified,
    string OwningModule)
{
    /// <summary>
    /// Validates invariants: non-empty name, positive retention, module set.
    /// Throws on violation so registration fails at DI time.
    /// </summary>
    public void AssertValid()
    {
        if (string.IsNullOrWhiteSpace(StoreName))
            throw new ArgumentException(
                "RegisteredMisconceptionStore.StoreName must be non-empty.",
                nameof(StoreName));
        if (RetentionDays <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(RetentionDays), RetentionDays,
                "RetentionDays must be > 0 (ADR-0003 requires a positive window).");
        if (string.IsNullOrWhiteSpace(OwningModule))
            throw new ArgumentException(
                "RegisteredMisconceptionStore.OwningModule must be non-empty.",
                nameof(OwningModule));
    }

    /// <summary>
    /// Effective retention after clamping to the 90-day hard cap.
    /// </summary>
    public int EffectiveRetentionDays =>
        (int)Math.Min(
            RetentionDays,
            DataRetentionPolicy.SessionMisconceptionHardCap.TotalDays);
}

/// <summary>
/// Purge callback: given a retention cutoff, remove (or redact) any
/// records older than the cutoff and return the number of records
/// affected. The retention worker invokes this once per registered store
/// per nightly run.
/// </summary>
/// <remarks>
/// Implementations MUST be idempotent — the worker may retry on failure,
/// and a partial purge must not double-count on retry. Throwing from the
/// callback is allowed; the worker logs, increments a failure counter, and
/// moves on to the next store without aborting the whole run.
/// </remarks>
public delegate Task<int> MisconceptionPurgeCallback(
    DateTimeOffset cutoff,
    CancellationToken cancellationToken);

/// <summary>
/// Registry of every misconception PII persistence seam in the system.
/// Registrations are additive during startup; once frozen (first call to
/// <see cref="GetAll"/> from the retention worker), no more registrations
/// are accepted — this catches the "register from a non-startup path"
/// anti-pattern.
/// </summary>
public interface IMisconceptionPiiStoreRegistry
{
    /// <summary>
    /// Register a misconception PII store. Call from DI composition root
    /// (e.g. <c>Program.cs</c> or a module's <c>AddXxx</c> extension).
    /// Duplicate registrations by <see cref="RegisteredMisconceptionStore.StoreName"/>
    /// are rejected with an <see cref="InvalidOperationException"/>.
    /// </summary>
    void Register(RegisteredMisconceptionStore store, MisconceptionPurgeCallback purge);

    /// <summary>
    /// Enumerates all registered stores. Safe to call concurrently with
    /// purge callbacks. The first call freezes the registry — subsequent
    /// <see cref="Register"/> calls throw.
    /// </summary>
    IReadOnlyList<RegisteredMisconceptionStore> GetAll();

    /// <summary>
    /// Looks up a registered store by name. Returns null if unregistered.
    /// </summary>
    RegisteredMisconceptionStore? Get(string storeName);

    /// <summary>
    /// Resolves the purge callback for a named store. Throws if the store
    /// is not registered — callers should always have checked via
    /// <see cref="Get"/> first.
    /// </summary>
    MisconceptionPurgeCallback GetPurgeCallback(string storeName);
}

/// <summary>
/// Thread-safe in-memory registry. The expected deployment surface is
/// singleton-per-process — a new store registration at runtime is not
/// supported (see <see cref="IMisconceptionPiiStoreRegistry.GetAll"/>).
/// </summary>
public sealed class InMemoryMisconceptionPiiStoreRegistry
    : IMisconceptionPiiStoreRegistry
{
    private readonly ConcurrentDictionary<string, (RegisteredMisconceptionStore Store, MisconceptionPurgeCallback Purge)> _stores
        = new(StringComparer.Ordinal);

    private int _frozen;

    public void Register(RegisteredMisconceptionStore store, MisconceptionPurgeCallback purge)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(purge);
        store.AssertValid();

        if (Volatile.Read(ref _frozen) != 0)
            throw new InvalidOperationException(
                $"Cannot register misconception PII store '{store.StoreName}' after the " +
                "registry has been frozen. Register all stores during application startup " +
                "(Program.cs or AddXxx DI extensions), before the RetentionWorker starts.");

        if (!_stores.TryAdd(store.StoreName, (store, purge)))
            throw new InvalidOperationException(
                $"Misconception PII store '{store.StoreName}' is already registered. " +
                "Each store must have a unique StoreName — duplicate registration is an error.");
    }

    public IReadOnlyList<RegisteredMisconceptionStore> GetAll()
    {
        // Freeze on first enumeration. CAS to avoid racing a late Register.
        Interlocked.CompareExchange(ref _frozen, 1, 0);
        return _stores.Values
            .Select(v => v.Store)
            .OrderBy(s => s.StoreName, StringComparer.Ordinal)
            .ToList();
    }

    public RegisteredMisconceptionStore? Get(string storeName)
    {
        ArgumentNullException.ThrowIfNull(storeName);
        return _stores.TryGetValue(storeName, out var v) ? v.Store : null;
    }

    public MisconceptionPurgeCallback GetPurgeCallback(string storeName)
    {
        ArgumentNullException.ThrowIfNull(storeName);
        if (!_stores.TryGetValue(storeName, out var v))
            throw new KeyNotFoundException(
                $"Misconception PII store '{storeName}' is not registered.");
        return v.Purge;
    }
}
