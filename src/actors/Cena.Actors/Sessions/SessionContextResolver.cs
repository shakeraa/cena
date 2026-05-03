// =============================================================================
// Cena Platform — SessionContextResolver + NullSessionContextResolver
// (EPIC-PRR-I PRR-310, SLICE 1 of 3)
//
// FIRST-SLICE FRAMING
// -------------------
// Default resolver composes the existing entitlement resolver with the
// tier catalog to build an immutable SessionContext snapshot at session
// start. See SessionContext.cs for the broader "slice 1 of 3" framing
// and what's deferred.
//
// NULL-OBJECT FALLBACK (legitimate — NOT a stub)
// ----------------------------------------------
// <see cref="NullSessionContextResolver"/> is the honest fallback for
// hosts that have not wired an IStudentEntitlementResolver (e.g., tests
// that don't exercise the entitlement path, or pre-subscriptions bring-
// up phases). It returns a SessionContext with:
//
//   PinnedTier     = SubscriptionTier.Unsubscribed
//   PinnedCaps     = TierCatalog.Get(Unsubscribed).Caps     (all zeros)
//   PinnedFeatures = TierCatalog.Get(Unsubscribed).Features (all false)
//
// Zero-cap + feature-off is the DENY-EVERYTHING posture. Downstream
// cap-enforcers interpret that as "no quota" and refuse paid features,
// which is the safest failure mode when entitlement wiring is absent.
// This mirrors the existing convention on StudentEntitlementResolver
// .SynthesizeUnsubscribed — "the hot path always has a live object to
// enforce against."
//
// This is NOT a stub per the 2026-04-11 "No stubs — production grade"
// directive: a Null-object that yields the safest posture is a shipped
// architectural choice (see NullEmailSender, NoopRefundUsageProbe for
// the same pattern elsewhere in this repo), not placeholder scaffolding
// waiting for a real implementation.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Actors.Sessions;

/// <summary>
/// Default <see cref="ISessionContextResolver"/> — composes the existing
/// <see cref="IStudentEntitlementResolver"/> and the tier catalog to build
/// an immutable SessionContext at session start.
/// </summary>
public sealed class SessionContextResolver : ISessionContextResolver
{
    private readonly IStudentEntitlementResolver _entitlements;

    public SessionContextResolver(IStudentEntitlementResolver entitlements)
    {
        _entitlements = entitlements
            ?? throw new ArgumentNullException(nameof(entitlements));
    }

    /// <inheritdoc/>
    public async Task<SessionContext> ResolveAtSessionStartAsync(
        string sessionId,
        string studentSubjectIdEncrypted,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        // Input validation intentionally duplicates the record's constructor
        // checks — we want a clear failure site here at the seam before
        // touching the entitlement store, and the record's own checks are
        // the backstop if a caller bypasses this seam.
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException(
                "Session id must be non-empty.", nameof(sessionId));
        }
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Student subject id must be non-empty.",
                nameof(studentSubjectIdEncrypted));
        }

        var entitlement = await _entitlements
            .ResolveAsync(studentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);

        // StudentEntitlementView already exposes Caps + Features via
        // TierCatalog.Get(...). Snapshot them HERE so the SessionContext
        // is fully-materialised and does not depend on the catalog being
        // a live singleton. This is the immutability contract.
        return new SessionContext(
            sessionId: sessionId,
            studentSubjectIdEncrypted: studentSubjectIdEncrypted,
            pinnedTier: entitlement.EffectiveTier,
            pinnedCaps: entitlement.Caps,
            pinnedFeatures: entitlement.Features,
            startedAt: startedAt);
    }
}

/// <summary>
/// Null-object resolver — returns an Unsubscribed + zero-caps + features-
/// off SessionContext regardless of student. Legitimate fallback (see
/// file banner). DO NOT use when you have a real entitlement store
/// wired; only use when the host genuinely does not participate in the
/// subscription bounded context (e.g., isolated component tests).
/// </summary>
public sealed class NullSessionContextResolver : ISessionContextResolver
{
    /// <summary>
    /// Shared instance — there is no mutable state, so one is enough.
    /// </summary>
    public static NullSessionContextResolver Instance { get; } = new();

    /// <inheritdoc/>
    public Task<SessionContext> ResolveAtSessionStartAsync(
        string sessionId,
        string studentSubjectIdEncrypted,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        var unsubscribed = TierCatalog.Get(SubscriptionTier.Unsubscribed);
        var snapshot = new SessionContext(
            sessionId: sessionId,
            studentSubjectIdEncrypted: studentSubjectIdEncrypted,
            pinnedTier: SubscriptionTier.Unsubscribed,
            pinnedCaps: unsubscribed.Caps,
            pinnedFeatures: unsubscribed.Features,
            startedAt: startedAt);
        return Task.FromResult(snapshot);
    }
}
