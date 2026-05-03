// =============================================================================
// Cena Platform — TutorContext cache erasure cascade (prr-152)
//
// IErasureProjectionCascade that invalidates every cached SessionTutorContext
// entry whose embedded StudentId matches the erased student. See
// ISessionTutorContextService.InvalidateAllForStudentAsync for the scan
// + delete primitive.
//
// Strategy: CACHE INVALIDATION (not delete, not anonymise). The cache is a
// derived read-through projection; invalidation is its only legitimate
// mutation. The underlying Marten session projections (the source of
// truth for rebuilds) are covered by the ADR-0038 crypto-shred of the
// student's subject key — ciphertext cannot be rebuilt into a fresh
// cache entry after that key is tombstoned.
//
// Why bother invalidating if cache entries expire on session TTL anyway?
//
//   - Default session TTL is 6 hours; the erasure cooling period is 30
//     days; so in the common case every entry is already gone. The
//     cascade still logs the explicit intent so the compliance audit
//     trail matches the declared policy.
//   - If an operator has bumped the session TTL (e.g. a support incident
//     leaves a session open for days), the bulk invalidate guarantees
//     the cache is clean even if the TTL has not fired.
//   - Idempotent: a second cascade run for the same student removes zero
//     entries, not an error.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Tutoring;

/// <summary>
/// prr-152 cascade that invalidates every cached
/// <see cref="SessionTutorContext"/> for the erased student.
/// </summary>
public sealed class TutorContextErasureCascade : IErasureProjectionCascade
{
    /// <summary>
    /// Stable name used in the erasure manifest audit trail.
    /// </summary>
    public const string StableName = "TutorContextCache";

    private readonly ISessionTutorContextService _contextService;
    private readonly ILogger<TutorContextErasureCascade> _logger;

    public TutorContextErasureCascade(
        ISessionTutorContextService contextService,
        ILogger<TutorContextErasureCascade> logger)
    {
        _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProjectionName => StableName;

    public async Task<ErasureManifestItem> EraseForStudentAsync(
        string studentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        var removed = await _contextService
            .InvalidateAllForStudentAsync(studentId, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "[SIEM] TutorContextErasureCascade: student={StudentId} keysRemoved={Count}",
            studentId, removed);

        // Cache invalidation is the "soft delete" of the read-through
        // projection — classify as Deleted for manifest purposes. Count=0
        // is the expected value in the common case (TTL already fired).
        return new ErasureManifestItem(
            store: StableName,
            action: ErasureAction.Deleted,
            count: removed,
            details: "Redis session cache entries invalidated (prr-152). " +
                     "Source-of-truth projections covered by ADR-0038 crypto-shred.");
    }
}
