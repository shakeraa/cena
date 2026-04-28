// =============================================================================
// Cena Platform — SubscriptionState (EPIC-PRR-I, ADR-0057)
//
// In-memory fold of subscription events. State is pure — no wall-clock,
// no persistence. Consumers ask IsActiveAsOf(now) for effective-state
// checks. Matches the ADR-0042 bounded-context state pattern.
//
// Linked students are tracked as an ordered list; primary = ordinal 0,
// siblings = 1..N. Ordinal is stable across re-ordering to preserve
// historical discount-depth semantics (a student that was ever ordinal 1
// keeps its discount even if another sibling is added/removed later —
// matches billing convention and is auditable).
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Folded state of a single <see cref="SubscriptionAggregate"/> instance.
/// </summary>
public sealed class SubscriptionState
{
    private readonly List<LinkedStudent> _linkedStudents = new();

    /// <summary>Current lifecycle status. Default Unsubscribed.</summary>
    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Unsubscribed;

    /// <summary>Current active tier. Unsubscribed until activation.</summary>
    public SubscriptionTier CurrentTier { get; private set; } = SubscriptionTier.Unsubscribed;

    /// <summary>Current billing cycle. None until activation.</summary>
    public BillingCycle CurrentCycle { get; private set; } = BillingCycle.None;

    /// <summary>Wire-format encrypted parent id (populated at activation).</summary>
    public string? ParentSubjectIdEncrypted { get; private set; }

    /// <summary>First activation timestamp. Null until activated.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>Next renewal boundary. Null when terminal.</summary>
    public DateTimeOffset? RenewsAt { get; private set; }

    /// <summary>Cancellation timestamp. Null unless cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Refund timestamp. Null unless refunded.</summary>
    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>Most-recent payment failure attempt count. 0 when payment is current.</summary>
    public int ConsecutivePaymentFailures { get; private set; }

    /// <summary>Ordered list of students linked to this subscription.</summary>
    public IReadOnlyList<LinkedStudent> LinkedStudents => _linkedStudents;

    // ----- Trial-cycle state (design §3) ---------------------------------
    // Populated on TrialStarted_V1 and reset on TrialExpired_V1 / on the
    // subsequent SubscriptionActivated_V1 that follows TrialConverted_V1.

    /// <summary>Wall-clock start of the current/most-recent trial. Null when never trialled.</summary>
    public DateTimeOffset? TrialStartedAt { get; private set; }

    /// <summary>Wall-clock calendar end of the current/most-recent trial. Null when never trialled.</summary>
    public DateTimeOffset? TrialEndsAt { get; private set; }

    /// <summary>Origin of the current/most-recent trial. Null when never trialled.</summary>
    public TrialKind? TrialOrigin { get; private set; }

    /// <summary>
    /// Caps pinned at trial-start. Null until the first trial starts and
    /// remains populated through Expired/Active so analytics can reconstruct
    /// "what allotment was this user offered?" without replaying.
    /// </summary>
    public Events.TrialCapsSnapshot? TrialCaps { get; private set; }

    /// <summary>
    /// Stripe card-fingerprint hash (SHA-256) recorded at trial-start.
    /// Empty for InstituteCode trials. Persists past trial end for the
    /// abuse-defense ledger join in <see cref="TrialFingerprintLedger"/>
    /// (separate task).
    /// </summary>
    public string TrialFingerprintHash { get; private set; } = string.Empty;

    /// <summary>
    /// A/B experiment variant locked at trial-start (design §5.21). Empty
    /// or <c>v1-baseline</c> until PRR-332 ships.
    /// </summary>
    public string TrialExperimentVariantId { get; private set; } = string.Empty;

    /// <summary>
    /// True if the subscription is currently active as of <paramref name="now"/>.
    /// Not simply <see cref="Status"/> == Active — also checks <see cref="RenewsAt"/>.
    /// </summary>
    public bool IsActiveAsOf(DateTimeOffset now) =>
        Status == SubscriptionStatus.Active && RenewsAt.HasValue && RenewsAt.Value > now;

