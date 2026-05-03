// =============================================================================
// Cena Platform — ConsentAggregate write-side seam (prr-155)
//
// Interface declared in Cena.Infrastructure so that GdprConsentManager can
// shadow-write through the ConsentAggregate primitive without creating a
// reverse project reference (Cena.Actors -> Cena.Infrastructure is the
// forward direction; Infrastructure -> Actors would be a cycle).
//
// The concrete adapter lives in Cena.Actors.Consent and is registered by
// AddConsentAggregate() alongside the aggregate store. If no adapter is
// registered, GdprConsentManager falls back to document-only behaviour and
// logs a one-time warning at startup — this preserves existing behaviour
// when a Host has not opted into the new aggregate yet.
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Write-side seam for the ConsentAggregate. <c>GdprConsentManager</c>
/// delegates to an implementation of this interface to record consent
/// grants / revocations as aggregate events in parallel with its existing
/// document-store writes. The adapter lives in Cena.Actors.Consent.
/// </summary>
public interface IConsentAggregateWriter
{
    /// <summary>
    /// Record a grant as a ConsentGranted_V1 event on the subject's stream.
    /// Implementations MUST encrypt subject/actor ids per ADR-0038 and MUST
    /// enforce age-band authorization per ADR-0041.
    /// </summary>
    Task GrantAsync(
        string subjectId,
        ProcessingPurpose legacyPurpose,
        bool isMinor,
        string recordedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Record a revoke as a ConsentRevoked_V1 event on the subject's stream.
    /// </summary>
    Task RevokeAsync(
        string subjectId,
        ProcessingPurpose legacyPurpose,
        bool isMinor,
        string recordedBy,
        string reason,
        CancellationToken ct = default);
}
