// =============================================================================
// Cena Platform — SubscriptionState (EPIC-PRR-I, ADR-0057)
//
// In-memory fold of subscription events. State is pure — no wall-clock,
// no persistence. Consumers ask IsActiveAsOf(now) for effective-state
// checks. Matches the ConsentState pattern (ADR-0042).
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

    /// <summary>
    /// True if the subscription is currently active as of <paramref name="now"/>.
    /// Not simply <see cref="Status"/> == Active — also checks <see cref="RenewsAt"/>.
    /// </summary>
    public bool IsActiveAsOf(DateTimeOffset now) =>
        Status == SubscriptionStatus.Active && RenewsAt.HasValue && RenewsAt.Value > now;

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
}
