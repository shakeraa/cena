// =============================================================================
// Cena Platform — IParentChildBindingStore (prr-009, EPIC-PRR-C)
//
// Authoritative source of truth for parent → child bindings. Read-path
// for ParentAuthorizationGuard: the JWT `parent_of` cache is a hint;
// this store's answer is the one that actually gates access.
//
// Phase 1 (this task): in-memory implementation for tests + dev; Marten-
// backed implementation is a Sprint 2 follow-up tracked under EPIC-PRR-A
// (same migration pattern ADR-0042 documents for the consent aggregate).
// Production cutover flips the DI registration; endpoint-level code does
// not change.
// =============================================================================

namespace Cena.Actors.Parent;

/// <summary>
/// Read + mutation contract for parent-child bindings. Contract is
/// intentionally narrow: look up by parent id, look up by
/// (parent, student, institute) triple, record a grant, record a
/// revocation. Anything broader belongs to the consent bounded context
/// (prr-155); this store is the lookup-path source of truth for the
/// authorization guard only.
/// </summary>
public interface IParentChildBindingStore
{
    /// <summary>
    /// Return the currently-active binding for this triple, or null when
    /// no active binding exists. Revoked or never-granted both return
    /// null; callers do not get to distinguish the two cases (refusing
    /// that distinction prevents existence-oracle leaks per
    /// TeacherOverride ADR-0001 precedent).
    /// </summary>
    Task<ParentChildBinding?> FindActiveAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default);

    /// <summary>
    /// Return every active binding this parent currently holds. Used at
    /// login / session-refresh time to populate the <c>parent_of</c>
    /// JWT claim cache.
    /// </summary>
    Task<IReadOnlyList<ParentChildBinding>> ListActiveForParentAsync(
        string parentActorId,
        CancellationToken ct = default);

    /// <summary>
    /// Record a new active binding. Idempotent: granting an already-active
    /// binding is a no-op and returns the existing record.
    /// </summary>
    Task<ParentChildBinding> GrantAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset grantedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Mark an existing active binding as revoked. No-op if no active
    /// binding exists.
    /// </summary>
    Task RevokeAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset revokedAtUtc,
        CancellationToken ct = default);
}
