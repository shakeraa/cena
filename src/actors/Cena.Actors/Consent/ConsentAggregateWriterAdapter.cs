// =============================================================================
// Cena Platform — ConsentAggregate write-side adapter (prr-155)
//
// Bridges Cena.Infrastructure's IConsentAggregateWriter seam to the
// concrete aggregate in Cena.Actors.Consent. Registered by
// AddConsentAggregate() so that GdprConsentManager (facade) can
// shadow-write aggregate events alongside the existing Marten-document
// writes.
//
// Behaviour on authorization refusal:
//   - Legacy GdprConsentManager does not have the age-band context that
//     the aggregate needs to make an ADR-0041 decision. To avoid breaking
//     callers that already have their own age-gating (e.g. the
//     MeGdprEndpoints IsMinor<16 check), this adapter maps isMinor->
//     AgeBand.Teen13to15 (the under-16 bucket) and isMinor=false->
//     AgeBand.Adult. This is intentionally coarse and will tighten once
//     prr-014 (Parent role) lands and actors route through the new
//     endpoint surface.
//   - Actor role for legacy writes is ActorRole.Student when the recorder
//     is the subject themselves (self-service consent flow), falling back
//     to ActorRole.Admin when the recorder differs — the legacy API does
//     not distinguish, so this is a judgment call that matches the
//     existing MeGdprEndpoints caller semantics.
// =============================================================================

using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Consent;

/// <summary>
/// Adapter mapping <see cref="IConsentAggregateWriter"/> calls onto
/// <see cref="ConsentCommandHandler"/> + <see cref="IConsentAggregateStore"/>.
/// </summary>
public sealed class ConsentAggregateWriterAdapter : IConsentAggregateWriter
{
    private readonly ConsentCommandHandler _handler;
    private readonly IConsentAggregateStore _store;

    public ConsentAggregateWriterAdapter(
        ConsentCommandHandler handler,
        IConsentAggregateStore store)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task GrantAsync(
        string subjectId,
        ProcessingPurpose legacyPurpose,
        bool isMinor,
        string recordedBy,
        CancellationToken ct = default)
    {
        var aggPurpose = ConsentPurposeMapping.TryToConsentPurpose(legacyPurpose);
        if (!aggPurpose.HasValue)
        {
            // Legacy contract-necessity purposes (AccountAuth, SessionContinuity)
            // aren't subject to consent events; skip silently.
            return;
        }

        var band = isMinor ? AgeBand.Teen13to15 : AgeBand.Adult;
        var role = ResolveActorRole(subjectId, recordedBy);

        try
        {
            var evt = await _handler.HandleAsync(new GrantConsent(
                SubjectId: subjectId,
                SubjectBand: band,
                Purpose: aggPurpose.Value,
                Scope: "legacy-facade",
                GrantedByRole: role,
                GrantedByActorId: recordedBy,
                GrantedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null), ct).ConfigureAwait(false);

            await _store.AppendAsync(subjectId, evt, ct).ConfigureAwait(false);
        }
        catch (ConsentAuthorizationException)
        {
            // Shadow-write path: if the aggregate refuses, the legacy path
            // has already written the document (or is about to). We do NOT
            // propagate the refusal in this phase — that would be a behaviour
            // regression. Phase 2 (ADR-0041 full rollout) will flip this so
            // refusals propagate to the facade.
            //
            // Silent: do not log PII; the aggregate itself emits a SIEM line
            // inside its own logger if one is wired. The absence of the
            // shadow event is audit-detectable via the aggregate store's
            // missing stream.
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        string subjectId,
        ProcessingPurpose legacyPurpose,
        bool isMinor,
        string recordedBy,
        string reason,
        CancellationToken ct = default)
    {
        var aggPurpose = ConsentPurposeMapping.TryToConsentPurpose(legacyPurpose);
        if (!aggPurpose.HasValue) return;

        var band = isMinor ? AgeBand.Teen13to15 : AgeBand.Adult;
        var role = ResolveActorRole(subjectId, recordedBy);

        try
        {
            var evt = await _handler.HandleAsync(new RevokeConsent(
                SubjectId: subjectId,
                SubjectBand: band,
                Purpose: aggPurpose.Value,
                RevokedByRole: role,
                RevokedByActorId: recordedBy,
                RevokedAt: DateTimeOffset.UtcNow,
                Reason: string.IsNullOrWhiteSpace(reason) ? "legacy-facade-revoke" : reason), ct)
                .ConfigureAwait(false);

            await _store.AppendAsync(subjectId, evt, ct).ConfigureAwait(false);
        }
        catch (ConsentAuthorizationException)
        {
            // Same shadow-write discipline as GrantAsync — see comment there.
        }
    }

    private static ActorRole ResolveActorRole(string subjectId, string recordedBy)
    {
        if (string.Equals(subjectId, recordedBy, StringComparison.Ordinal))
        {
            return ActorRole.Student;
        }
        // Recorded by a different actor — the legacy API does not distinguish
        // parent vs admin vs teacher, so we default to Admin (the most
        // common path for write-via-operator compliance actions).
        return ActorRole.Admin;
    }
}
