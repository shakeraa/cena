// =============================================================================
// Cena Platform — ParentDigestPreferences erasure cascade (prr-152)
//
// IErasureProjectionCascade implementation that deletes every
// parent-digest-preferences row keyed by the erased student's subject id,
// regardless of which parent owns them or which institute the pair lives
// in. Safety-alerts defaulting-ON is a DIGEST concern, not a compliance
// one — when the student is erased the digest pipeline must stop firing
// for that student on any (parent, institute) slice.
//
// Strategy: HARD DELETE. ParentDigestPreferences is a mutable projection
// (Marten doc, not an append-only event stream), so row-level DELETE is
// appropriate and leaves no residue. The underlying consent events
// (ParentDigestPreferencesUpdated_V1, ParentDigestUnsubscribed_V1) stay
// in the event stream — those are the legal provenance, and they will be
// crypto-shredded by the existing ADR-0038 subject-key tombstone.
//
// Idempotence: the store's FindAsync returns null when no row exists, so
// a second run is a no-op (zero rows deleted).
//
// ADR-0003 + ADR-0038 preservation: no misconception data touched; the
// crypto-shred of the subject key already covers the append-only event
// stream; only the cached projection rows are actively deleted here.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// prr-152 cascade for <see cref="ParentDigestPreferences"/> projection rows.
/// Scans every (parent, child=erased-student, institute) triple and removes
/// matching rows from the store.
/// </summary>
public sealed class ParentDigestErasureCascade : IErasureProjectionCascade
{
    /// <summary>
    /// Stable name used in the erasure manifest audit trail and in
    /// <c>[RegisteredMisconceptionStoreAttribute]</c>-style allowlists.
    /// </summary>
    public const string StableName = "ParentDigestPreferences";

    private readonly IParentDigestPreferencesErasureSink _sink;
    private readonly ILogger<ParentDigestErasureCascade> _logger;

    public ParentDigestErasureCascade(
        IParentDigestPreferencesErasureSink sink,
        ILogger<ParentDigestErasureCascade> logger)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProjectionName => StableName;

    public async Task<ErasureManifestItem> EraseForStudentAsync(
        string studentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        var deleted = await _sink.DeleteByStudentAsync(studentId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[SIEM] ParentDigestErasureCascade: student={StudentId} rowsDeleted={Count}",
            studentId, deleted);

        return new ErasureManifestItem(
            store: StableName,
            action: ErasureAction.Deleted,
            count: deleted,
            details: "All (parent, child=erased, institute) preference rows hard-deleted (prr-152).");
    }
}

/// <summary>
/// Abstraction used by <see cref="ParentDigestErasureCascade"/> for the
/// actual delete-by-student operation. Lives here (not on
/// <see cref="IParentDigestPreferencesStore"/>) because erasure is a
/// compliance seam — the normal store contract intentionally lacks a
/// "delete by student" method to avoid accidental bulk deletion from
/// product code paths.
/// </summary>
public interface IParentDigestPreferencesErasureSink
{
    /// <summary>
    /// Remove every preference row whose <c>StudentSubjectId</c> matches
    /// <paramref name="studentId"/>. Returns the number of rows removed.
    /// Idempotent — a second call with the same student id returns 0.
    /// </summary>
    Task<int> DeleteByStudentAsync(string studentId, CancellationToken ct);
}
