// =============================================================================
// Cena Platform — ITaxonomyVersionStore (EPIC-PRR-J PRR-375)
//
// Abstraction over the governance-version store. Two implementations ship
// in this commit:
//   InMemoryTaxonomyVersionStore — production-grade for dev/test (not a stub,
//   mirrors the InMemoryChurnReasonRepository / InMemoryDiagnosticDisputeRepository
//   idiom); all methods honour the full interface contract with concurrency-
//   safe semantics via a lock on the internal dictionary.
//   MartenTaxonomyVersionStore — production path, Marten-backed.
//
// Interface invariants enforced here (not just in documentation):
//   - ProposeAsync assigns the next monotonic TaxonomyVersion per TemplateKey
//     atomically. Two concurrent Propose calls for the same key get
//     consecutive version numbers, not a collision.
//   - ApproveAsync REQUIRES the row's Reviewers list to contain at least
//     TaxonomyVersionDocument.MinReviewersForApproval distinct non-empty
//     entries. Callers that want to approve must have already accumulated
//     reviewer sign-offs on the row (via UpsertAsync) or the store throws
//     InvalidOperationException — the two-reviewer guardrail is not
//     optional.
//   - RollbackAsync and DeprecateAsync are idempotent w.r.t. the target
//     status but will throw if the row does not exist. They do NOT delete;
//     the audit trail is preserved.
//   - GetLatestApprovedAsync returns the highest-TaxonomyVersion row with
//     Status == Approved. Rolled-back and deprecated rows are skipped.
//     Returns null if no Approved version exists for the key.
//   - ListVersionsAsync returns all versions for the key ordered by
//     TaxonomyVersion DESC. Empty list (never null) if the key is unknown.
//
// Threading model:
//   InMemory impl uses ConcurrentDictionary plus a lock per TemplateKey for
//   the read-modify-write paths (propose, approve, rollback, deprecate).
//   This is enough for dev/test load; Marten relies on PostgreSQL's
//   serializable isolation + Marten's optimistic concurrency.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;

/// <summary>Read+write contract for the taxonomy-version store.</summary>
public interface ITaxonomyVersionStore
{
    /// <summary>
    /// Upsert a row by Id. Used for reviewer-list accretion and raw writes
    /// (tests, bulk imports). Does NOT enforce the two-reviewer rule on its
    /// own — callers that want to approve must route through
    /// <see cref="ApproveAsync"/>. Idempotent on Id.
    /// </summary>
    Task UpsertAsync(TaxonomyVersionDocument doc, CancellationToken ct);

    /// <summary>
    /// Return the highest-<c>TaxonomyVersion</c> row for the given key with
    /// <see cref="TaxonomyVersionStatus.Approved"/> status. Skips
    /// RolledBack / Deprecated rows so rollback resurrects the prior
    /// Approved version automatically. Returns <c>null</c> if no approved
    /// version exists.
    /// </summary>
    Task<TaxonomyVersionDocument?> GetLatestApprovedAsync(
        string templateKey, CancellationToken ct);

    /// <summary>
    /// Return every row for the given key, ordered by TaxonomyVersion
    /// descending. Empty list if the key is unknown (never <c>null</c>).
    /// </summary>
    Task<IReadOnlyList<TaxonomyVersionDocument>> ListVersionsAsync(
        string templateKey, CancellationToken ct);

    /// <summary>
    /// Append a new Proposed row for <paramref name="templateKey"/> with the
    /// next monotonic <c>TaxonomyVersion</c>. The store assigns the version
    /// number atomically; callers pass content + author only.
    /// </summary>
    Task<TaxonomyVersionDocument> ProposeAsync(
        string templateKey,
        string templateContent,
        string authoredBy,
        DateTimeOffset authoredAtUtc,
        CancellationToken ct);

    /// <summary>
    /// Flip the row identified by <paramref name="versionId"/> from Proposed
    /// to Approved. <paramref name="reviewer"/> is appended to the row's
    /// Reviewers list (deduped, case-insensitive). The transition is ONLY
    /// applied once the row accumulates ≥
    /// <see cref="TaxonomyVersionDocument.MinReviewersForApproval"/>
    /// distinct reviewers; until then the call returns the updated
    /// (still-Proposed) row. Throws
    /// <see cref="InvalidOperationException"/> if the row is already
    /// terminal (Approved/Deprecated/RolledBack).
    /// </summary>
    Task<TaxonomyVersionDocument> ApproveAsync(
        Guid versionId,
        string reviewer,
        DateTimeOffset approvedAtUtc,
        CancellationToken ct);

    /// <summary>
    /// Flip the row to <see cref="TaxonomyVersionStatus.RolledBack"/>.
    /// Preserves content, reviewers, and ApprovedAtUtc so the audit trail
    /// stays intact. Previous Approved version (if any) becomes live again
    /// via GetLatestApprovedAsync. Throws if the row does not exist.
    /// </summary>
    Task<TaxonomyVersionDocument> RollbackAsync(
        Guid versionId, CancellationToken ct);

    /// <summary>
    /// Flip the row to <see cref="TaxonomyVersionStatus.Deprecated"/>. Used
    /// when a template is being retired (not a bad version — just no
    /// longer in use). Preserves content. Throws if the row does not exist.
    /// </summary>
    Task<TaxonomyVersionDocument> DeprecateAsync(
        Guid versionId, CancellationToken ct);
}
