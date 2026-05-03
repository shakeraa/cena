// =============================================================================
// Cena Platform — IParentDigestPreferencesStore (prr-051 / EPIC-PRR-C).
//
// Authoritative store for per-(parent, child, institute) digest preferences.
// The contract intentionally mirrors IParentChildBindingStore:
//   - lookup by the full (parent, child, institute) triple,
//   - no "list-all-for-parent" read path (the API never needs it; a parent
//     only sees preferences inside the institute they're signed into),
//   - idempotent upsert returning the current row.
//
// Phase 1 (this task): in-memory implementation for tests + dev; a Marten-
// backed projection over ParentDigestPreferencesUpdated_V1 /
// ParentDigestUnsubscribed_V1 ships alongside EPIC-PRR-A consent work.
// Production cutover flips the DI registration; endpoint code does not
// change.
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Read + write contract for digest preferences. Scoped by the full
/// (parent, student, institute) triple. Tenant crossing is rejected at
/// the endpoint layer (<see cref="Cena.Infrastructure.Security.ParentAuthorizationGuard"/>);
/// this store does not re-check the guard but will never return a row whose
/// stored <c>InstituteId</c> does not match the request — the store key IS
/// the triple, so a cross-tenant probe misses.
/// </summary>
public interface IParentDigestPreferencesStore
{
    /// <summary>
    /// Return the currently-stored preferences for this triple, or null
    /// when the parent has never written. Callers treat null as "all
    /// purposes at NotSet" — the default-table decides the effective answer.
    /// </summary>
    Task<ParentDigestPreferences?> FindAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default);

    /// <summary>
    /// Apply the supplied per-purpose updates to the (parent, child,
    /// institute) row, creating the row if it did not exist. Returns the
    /// post-write state. Idempotent: repeating an identical update is a
    /// no-op from the caller's perspective (the UpdatedAtUtc stamp is
    /// advanced so projections observe the write, but purpose bits do
    /// not flip).
    /// </summary>
    Task<ParentDigestPreferences> ApplyUpdateAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        ImmutableDictionary<DigestPurpose, OptInStatus> updates,
        DateTimeOffset updatedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Apply the bulk one-click unsubscribe: every known purpose → OptedOut,
    /// stamped with <paramref name="unsubscribedAtUtc"/>. Idempotent: calling
    /// a second time after an unsubscribe leaves the opt-out state intact
    /// and only advances the stamp (matching the endpoint contract that
    /// the link may be followed twice — the second click is a no-op, not
    /// a 500).
    /// </summary>
    Task<ParentDigestPreferences> ApplyUnsubscribeAllAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset unsubscribedAtUtc,
        CancellationToken ct = default);
}
