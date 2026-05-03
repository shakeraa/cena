// =============================================================================
// Cena Platform — DI wiring: canonical Marten misconception stream (prr-015)
//
// This file is the runtime bridge between IMisconceptionPiiStoreRegistry and
// the Marten event store that holds the three ADR-0003 event types
// (MisconceptionDetected_V1 / MisconceptionRemediated_V1 /
// SessionMisconceptionsScrubbed_V1).
//
// Why a dedicated file?
//
//   1. Keeps MartenConfiguration.cs under its 500-LOC grandfather baseline.
//   2. Gives the architecture test a single, unmistakable class to scan for
//      the [RegisteredMisconceptionStore] attribute — the canonical Marten
//      store is the #1 surface the registry must cover, and annotating
//      MartenConfiguration.cs (a 1000+ LOC legacy file) would be visually
//      lost.
//   3. Makes the purge callback explicit: the existing retention worker
//      (RetentionWorker.PurgeSessionMisconceptionsAsync) already handles the
//      Marten event stream via ADR-0038 crypto-shredding; this registration
//      surfaces that seam in the central registry for parity, with a no-op
//      callback (the older worker owns the actual purge).
//
// Store contract:
//
//   store_name             = "marten-session-misconception-stream"
//   retention_days         = 30     (ADR-0003 Decision 2 default)
//   purge_strategy         = HashRedact — append-only event stream cannot
//                            row-delete, so the established ADR-0038 path
//                            (crypto-shred the subject key) is the nearest
//                            semantic match: once the key tombstones, the
//                            encrypted StudentAnswer is unrecoverable, which
//                            is effectively a hash redaction at rest.
//   session_scope_verified = true   (ADR-0003 Decision 1 verified by
//                            NoAtRiskPersistenceTest + MlExclusionEnforcementTests)
//
// See docs/adr/0003-misconception-session-scope.md.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Configuration;

/// <summary>
/// DI registration shim — the class itself is the documentation seam for
/// <c>NoUnregisteredMisconceptionStoreTest</c>. Do not delete without
/// rewriting the architecture test.
/// </summary>
[RegisteredMisconceptionStore(
    "marten-session-misconception-stream",
    "ADR-0003 Decision 1: canonical session-scoped misconception event store.")]
public static class MisconceptionStoreRegistrations
{
    /// <summary>
    /// Stable store name that matches the class attribute above and the
    /// runtime registry row. Tests and ops queries both use this literal.
    /// </summary>
    public const string MartenStreamStoreName = "marten-session-misconception-stream";

    /// <summary>
    /// Register the canonical Marten-backed misconception stream with the
    /// central registry. The purge callback is a no-op: the existing
    /// <c>RetentionWorker.PurgeSessionMisconceptionsAsync</c> already owns
    /// retention for this store via ADR-0038 crypto-shredding. The
    /// registration surfaces the store in the central registry so the
    /// <c>/metrics</c> dashboard shows a registered row and the
    /// <c>NoUnregisteredMisconceptionStoreTest</c> allowlist stays small.
    /// </summary>
    /// <remarks>
    /// If a future refactor consolidates the Marten purge into the new
    /// <c>MisconceptionRetentionWorker</c>, replace the no-op callback here
    /// with a direct delegation to <c>RetentionWorker</c> — do not
    /// duplicate the crypto-shred logic.
    /// </remarks>
    public static IServiceCollection AddCanonicalMartenMisconceptionStore(
        this IServiceCollection services)
    {
        var store = new RegisteredMisconceptionStore(
            StoreName: MartenStreamStoreName,
            RetentionDays: (int)DataRetentionPolicy.SessionMisconceptionRetention.TotalDays,
            PurgeStrategy: MisconceptionPurgeStrategy.HashRedact,
            SessionScopeVerified: true,
            OwningModule: "Cena.Actors.Configuration");

        return services.RegisterMisconceptionPiiStore(
            store,
            // Purge is owned by RetentionWorker.PurgeSessionMisconceptionsAsync
            // (ADR-0038 crypto-shred path). This callback is intentionally a
            // no-op — it exists so the registry contains the canonical store,
            // so the metrics dashboard shows it, and so the architecture
            // test has something to cross-check. Returning 0 avoids
            // double-counting purges in cena_misconception_retention_purge_total.
            _ => (_, __) => Task.FromResult(0));
    }
}
