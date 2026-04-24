// =============================================================================
// Cena Platform — IItemDeliveryGate (prr-008)
//
// The single runtime chokepoint that enforces the Bagrut-reference-only
// invariant at the last moment before a student-bound item is serialised
// onto the wire. Callers at every student-delivery seam (exam simulation,
// diagnostic, practice session, tutor playback, SignalR push) invoke
// `AssertDeliverable` with the item's `Provenance`, its id, and the
// session/tenant/actor context, and the gate:
//
//   1. Permits `AiRecreated` and `TeacherAuthoredOriginal` items through.
//   2. Blocks `MinistryBagrut` items with an `InvalidOperationException`.
//   3. Writes a SIEM-grade structured log entry on block (itemId,
//      sessionId, tenantId, actorId — never the raw item body, so the
//      log is safe even if the Ministry text is what was about to leak).
//
// See `Provenance.cs` for the companion compile-time `Deliverable<T>`
// phantom-type, ADR-0032 for the write-side CAS-gated ingestion story,
// and ADR-0043 bagrut-reference-only-enforcement for the invariant.
// =============================================================================

using Microsoft.Extensions.Logging;

using Cena.Actors.Content;

namespace Cena.Actors.Assessment;

/// <summary>
/// Runtime delivery-gate chokepoint. Every student-delivery seam MUST call
/// <see cref="AssertDeliverable"/> immediately before serialising an item
/// onto the outbound wire. Bypassing the gate defeats the Bagrut-reference-only
/// invariant and will be caught (eventually) by <c>BagrutRecreationOnlyTest</c>.
/// </summary>
public interface IItemDeliveryGate
{
    /// <summary>
    /// Asserts that an item is deliverable to a student. Throws if its
    /// provenance is <see cref="ProvenanceKind.MinistryBagrut"/>; permits
    /// otherwise. On throw, a SIEM-grade structured log entry is written
    /// with the provided context fields.
    /// </summary>
    /// <param name="provenance">Origin of the item (required).</param>
    /// <param name="itemId">Public item identifier (required; included in SIEM log).</param>
    /// <param name="sessionId">Student learning-session id (required; included in SIEM log).</param>
    /// <param name="tenantId">Tenant id (ADR-0001 multi-institute scope; required; included in SIEM log).</param>
    /// <param name="actorId">Delivering actor id (StudentActor instance id or HTTP call-site; included in SIEM log).</param>
    /// <exception cref="InvalidOperationException">
    /// When <paramref name="provenance"/>.<see cref="Provenance.Kind"/> is
    /// <see cref="ProvenanceKind.MinistryBagrut"/>. This is a BUG, not a graceful
    /// fallback — callers should propagate up to the API surface as a 5xx.
    /// See prr-008 negative-integration test for the intended behaviour.
    /// </exception>
    void AssertDeliverable(
        Provenance provenance,
        string itemId,
        string sessionId,
        string tenantId,
        string actorId);
}

/// <summary>
/// Default implementation of <see cref="IItemDeliveryGate"/>. Backed by
/// an <see cref="ILogger"/> that any SIEM exporter (Serilog sink, OTel
/// log exporter, Sentry, etc.) can subscribe to via the well-known
/// event id <see cref="BagrutReferenceOnlyViolationEventId"/>.
/// </summary>
public sealed class ItemDeliveryGate : IItemDeliveryGate
{
    /// <summary>
    /// Structured-log event id that SIEM pipelines key on. Pinned value
    /// so downstream pipelines don't churn if log-line text changes.
    /// </summary>
    public static readonly EventId BagrutReferenceOnlyViolationEventId =
        new(8008, "BagrutReferenceOnlyViolation");

    private readonly ILogger<ItemDeliveryGate> _logger;

    public ItemDeliveryGate(ILogger<ItemDeliveryGate> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void AssertDeliverable(
        Provenance provenance,
        string itemId,
        string sessionId,
        string tenantId,
        string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        if (provenance.IsDeliverable) return;

        // SIEM-grade structured log. NOTE: we never log the item body —
        // that's the whole point of the gate; the Ministry content is
        // what we're refusing to emit. Only stable identifiers go in.
        _logger.LogError(
            BagrutReferenceOnlyViolationEventId,
            "Bagrut reference-only invariant violated: attempted to deliver "
            + "item with provenance={ProvenanceKind} source={ProvenanceSource} "
            + "itemId={ItemId} sessionId={SessionId} tenantId={TenantId} "
            + "actorId={ActorId}. This is a bug — see prr-008 + "
            + "ADR-0043 bagrut-reference-only-enforcement.",
            provenance.Kind,
            provenance.Source,
            itemId,
            sessionId,
            tenantId,
            actorId);

        throw new InvalidOperationException(
            $"Attempted to deliver MinistryBagrut-provenanced item '{itemId}' "
            + $"to session '{sessionId}' (tenant '{tenantId}', actor '{actorId}'). "
            + "Ministry content is reference-only per the 2026-04-15 decision "
            + "(CLAUDE.md non-negotiable 'Bagrut reference-only'). Route the "
            + "item through BagrutRecreationAggregate (ADR-0032) and deliver "
            + "the AiRecreated recreation instead.");
    }
}
