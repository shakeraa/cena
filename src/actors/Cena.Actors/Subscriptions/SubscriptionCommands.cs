// =============================================================================
// Cena Platform — SubscriptionCommands (EPIC-PRR-I PRR-300, ADR-0057)
//
// Pure command validators. Each command is a static method that takes the
// current aggregate state + command inputs, validates business rules, and
// returns the event to append. Throws SubscriptionCommandException on rule
// violations — no silent-deny path (matches ConsentAuthorizationException).
//
// Business rules enforced here:
//   - Activation requires Unsubscribed status
//   - Tier upgrades: immediate; downgrades: end-of-cycle
//   - Siblings: only on active subscription; ordinal is computed (no
//     caller-picked ordinals)
//   - Cancellation: terminal; no re-activation on same stream
//   - Refund: only within 30 days of ActivatedAt (guarantee window)
//   - Renewal: only when Active or PastDue
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Thrown when a subscription command violates a business rule. No silent
/// deny — callers that encounter this exception have a programming or UX bug.
/// </summary>
public sealed class SubscriptionCommandException : Exception
{
    public SubscriptionCommandException(string message) : base(message) { }
}

/// <summary>Pure command validators. Each static method is idempotent given equal inputs.</summary>
public static class SubscriptionCommands
{
    /// <summary>
    /// Validate and produce an activation event. Reject if the state is not
    /// in the Unsubscribed lifecycle.
    /// </summary>
    public static SubscriptionActivated_V1 Activate(
        SubscriptionState currentState,
        string parentSubjectIdEncrypted,
        string primaryStudentSubjectIdEncrypted,
        SubscriptionTier tier,
        BillingCycle cycle,
        string paymentTransactionIdEncrypted,
        DateTimeOffset activatedAt)
    {
        if (currentState.Status != SubscriptionStatus.Unsubscribed)
        {
            throw new SubscriptionCommandException(
                $"Cannot activate; subscription is already in status {currentState.Status}. " +
                "Terminal states require a new stream.");
        }
        if (tier == SubscriptionTier.Unsubscribed)
        {
            throw new SubscriptionCommandException(
                "Cannot activate into the Unsubscribed tier; pick a retail or school tier.");
        }
        if (cycle == BillingCycle.None)
        {
            throw new SubscriptionCommandException(
                "Cannot activate with BillingCycle.None; pick Monthly or Annual.");
        }

        var definition = TierCatalog.Get(tier);
        var gross = cycle == BillingCycle.Annual
            ? definition.AnnualPrice
            : definition.MonthlyPrice;
        var renewsAt = ComputeNextRenewal(activatedAt, cycle);

        return new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
            PrimaryStudentSubjectIdEncrypted: primaryStudentSubjectIdEncrypted,
            Tier: tier,
            Cycle: cycle,
            GrossAmountAgorot: gross.Amount,
            PaymentTransactionIdEncrypted: paymentTransactionIdEncrypted,
            ActivatedAt: activatedAt,
            RenewsAt: renewsAt);
    }

    /// <summary>
    /// Change tier. Upgrades (moving to a higher-price tier) take effect
    /// immediately. Downgrades apply at next renewal (rule per PRR-310 session
    /// pinning + UX fairness). Returns the event with an appropriate
    /// <c>EffectiveAt</c>.
    /// </summary>
    public static TierChanged_V1 ChangeTier(
        SubscriptionState currentState,
        SubscriptionTier newTier,
        DateTimeOffset now)
    {
        if (currentState.Status != SubscriptionStatus.Active)
        {
            throw new SubscriptionCommandException(
                $"Cannot change tier from status {currentState.Status}; must be Active.");
        }
        if (newTier == currentState.CurrentTier)
        {
            throw new SubscriptionCommandException(
                $"Already on tier {newTier}; no change.");
        }
        if (newTier == SubscriptionTier.Unsubscribed)
        {
            throw new SubscriptionCommandException(
                "Use Cancel to end a subscription, not ChangeTier to Unsubscribed.");
        }

        var fromPrice = TierCatalog.Get(currentState.CurrentTier).MonthlyPrice.Amount;
        var toPrice = TierCatalog.Get(newTier).MonthlyPrice.Amount;
        var isUpgrade = toPrice > fromPrice;
        var effectiveAt = isUpgrade
            ? now
            : currentState.RenewsAt ?? throw new SubscriptionCommandException(
                "Downgrade requires a known next-renewal boundary.");

        return new TierChanged_V1(
            ParentSubjectIdEncrypted: RequireParentId(currentState),
            FromTier: currentState.CurrentTier,
            ToTier: newTier,
            ChangedAt: now,
            EffectiveAt: effectiveAt);
    }

    /// <summary>
    /// Link a sibling student. Ordinal is computed (next integer from the
    /// current linked-students list), so callers cannot pick ordinals.
    /// Tier of the sibling is <paramref name="siblingTier"/> (persona #7
    /// flexibility: sibling may be on a different tier than primary).
    /// </summary>
    public static SiblingEntitlementLinked_V1 LinkSibling(
        SubscriptionState currentState,
        string siblingStudentSubjectIdEncrypted,
        SubscriptionTier siblingTier,
        DateTimeOffset now)
    {
        if (currentState.Status != SubscriptionStatus.Active)
        {
            throw new SubscriptionCommandException(
                $"Cannot link sibling while status is {currentState.Status}; must be Active.");
        }
        if (!TierCatalog.Get(siblingTier).IsRetail)
        {
            throw new SubscriptionCommandException(
                $"Sibling tier must be retail; got {siblingTier}.");
        }
        if (currentState.LinkedStudents.Any(s =>
                s.StudentSubjectIdEncrypted == siblingStudentSubjectIdEncrypted))
        {
            throw new SubscriptionCommandException(
                "Sibling is already linked to this subscription.");
        }

        int nextOrdinal = currentState.LinkedStudents.Count; // primary=0, first sibling=1, ...
        var siblingPrice = TierCatalog.SiblingMonthlyPrice(nextOrdinal);

        return new SiblingEntitlementLinked_V1(
            ParentSubjectIdEncrypted: RequireParentId(currentState),
            SiblingStudentSubjectIdEncrypted: siblingStudentSubjectIdEncrypted,
            SiblingOrdinal: nextOrdinal,
            Tier: siblingTier,
            SiblingMonthlyAgorot: siblingPrice.Amount,
            LinkedAt: now);
    }

    /// <summary>
    /// Cancel the subscription. Terminal. Refund policy enforced by
    /// <see cref="Refund"/>, not here.
    /// </summary>
    public static SubscriptionCancelled_V1 Cancel(
        SubscriptionState currentState,
        string reason,
        string initiator,
        DateTimeOffset now)
    {
        if (currentState.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Refunded)
        {
            throw new SubscriptionCommandException(
                $"Cannot cancel; already in terminal state {currentState.Status}.");
        }
        return new SubscriptionCancelled_V1(
            ParentSubjectIdEncrypted: RequireParentId(currentState),
            Reason: reason,
            Initiator: initiator,
            CancelledAt: now);
    }

    /// <summary>
    /// Refund the subscription. Only permitted inside the 30-day money-back
    /// guarantee window from the original activation. Abuse policy lives at
    /// the application layer (PRR-306) not here — the aggregate enforces the
    /// date boundary only.
    /// </summary>
    public static SubscriptionRefunded_V1 Refund(
        SubscriptionState currentState,
        long refundedAmountAgorot,
        string reason,
        DateTimeOffset now)
    {
        if (currentState.ActivatedAt is null)
        {
            throw new SubscriptionCommandException(
                "Cannot refund a subscription that was never activated.");
        }
        var windowEnd = currentState.ActivatedAt.Value.AddDays(30);
        if (now > windowEnd)
        {
            throw new SubscriptionCommandException(
                $"Refund window closed at {windowEnd:o}; now {now:o} is outside the 30-day guarantee.");
        }
        if (refundedAmountAgorot <= 0)
        {
            throw new SubscriptionCommandException(
                $"Refund amount must be positive; got {refundedAmountAgorot} agorot.");
        }
        return new SubscriptionRefunded_V1(
            ParentSubjectIdEncrypted: RequireParentId(currentState),
            RefundedAmountAgorot: refundedAmountAgorot,
            Reason: reason,
            RefundedAt: now);
    }

    /// <summary>Compute the next renewal boundary given activation time + cycle.</summary>
    public static DateTimeOffset ComputeNextRenewal(DateTimeOffset from, BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => from.AddMonths(1),
        BillingCycle.Annual => from.AddYears(1),
        _ => throw new SubscriptionCommandException(
            $"Cannot compute next renewal for cycle {cycle}."),
    };

    private static string RequireParentId(SubscriptionState state) =>
        state.ParentSubjectIdEncrypted ?? throw new SubscriptionCommandException(
            "Subscription state has no parent subject id — activate first.");
}
