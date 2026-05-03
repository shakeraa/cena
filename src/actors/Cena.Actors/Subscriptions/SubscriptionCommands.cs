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
    /// Validate and produce an activation event. Allowed from
    /// <see cref="SubscriptionStatus.Unsubscribed"/> (first activation),
    /// <see cref="SubscriptionStatus.Trialing"/> (paid conversion — caller
    /// SHOULD also append <see cref="TrialConverted_V1"/> as a marker
    /// before calling this), and <see cref="SubscriptionStatus.Expired"/>
    /// (re-purchase after a non-converting trial — design §3 Expired→Active
    /// transition; no second trial is permitted on the stream).
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
        if (currentState.Status is not (SubscriptionStatus.Unsubscribed
            or SubscriptionStatus.Trialing
            or SubscriptionStatus.Expired))
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
    /// Unlink a sibling and compute the pro-rata credit for the unused
    /// remainder of the current billing cycle. Credit = sibling_price ×
    /// (days_remaining / cycle_days), floored at 0. This is the mirror of
    /// <see cref="LinkSibling"/> — every new sibling charged on the next
    /// invoice at <c>SiblingMonthlyPrice(ordinal)</c> can be unlinked
    /// with the portion not consumed credited back.
    /// </summary>
    /// <remarks>
    /// Ordinal-stability after an unlink is intentionally out of scope
    /// here — the LinkedStudents list keeps its historical ordering so
    /// "a student that was ever ordinal 1 keeps its discount even if
    /// another sibling is added/removed later" (SubscriptionState.cs
    /// header). The price for this particular sibling at time of
    /// unlink is derived from the ordinal it currently carries on the
    /// aggregate, which may be higher than ordinal 1 if earlier siblings
    /// have already been unlinked — the invoice reflects reality.
    /// </remarks>
    public static SiblingEntitlementUnlinked_V1 UnlinkSibling(
        SubscriptionState currentState,
        string siblingStudentSubjectIdEncrypted,
        DateTimeOffset now)
    {
        if (currentState.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Refunded)
        {
            throw new SubscriptionCommandException(
                $"Cannot unlink sibling; subscription is {currentState.Status}.");
        }
        var sibling = currentState.LinkedStudents
            .FirstOrDefault(s =>
                s.StudentSubjectIdEncrypted == siblingStudentSubjectIdEncrypted);
        if (sibling is null)
        {
            throw new SubscriptionCommandException(
                "Sibling is not linked to this subscription.");
        }
        if (sibling.Ordinal == 0)
        {
            throw new SubscriptionCommandException(
                "Primary student (ordinal 0) cannot be unlinked as a sibling; "
                + "cancel the subscription instead.");
        }

        var siblingPrice = TierCatalog.SiblingMonthlyPrice(sibling.Ordinal);
        long proRataCreditAgorot = ComputeSiblingProRataCredit(
            siblingPrice.Amount, currentState, now);

        return new SiblingEntitlementUnlinked_V1(
            ParentSubjectIdEncrypted: RequireParentId(currentState),
            SiblingStudentSubjectIdEncrypted: siblingStudentSubjectIdEncrypted,
            ProRataCreditAgorot: proRataCreditAgorot,
            UnlinkedAt: now);
    }

    /// <summary>
    /// Pure pro-rata helper. <c>credit = monthly_price × daysRemaining
    /// / cycleDays</c>. Integer-agorot truncation so the credit is
    /// deterministic and can never exceed the monthly charge. Returns 0
    /// when the cycle has already fully elapsed or RenewsAt is missing.
    /// Annual cycles use the monthly price (sibling is billed monthly
    /// regardless of parent's cycle — one sibling invoice per month,
    /// consolidated with the parent's renewal).
    /// </summary>
    public static long ComputeSiblingProRataCredit(
        long siblingMonthlyAgorot,
        SubscriptionState state,
        DateTimeOffset now)
    {
        if (siblingMonthlyAgorot <= 0) return 0;
        if (state.RenewsAt is null) return 0;
        var renewsAt = state.RenewsAt.Value;
        if (now >= renewsAt) return 0;

        // Cycle boundary for the sibling credit denominator is always the
        // monthly cadence, so the remainder computation uses the NEXT
        // monthly boundary regardless of whether the parent is on a
        // monthly or annual cycle. For monthly parent cycles, RenewsAt IS
        // the next boundary. For annual, the sibling's renewal is monthly
        // within the annual term — we take the nearest month boundary
        // from ActivatedAt forward.
        DateTimeOffset cycleStart;
        DateTimeOffset cycleEnd;
        switch (state.CurrentCycle)
        {
            case BillingCycle.Monthly:
                // Current cycle runs renewsAt-30 → renewsAt.
                cycleStart = renewsAt.AddDays(-30);
                cycleEnd = renewsAt;
                break;
            case BillingCycle.Annual:
                // Walk monthly anchors from ActivatedAt to find the
                // current [cycleStart, cycleEnd) that contains `now`.
                if (state.ActivatedAt is null) return 0;
                cycleStart = state.ActivatedAt.Value;
                while (cycleStart.AddMonths(1) <= now)
                {
                    cycleStart = cycleStart.AddMonths(1);
                }
                cycleEnd = cycleStart.AddMonths(1);
                break;
            default:
                return 0;
        }

        var totalSeconds = (cycleEnd - cycleStart).TotalSeconds;
        if (totalSeconds <= 0) return 0;
        var remainingSeconds = (cycleEnd - now).TotalSeconds;
        if (remainingSeconds <= 0) return 0;

        // Integer-agorot truncating math: floor((price × remaining) / total).
        var credit = (long)((siblingMonthlyAgorot * remainingSeconds) / totalSeconds);
        if (credit < 0) return 0;
        if (credit > siblingMonthlyAgorot) return siblingMonthlyAgorot;
        return credit;
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

    // ----- Trial commands (design §3, task body item 4) ------------------

    /// <summary>
    /// Start a trial. Allowed only from <see cref="SubscriptionStatus.Unsubscribed"/>
    /// — once a stream has trialled, it cannot trial again (abuse defense
    /// per design §5.7; the fingerprint ledger and admin-override paths
    /// live in sibling tasks). Validates that the caps snapshot has at
    /// least one non-zero knob; rejects with <c>trial_not_offered</c>
    /// when the platform-wide allotment is fully zero. Validates that
    /// <paramref name="trialEndsAt"/> is strictly later than
    /// <paramref name="trialStartedAt"/> when the duration knob is
    /// non-zero; equality is allowed only on cap-only trials (duration
    /// knob = 0).
    /// </summary>
    /// <param name="currentState">The aggregate's current state.</param>
    /// <param name="parentSubjectIdEncrypted">Encrypted parent subject id.</param>
    /// <param name="primaryStudentSubjectIdEncrypted">Encrypted primary student id.</param>
    /// <param name="trialKind">Origin of the trial (self-pay, parent-pay, institute-code).</param>
    /// <param name="trialStartedAt">UTC start instant.</param>
    /// <param name="trialEndsAt">UTC end instant. Equals <paramref name="trialStartedAt"/> for cap-only trials.</param>
    /// <param name="fingerprintHash">SHA-256 digest of Stripe card.fingerprint, or empty string for InstituteCode trials.</param>
    /// <param name="experimentVariantId">Pricing-experiment variant locked at trial-start (design §5.21). Empty defaults to <c>v1-baseline</c>.</param>
    /// <param name="capsSnapshot">Caps pinned from <see cref="TrialAllotmentConfig"/>.</param>
    public static TrialStarted_V1 StartTrial(
        SubscriptionState currentState,
        string parentSubjectIdEncrypted,
        string primaryStudentSubjectIdEncrypted,
        TrialKind trialKind,
        DateTimeOffset trialStartedAt,
        DateTimeOffset trialEndsAt,
        string fingerprintHash,
        string experimentVariantId,
        TrialCapsSnapshot capsSnapshot)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(capsSnapshot);

        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            throw new SubscriptionCommandException(
                "Parent subject id (encrypted) is required to start a trial.");
        }
        if (string.IsNullOrWhiteSpace(primaryStudentSubjectIdEncrypted))
        {
            throw new SubscriptionCommandException(
                "Primary student subject id (encrypted) is required to start a trial.");
        }

        if (currentState.Status != SubscriptionStatus.Unsubscribed)
        {
            throw new SubscriptionCommandException(
                $"Cannot start trial; subscription is in status {currentState.Status}. " +
                "Trials are allowed only from Unsubscribed (no second-trial path on this stream).");
        }

        if (!capsSnapshot.HasAnyAllotment)
        {
            // Mirrors the §11.2 410 trial_not_offered case at the domain
            // boundary so a caller that bypasses the endpoint validator
            // still cannot smuggle an empty trial through.
            throw new SubscriptionCommandException("trial_not_offered");
        }

        if (capsSnapshot.TrialDurationDays > 0 && trialEndsAt <= trialStartedAt)
        {
            throw new SubscriptionCommandException(
                $"trialEndsAt ({trialEndsAt:o}) must be strictly later than trialStartedAt " +
                $"({trialStartedAt:o}) when TrialDurationDays > 0 is set on the caps snapshot.");
        }
        if (capsSnapshot.TrialDurationDays == 0 && trialEndsAt != trialStartedAt)
        {
            throw new SubscriptionCommandException(
                "Cap-only trial (TrialDurationDays = 0) requires trialEndsAt == trialStartedAt; " +
                "the daemon never expires this trial on calendar grounds.");
        }

        // Empty fingerprintHash is legitimate ONLY for InstituteCode trials.
        // SelfPay and ParentPay both come from a SetupIntent and MUST carry
        // a digest. Defending here, not just at the endpoint, so a caller
        // bypassing the endpoint cannot smuggle an empty fingerprint.
        var fp = fingerprintHash ?? string.Empty;
        if (trialKind != TrialKind.InstituteCode && string.IsNullOrWhiteSpace(fp))
        {
            throw new SubscriptionCommandException(
                $"fingerprintHash is required for trial-kind {trialKind} (only InstituteCode " +
                "trials may omit the fingerprint).");
        }

        var variantId = string.IsNullOrWhiteSpace(experimentVariantId)
            ? "v1-baseline"
            : experimentVariantId;

        return new TrialStarted_V1(
            ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
            PrimaryStudentSubjectIdEncrypted: primaryStudentSubjectIdEncrypted,
            TrialKind: trialKind,
            TrialStartedAt: trialStartedAt,
            TrialEndsAt: trialEndsAt,
            FingerprintHash: fp,
            ExperimentVariantId: variantId,
            CapsSnapshot: capsSnapshot);
    }

    /// <summary>
    /// Convert a trialling stream to paid. Emits a marker event; the caller
    /// is responsible for then calling <see cref="Activate"/> to land the
    /// commercial state. Rejects when the stream is not currently
    /// <see cref="SubscriptionStatus.Trialing"/> or when the target tier is
    /// not retail (Basic/Plus/Premium).
    /// </summary>
    public static TrialConverted_V1 ConvertTrial(
        SubscriptionState currentState,
        SubscriptionTier convertedToTier,
        BillingCycle billingCycle,
        string paymentTransactionIdEncrypted,
        TrialUtilization utilizationAtConversion,
        DateTimeOffset convertedAt)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(utilizationAtConversion);

        if (currentState.Status != SubscriptionStatus.Trialing)
        {
            throw new SubscriptionCommandException(
                $"Cannot convert trial; subscription is in status {currentState.Status}. " +
                "ConvertTrial requires Trialing.");
        }
        if (!TierCatalog.Get(convertedToTier).IsRetail)
        {
            throw new SubscriptionCommandException(
                $"Cannot convert trial to non-retail tier {convertedToTier}; " +
                "only Basic/Plus/Premium are valid conversion targets.");
        }
        if (billingCycle == BillingCycle.None)
        {
            throw new SubscriptionCommandException(
                "ConvertTrial requires a concrete BillingCycle (Monthly or Annual).");
        }
        if (string.IsNullOrWhiteSpace(paymentTransactionIdEncrypted))
        {
            throw new SubscriptionCommandException(
                "ConvertTrial requires a non-empty payment transaction id (encrypted).");
        }

        // Days into trial: whole UTC calendar days between start and conversion.
        // Negative deltas (clock-skew or replayed event) clamp to 0 so analytics
        // never sees a negative bucket.
        var startedAt = currentState.TrialStartedAt
            ?? throw new SubscriptionCommandException(
                "Trial state is missing TrialStartedAt — stream is corrupt.");
        var daysIntoTrial = (int)Math.Max(0, (convertedAt.UtcDateTime.Date - startedAt.UtcDateTime.Date).TotalDays);

        var parentId = currentState.ParentSubjectIdEncrypted
            ?? throw new SubscriptionCommandException(
                "Trial state is missing ParentSubjectIdEncrypted — stream is corrupt.");
        var primaryId = currentState.LinkedStudents.FirstOrDefault()?.StudentSubjectIdEncrypted
            ?? throw new SubscriptionCommandException(
                "Trial state has no linked primary student — stream is corrupt.");

        return new TrialConverted_V1(
            ParentSubjectIdEncrypted: parentId,
            PrimaryStudentSubjectIdEncrypted: primaryId,
            ConvertedAt: convertedAt,
            DaysIntoTrial: daysIntoTrial,
            ConvertedToTier: convertedToTier,
            BillingCycle: billingCycle,
            PaymentTransactionIdEncrypted: paymentTransactionIdEncrypted,
            UtilizationAtConversion: utilizationAtConversion);
    }

    /// <summary>
    /// Expire a trialling stream on calendar timeout. Idempotent on
    /// re-call against an already-Expired stream (returns a fresh event
    /// with the original <c>TrialEndsAt</c>); the caller is expected to
    /// either skip the duplicate append or rely on the underlying
    /// store's idempotency guard. Rejects when called from any state
    /// that is neither Trialing nor Expired.
    /// </summary>
    public static TrialExpired_V1 ExpireTrial(
        SubscriptionState currentState,
        TrialUtilization utilization,
        DateTimeOffset trialEndedAt)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(utilization);

        if (currentState.Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Expired))
        {
            throw new SubscriptionCommandException(
                $"Cannot expire trial; subscription is in status {currentState.Status}. " +
                "ExpireTrial requires Trialing (or already-Expired for idempotent re-call).");
        }

        var parentId = currentState.ParentSubjectIdEncrypted
            ?? throw new SubscriptionCommandException(
                "Trial state is missing ParentSubjectIdEncrypted — stream is corrupt.");
        var primaryId = currentState.LinkedStudents.FirstOrDefault()?.StudentSubjectIdEncrypted
            ?? throw new SubscriptionCommandException(
                "Trial state has no linked primary student — stream is corrupt.");

        // Idempotent re-call: pin the event to the originally-pinned
        // TrialEndsAt rather than `now`, so replays produce identical
        // streams regardless of when ExpireTrial fires post-deadline.
        var endedAt = currentState.TrialEndsAt is { } pinned && pinned <= trialEndedAt
            ? pinned
            : trialEndedAt;

        return new TrialExpired_V1(
            ParentSubjectIdEncrypted: parentId,
            PrimaryStudentSubjectIdEncrypted: primaryId,
            TrialEndedAt: endedAt,
            Outcome: TrialExpired_V1.OutcomeExpired,
            Utilization: utilization);
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
