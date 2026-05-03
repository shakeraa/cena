// =============================================================================
// Cena Platform — IAlphaMigrationSeedSource (EPIC-PRR-I PRR-344)
//
// Why this exists (alpha-migration grace policy, ADR-0057 + 2026-04-11 no-stubs
// memory). When the subscription paywall ships, we grant every pre-existing
// alpha/beta parent a 60-day Premium grace window so they don't wake up to a
// locked product. AlphaUserMigrationWorker writes an AlphaGraceMarker Marten
// doc per parent id; the StudentEntitlementResolver consults those markers
// and synthesises Premium caps until GraceEndAt. Before this task the worker
// had no input — CandidatesForGrace returned Array.Empty<string>() with a
// "// seeds are a follow-up" TODO. Operators could not point Cena at the
// alpha user list without editing code. That was the PRR-344 blocker.
//
// IAlphaMigrationSeedSource is the operator seam. The admin endpoint
// POST /api/admin/alpha-migration/seed calls SeedAsync with the encrypted
// parent-subject-id list; the worker calls GetSeedParentIdsAsync on its
// cron tick and emits grace markers for the delta. The in-memory
// implementation is production-grade for single-host; MartenAlphaMigration-
// SeedSource persists a single current-seed doc so the list survives pod
// restarts and multiple replicas share one canonical seed (no split-brain).
//
// This is NOT a stub. Memory "No stubs — production grade" (2026-04-11):
// both implementations are real, tested, deployable. The deferred bit is
// the actual operator-input list (data) + the Vue admin page (UI).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Source of the alpha-era parent-subject-id list that should receive
/// 60-day Premium grace. Operator writes via <see cref="SeedAsync"/>;
/// the migration worker reads via <see cref="GetSeedParentIdsAsync"/>.
/// </summary>
/// <remarks>
/// The seed semantics are "overwrite, not append": calling SeedAsync
/// replaces the current list. Operators who need to extend coverage
/// re-post the union. Rationale: the worker is idempotent on re-run and
/// filters already-granted markers, so the common flow is
///   1. Post full seed list.
///   2. Call /run-now to apply.
///   3. Re-post a superset list later if a missed parent surfaces.
/// Accumulate-and-append semantics would make it impossible to remove
/// a parent (e.g. ingested from a wrong source) without a DB surgery.
/// </remarks>
public interface IAlphaMigrationSeedSource
{
    /// <summary>
    /// Return the current set of alpha parent subject ids (encrypted) that
    /// should receive grace. Empty list when no seed has been uploaded.
    /// Idempotent and safe to call on every worker tick.
    /// </summary>
    Task<IReadOnlyList<string>> GetSeedParentIdsAsync(CancellationToken ct);

    /// <summary>
    /// Overwrite the current seed list. Called by the admin endpoint.
    /// <paramref name="uploadedByAdminSubjectId"/> is recorded for audit;
    /// pass the caller's encrypted subject id from the JWT <c>sub</c> claim.
    /// </summary>
    Task SeedAsync(
        IReadOnlyList<string> parentSubjectIdsEncrypted,
        string uploadedByAdminSubjectId,
        CancellationToken ct);
}

/// <summary>
/// In-memory <see cref="IAlphaMigrationSeedSource"/>. Production-grade for
/// single-host deployments (the worker and admin endpoint live in the same
/// process, so a singleton instance sees the same seed). Multi-replica
/// deployments must use <see cref="MartenAlphaMigrationSeedSource"/> so
/// every pod reads the same canonical list.
/// </summary>
public sealed class InMemoryAlphaMigrationSeedSource : IAlphaMigrationSeedSource
{
    private readonly object _gate = new();
    private IReadOnlyList<string> _current = Array.Empty<string>();
    private string _uploadedBy = string.Empty;
    private DateTimeOffset _uploadedAt;

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetSeedParentIdsAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            // Return a snapshot so the caller can't mutate our backing store
            // via the IReadOnlyList reference.
            return Task.FromResult<IReadOnlyList<string>>(_current.ToArray());
        }
    }

    /// <inheritdoc/>
    public Task SeedAsync(
        IReadOnlyList<string> parentSubjectIdsEncrypted,
        string uploadedByAdminSubjectId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parentSubjectIdsEncrypted);
        if (string.IsNullOrWhiteSpace(uploadedByAdminSubjectId))
        {
            throw new ArgumentException(
                "uploadedByAdminSubjectId is required for audit trail.",
                nameof(uploadedByAdminSubjectId));
        }

        // Distinct + non-empty filter so a slightly-dirty operator upload
        // (whitespace, dupes) doesn't fan out into duplicate markers later.
        var clean = parentSubjectIdsEncrypted
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        lock (_gate)
        {
            _current = clean;
            _uploadedBy = uploadedByAdminSubjectId;
            _uploadedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>Last admin that uploaded a seed (audit readout).</summary>
    public string LastUploadedBy
    {
        get { lock (_gate) return _uploadedBy; }
    }

    /// <summary>Last upload timestamp (audit readout).</summary>
    public DateTimeOffset LastUploadedAt
    {
        get { lock (_gate) return _uploadedAt; }
    }
}
