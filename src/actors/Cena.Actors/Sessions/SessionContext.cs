// =============================================================================
// Cena Platform — SessionContext (EPIC-PRR-I PRR-310, SLICE 1 of 3)
//
// FIRST-SLICE FRAMING
// -------------------
// PRR-310 in full is: propagate the student's effective SubscriptionTier +
// UsageCaps end-to-end — student-api, NATS envelope headers, LLM router,
// diagnostic pipeline, session-actor pinning. That is multi-hour cross-
// cutting work. Slice 1 (this file) ships the FOUNDATION — the immutable
// per-session snapshot record — so the follow-up slices can wire it
// through NATS envelope enrichment (slice 2) and session-actor pinning
// (slice 3) without refactoring the type.
//
// DEFERRED TO FOLLOW-UP SLICES
// ----------------------------
//  • NATS request envelope headers carrying tier + caps on every
//    student→actor call.
//  • SessionActor pinning at StartSession (LearningSessionActor reads
//    SessionContext at session start; no mid-session re-resolve).
//  • LLM router + diagnostic intake consuming SessionContext rather than
//    re-resolving entitlement per request.
//
// INVARIANT (non-negotiable, ADR-0003-adjacent)
// ---------------------------------------------
// SessionContext is IMMUTABLE for the duration of a session. Tier upgrades
// that happen mid-session take effect on the NEXT session, not this one.
// This is a UX + correctness decision: caps must not shift under an active
// student, and downstream caches (LLM router, cap enforcer) can trust a
// session-scoped key.
//
// Why a record not a class:
//   - Value-equality (same snapshot = same instance) is the natural semantic.
//   - `init` properties + positional syntax give us construction-time
//     validation + no mutation after the fact.
//
// Why "Encrypted" in the student id name:
//   - Matches StudentEntitlementView.StudentSubjectIdEncrypted which is the
//     wire-format encrypted subject id per ADR-0038. We do not decrypt at
//     the session seam; resolver and downstream consumers stay symmetric.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Actors.Sessions;

/// <summary>
/// Immutable per-session snapshot of a student's effective entitlement.
///
/// Pinned at session start by <see cref="ISessionContextResolver"/>; held
/// for the session's full duration. Tier upgrades that occur mid-session
/// apply only to the NEXT session (see class-level file banner for the
/// rationale — predictable UX + cacheability at the enforcement seams).
/// </summary>
/// <param name="SessionId">
/// Opaque session identifier (non-empty). Identity of this snapshot for
/// caching and NATS-envelope correlation.
/// </param>
/// <param name="StudentSubjectIdEncrypted">
/// Student subject id in wire-format encryption (ADR-0038). Matches
/// <see cref="StudentEntitlementView.StudentSubjectIdEncrypted"/>.
/// </param>
/// <param name="PinnedTier">
/// The subscription tier that was effective at session start. Does NOT
/// change for the life of this snapshot.
/// </param>
/// <param name="PinnedCaps">Usage caps for <paramref name="PinnedTier"/>.</param>
/// <param name="PinnedFeatures">Feature flags for <paramref name="PinnedTier"/>.</param>
/// <param name="StartedAt">Session start timestamp (UTC).</param>
public sealed record SessionContext
{
    public string SessionId { get; }
    public string StudentSubjectIdEncrypted { get; }
    public SubscriptionTier PinnedTier { get; }
    public UsageCaps PinnedCaps { get; }
    public TierFeatureFlags PinnedFeatures { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Construct a SessionContext snapshot. Validates non-empty string
    /// fields and non-null reference fields — failing fast at the seam
    /// where the snapshot is assembled rather than at the consumer.
    /// </summary>
    public SessionContext(
        string sessionId,
        string studentSubjectIdEncrypted,
        SubscriptionTier pinnedTier,
        UsageCaps pinnedCaps,
        TierFeatureFlags pinnedFeatures,
        DateTimeOffset startedAt)
    {
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

        SessionId = sessionId;
        StudentSubjectIdEncrypted = studentSubjectIdEncrypted;
        PinnedTier = pinnedTier;
        PinnedCaps = pinnedCaps
            ?? throw new ArgumentNullException(nameof(pinnedCaps));
        PinnedFeatures = pinnedFeatures
            ?? throw new ArgumentNullException(nameof(pinnedFeatures));
        StartedAt = startedAt;
    }
}