    /// <summary>
    /// True if the subscription is currently in a trial as of
    /// <paramref name="now"/>. Returns false when the trial has passed
    /// its calendar boundary even if no <see cref="Events.TrialExpired_V1"/>
    /// has been applied yet — the read-side cap check must not extend a
    /// trial past its wall-clock end. Cap-only trials (duration days = 0)
    /// stay <c>true</c> while <see cref="Status"/> = Trialing because the
    /// calendar bound never fires — those expire on cap-hit telemetry,
    /// emitted by the cap enforcer (separate task).
    /// </summary>
    public bool IsTrialingAsOf(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trialing) return false;
        if (!TrialStartedAt.HasValue || !TrialEndsAt.HasValue) return false;
        // Cap-only trial: TrialEndsAt was pinned equal to TrialStartedAt
        // because TrialAllotmentConfig.TrialDurationDays = 0. Calendar
        // boundary is "open" — Trialing remains effective until a cap-hit
        // event triggers ExpireTrial via the enforcer.
        if (TrialEndsAt.Value == TrialStartedAt.Value) return true;
        return TrialEndsAt.Value > now;
    }

    // ----- Event application -----

    internal void Apply(SubscriptionActivated_V1 e)
    {
        ParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted;
        Status = SubscriptionStatus.Active;
        CurrentTier = e.Tier;
        CurrentCycle = e.Cycle;
        ActivatedAt = e.ActivatedAt;
        RenewsAt = e.RenewsAt;
        ConsecutivePaymentFailures = 0;

        _linkedStudents.Clear();
        _linkedStudents.Add(new LinkedStudent(
            StudentSubjectIdEncrypted: e.PrimaryStudentSubjectIdEncrypted,
            Ordinal: 0,
            Tier: e.Tier,
            LinkedAt: e.ActivatedAt));
    }

    internal void Apply(TierChanged_V1 e)
    {
        CurrentTier = e.ToTier;
        // Primary student moves to the new tier by default; siblings keep theirs.
        if (_linkedStudents.Count > 0)
        {
            var primary = _linkedStudents[0];
            _linkedStudents[0] = primary with { Tier = e.ToTier };
        }
    }

    internal void Apply(BillingCycleChanged_V1 e)
    {
        CurrentCycle = e.ToCycle;
    }

    internal void Apply(SiblingEntitlementLinked_V1 e)
    {
        _linkedStudents.Add(new LinkedStudent(
            StudentSubjectIdEncrypted: e.SiblingStudentSubjectIdEncrypted,
            Ordinal: e.SiblingOrdinal,
            Tier: e.Tier,
            LinkedAt: e.LinkedAt));
    }

    internal void Apply(SiblingEntitlementUnlinked_V1 e)
    {
        var idx = _linkedStudents.FindIndex(
            s => s.StudentSubjectIdEncrypted == e.SiblingStudentSubjectIdEncrypted);
        if (idx >= 0)
        {
            _linkedStudents.RemoveAt(idx);
        }
    }

    internal void Apply(RenewalProcessed_V1 e)
    {
        Status = SubscriptionStatus.Active;
        RenewsAt = e.NextRenewsAt;
        ConsecutivePaymentFailures = 0;
    }

    internal void Apply(PaymentFailed_V1 e)
    {
        Status = SubscriptionStatus.PastDue;
        ConsecutivePaymentFailures = e.AttemptNumber;
    }

    internal void Apply(SubscriptionCancelled_V1 e)
    {
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = e.CancelledAt;
    }

    internal void Apply(SubscriptionRefunded_V1 e)
    {
        Status = SubscriptionStatus.Refunded;
        RefundedAt = e.RefundedAt;
    }

    internal void Apply(EntitlementSoftCapReached_V1 _)
    {
        // Soft-cap events are telemetry; do not change state. Present for
        // stream completeness only (replay reconstructs usage analytics).
    }

    // ----- Trial-cycle Apply methods (design §3) ------------------------

    internal void Apply(TrialStarted_V1 e)
    {
        ParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted;
        Status = SubscriptionStatus.Trialing;
        // Trial does NOT set CurrentTier — the resolver synthesizes a view
        // from TrialPlus + the live caps snapshot. Keeping CurrentTier =
        // Unsubscribed makes the SubscriptionTier persisted on the parent
        // stream honest about commercial state ("not yet a paid tier").
        TrialStartedAt = e.TrialStartedAt;
        TrialEndsAt = e.TrialEndsAt;
        TrialOrigin = e.TrialKind;
        TrialCaps = e.CapsSnapshot;
        TrialFingerprintHash = e.FingerprintHash ?? string.Empty;
        TrialExperimentVariantId = e.ExperimentVariantId ?? string.Empty;

        // Pin the primary student so the entitlement resolver can fan the
        // trial caps onto a single student view immediately. The list is
        // ordinal-stable per the existing convention (primary = 0).
        if (_linkedStudents.Count == 0)
        {
            _linkedStudents.Add(new LinkedStudent(
                StudentSubjectIdEncrypted: e.PrimaryStudentSubjectIdEncrypted,
                Ordinal: 0,
                Tier: SubscriptionTier.Unsubscribed,
                LinkedAt: e.TrialStartedAt));
        }
    }

    internal void Apply(TrialConverted_V1 _)
    {
        // Marker-only — Status stays Trialing here. The SubscriptionActivated_V1
        // that follows in the same stream flips Status to Active. We do NOT
        // wipe the trial fields — they remain on the state for analytics
        // (TrialStartedAt / TrialEndsAt let "days into trial" be re-derived
        // from replay if the marker is ever re-emitted).
    }

    internal void Apply(TrialExpired_V1 e)
    {
        Status = SubscriptionStatus.Expired;
        // TrialEndsAt was already set by TrialStarted_V1; ensure it isn't
        // moved backwards if the worker fires slightly past the pinned
        // boundary (idempotent on re-emission).
        if (!TrialEndsAt.HasValue || TrialEndsAt.Value < e.TrialEndedAt)
        {
            TrialEndsAt = e.TrialEndedAt;
        }
    }
}
