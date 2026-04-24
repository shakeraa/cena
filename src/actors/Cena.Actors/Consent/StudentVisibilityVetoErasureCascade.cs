// =============================================================================
// Cena Platform — StudentVisibilityVeto erasure cascade (prr-152)
//
// IErasureProjectionCascade that records the erasure handling of
// StudentVisibilityVetoed_V1 + StudentVisibilityRestored_V1 events (prr-052).
//
// Strategy: PRESERVE via ADR-0038 crypto-shred.
//
// These events live on the append-only ConsentAggregate stream
// (consent-{studentSubjectId}). Per ADR-0038 §"Crypto-shredding for
// append-only streams", the subject-key tombstone fired by
// RightToErasureService makes every ciphertext in the stream
// undecryptable — which is the ADR-mandated equivalent of deletion for
// append-only streams.
//
// We therefore DO NOT rewrite or purge these events; instead, we record
// the Preserved action in the manifest so the audit trail shows the
// policy was considered. The existing ErasureWorker's subject-key
// DeleteAsync call remains the active mechanism — this cascade only
// attests that we have not forgotten about the new projection.
//
// Why record a Preserved action with Count=0?
//
//   The arch test ErasureCascadeCoversAllPerStudentDocsTest enforces that
//   every *Document with a student-id field has a named cascade OR an
//   allowlist entry with a compliance reason. StudentVisibilityVeto is an
//   event, not a Document, but it carries StudentSubjectIdEncrypted —
//   close enough that the compliance review wanted a visible cascade
//   rather than a silent allowlist. This cascade is the documented
//   ratchet entry: "yes, we considered it; crypto-shred is the mechanism".
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Consent;

/// <summary>
/// prr-152 cascade for the StudentVisibility* consent events. Records the
/// Preserved action so the manifest shows we considered the projection;
/// the actual "deletion" is ADR-0038 crypto-shred of the subject key,
/// handled by <c>ErasureWorker</c>.
/// </summary>
public sealed class StudentVisibilityVetoErasureCascade : IErasureProjectionCascade
{
    /// <summary>
    /// Stable name used in the erasure manifest audit trail.
    /// </summary>
    public const string StableName = "StudentVisibilityVetoEvents";

    private readonly ILogger<StudentVisibilityVetoErasureCascade> _logger;

    public StudentVisibilityVetoErasureCascade(
        ILogger<StudentVisibilityVetoErasureCascade> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProjectionName => StableName;

    public Task<ErasureManifestItem> EraseForStudentAsync(
        string studentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        _logger.LogInformation(
            "[SIEM] StudentVisibilityVetoErasureCascade: student={StudentId} " +
            "events preserved via ADR-0038 crypto-shred (append-only stream).",
            studentId);

        return Task.FromResult(new ErasureManifestItem(
            store: StableName,
            action: ErasureAction.Preserved,
            count: 0,
            details: "Append-only ConsentAggregate events; crypto-shred of subject " +
                     "key renders ciphertext undecryptable (ADR-0038 + prr-152)."));
    }
}
