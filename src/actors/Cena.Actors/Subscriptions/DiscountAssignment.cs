// =============================================================================
// Cena Platform — DiscountAssignment aggregate (per-user discount-codes feature)
//
// Event-sourced aggregate for a single admin-issued personal discount.
// Stream key: `discount-{assignmentId}` where assignmentId is a server-
// minted ULID-like guid string at issuance time.
//
// State machine:
//   Issued ─[checkout completes]─→ Redeemed (terminal)
//   Issued ─[admin revoke]──────→ Revoked  (terminal)
//
// Re-issuing a discount for the same normalized email while a previous
// assignment is in the Issued state is rejected at the service layer
// (DiscountAssignmentService.IssueAsync → 409 discount_already_active).
// The aggregate itself only knows about its own stream — global "one-active-
// per-email" is enforced by the IDiscountAssignmentStore which indexes
// active assignments by normalized email.
//
// Why event-sourced + parent-keyed-aggregate-style? Because the DoD requires
// audit trail (issuer + reason + revoker + reason) and the lifecycle is
// monotonic — the event log IS the source of truth. The neighboring
// SubscriptionAggregate uses the same shape (ADR-0057); this aggregate
// follows the convention so review + maintenance stays cheap.
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>Lifecycle state of a <see cref="DiscountAssignment"/>.</summary>
public enum DiscountStatus
{
    /// <summary>Aggregate has no events yet — empty stream / unknown id.</summary>
    None = 0,

    /// <summary>Issued but not yet redeemed; revocable.</summary>
    Issued = 1,

    /// <summary>Redeemed at checkout (terminal).</summary>
    Redeemed = 2,

    /// <summary>Administratively revoked before redemption (terminal).</summary>
    Revoked = 3,
}

/// <summary>
/// Read-side state of a <see cref="DiscountAssignment"/>. Built by replaying
/// the event stream. All public fields are nullable until the corresponding
/// event has been applied.
/// </summary>
public sealed class DiscountAssignmentState
{
    /// <summary>Server-minted assignment id (Marten doc id + stream key suffix).</summary>
    public string AssignmentId { get; private set; } = "";

    /// <summary>Lower-cased Gmail-folded canonical email (per <see cref="EmailNormalizer.Normalize"/>).</summary>
    public string TargetEmailNormalized { get; private set; } = "";

    /// <summary>Discount kind: PercentOff or AmountOff.</summary>
    public DiscountKind Kind { get; private set; }

    /// <summary>Basis points (PercentOff) or agorot (AmountOff).</summary>
    public int Value { get; private set; }

    /// <summary>Number of paid invoices the discount applies to.</summary>
    public int DurationMonths { get; private set; }

    /// <summary>Encrypted issuer subject id (audit).</summary>
    public string IssuedByAdminSubjectIdEncrypted { get; private set; } = "";

    /// <summary>Free-text reason captured at issuance.</summary>
    public string Reason { get; private set; } = "";

    /// <summary>External Stripe Coupon id, empty for non-Stripe composition.</summary>
    public string StripeCouponId { get; private set; } = "";

    /// <summary>External Stripe Promotion Code id, empty for non-Stripe composition.</summary>
    public string StripePromotionCodeId { get; private set; } = "";

    /// <summary>Issuance timestamp.</summary>
    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>Redemption timestamp; null until Redeemed.</summary>
    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>Encrypted parent id captured at redemption; empty until Redeemed.</summary>
    public string RedeemedByParentSubjectIdEncrypted { get; private set; } = "";

    /// <summary>External Stripe subscription id captured at redemption.</summary>
    public RedemptionDetails Redemption { get; private set; } = RedemptionDetails.None;

    /// <summary>Revocation timestamp; null until Revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Encrypted revoker subject id; empty until Revoked.</summary>
    public string RevokedByAdminSubjectIdEncrypted { get; private set; } = "";

    /// <summary>Free-text reason captured at revocation.</summary>
    public string RevokeReason { get; private set; } = "";

    /// <summary>Current lifecycle state.</summary>
    public DiscountStatus Status { get; private set; } = DiscountStatus.None;

    /// <summary>Apply <see cref="DiscountIssued_V1"/>.</summary>
    public void Apply(DiscountIssued_V1 e)
    {
        AssignmentId = e.AssignmentId;
        TargetEmailNormalized = e.TargetEmailNormalized;
        Kind = e.DiscountKind;
        Value = e.DiscountValue;
        DurationMonths = e.DurationMonths;
        IssuedByAdminSubjectIdEncrypted = e.IssuedByAdminSubjectIdEncrypted;
        Reason = e.Reason ?? "";
        StripeCouponId = e.StripeCouponId ?? "";
        StripePromotionCodeId = e.StripePromotionCodeId ?? "";
        IssuedAt = e.IssuedAt;
        Status = DiscountStatus.Issued;
    }

    /// <summary>Apply <see cref="DiscountRedeemed_V1"/>.</summary>
    public void Apply(DiscountRedeemed_V1 e)
    {
        RedeemedAt = e.RedeemedAt;
        RedeemedByParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted ?? "";
        Redemption = new RedemptionDetails(
            ParentSubjectIdEncrypted: e.ParentSubjectIdEncrypted ?? "",
            StripeSubscriptionId: e.StripeSubscriptionId ?? "");
        Status = DiscountStatus.Redeemed;
    }

    /// <summary>Apply <see cref="DiscountRevoked_V1"/>.</summary>
    public void Apply(DiscountRevoked_V1 e)
    {
        RevokedAt = e.RevokedAt;
        RevokedByAdminSubjectIdEncrypted = e.RevokedByAdminSubjectIdEncrypted ?? "";
        RevokeReason = e.Reason ?? "";
        Status = DiscountStatus.Revoked;
    }
}

/// <summary>Captured details from <see cref="DiscountRedeemed_V1"/>.</summary>
public sealed record RedemptionDetails(
    string ParentSubjectIdEncrypted,
    string StripeSubscriptionId)
{
    /// <summary>Sentinel "no redemption yet" value.</summary>
    public static readonly RedemptionDetails None = new("", "");
}

/// <summary>
/// Aggregate root for a single discount assignment. Stream key:
/// <c>discount-{assignmentId}</c>. Mirrors the
/// <see cref="SubscriptionAggregate"/> shape — thin Apply-dispatch shell;
/// command validation lives in <see cref="DiscountAssignmentCommands"/>.
/// </summary>
public sealed class DiscountAssignment
{
    /// <summary>Conventional stream-key prefix.</summary>
    public const string StreamKeyPrefix = "discount-";

    /// <summary>Build the stream key for an assignment id.</summary>
    public static string StreamKey(string assignmentId)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new ArgumentException(
                "Assignment id must be non-empty for stream-key construction.",
                nameof(assignmentId));
        }
        return StreamKeyPrefix + assignmentId;
    }

    /// <summary>Backing state.</summary>
    public DiscountAssignmentState State { get; } = new();

    /// <summary>Apply an inbound domain event. Unknown events are silently ignored.</summary>
    public void Apply(object @event)
    {
        switch (@event)
        {
            case DiscountIssued_V1 issued: State.Apply(issued); break;
            case DiscountRedeemed_V1 redeemed: State.Apply(redeemed); break;
            case DiscountRevoked_V1 revoked: State.Apply(revoked); break;
        }
    }

    /// <summary>Replay events into a fresh aggregate.</summary>
    public static DiscountAssignment ReplayFrom(IEnumerable<object> events)
    {
        var aggregate = new DiscountAssignment();
        foreach (var evt in events) aggregate.Apply(evt);
        return aggregate;
    }
}

/// <summary>
/// Thrown when a <see cref="DiscountAssignmentCommands"/> validation rule
/// rejects an input. Carries a stable machine-readable reason code so the
/// admin endpoint can map to a structured 400/409 response.
/// </summary>
public sealed class DiscountCommandException : Exception
{
    /// <summary>Stable machine-readable reason code.</summary>
    public string ReasonCode { get; }

    /// <summary>Field name (when applicable) the violation refers to.</summary>
    public string? Field { get; }

    public DiscountCommandException(string reasonCode, string message, string? field = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        Field = field;
    }
}

/// <summary>
/// Pure validators for discount-assignment lifecycle transitions. Mirrors
/// the <see cref="SubscriptionCommands"/> pattern: validate input + current
/// state, return the would-be event. The store appends + applies.
/// </summary>
public static class DiscountAssignmentCommands
{
    /// <summary>Min basis points for PercentOff (0.01%).</summary>
    public const int MinPercentBasisPoints = 1;

    /// <summary>Max basis points for PercentOff (100%).</summary>
    public const int MaxPercentBasisPoints = 10_000;

    /// <summary>Min duration months.</summary>
    public const int MinDurationMonths = 1;

    /// <summary>Max duration months.</summary>
    public const int MaxDurationMonths = 36;

    /// <summary>Max free-text reason length to keep audit log readable.</summary>
    public const int MaxReasonLength = 1_024;

    /// <summary>
    /// Validate inputs and produce a <see cref="DiscountIssued_V1"/> event.
    /// Caller must already have the Stripe Coupon + Promotion Code created
    /// (or the in-memory provider's deterministic placeholders) and must
    /// have checked the one-active-per-email global rule.
    /// </summary>
    public static DiscountIssued_V1 Issue(
        string assignmentId,
        string targetEmailNormalized,
        DiscountKind kind,
        int value,
        int durationMonths,
        long tierAnnualPriceAgorotForAmountOffCheck,
        string issuedByAdminSubjectIdEncrypted,
        string reason,
        string stripeCouponId,
        string stripePromotionCodeId,
        DateTimeOffset issuedAt)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new DiscountCommandException(
                "invalid_assignment_id", "Assignment id is required.", "assignmentId");
        }
        if (string.IsNullOrWhiteSpace(targetEmailNormalized))
        {
            throw new DiscountCommandException(
                "invalid_email", "Target email is required.", "targetEmail");
        }
        if (kind != DiscountKind.PercentOff && kind != DiscountKind.AmountOff)
        {
            throw new DiscountCommandException(
                "invalid_discount_kind", "Discount kind must be PercentOff or AmountOff.", "discountKind");
        }
        if (kind == DiscountKind.PercentOff)
        {
            if (value < MinPercentBasisPoints || value > MaxPercentBasisPoints)
            {
                throw new DiscountCommandException(
                    "invalid_percent_value",
                    $"PercentOff value must be in basis points 1..{MaxPercentBasisPoints} (0.01%..100%).",
                    "discountValue");
            }
        }
        else // AmountOff
        {
            if (value <= 0)
            {
                throw new DiscountCommandException(
                    "invalid_amount_value",
                    "AmountOff value must be greater than 0 agorot.",
                    "discountValue");
            }
            if (tierAnnualPriceAgorotForAmountOffCheck > 0 &&
                value > tierAnnualPriceAgorotForAmountOffCheck)
            {
                throw new DiscountCommandException(
                    "amount_exceeds_tier_price",
                    $"AmountOff cannot exceed the highest annual tier price " +
                    $"({tierAnnualPriceAgorotForAmountOffCheck} agorot) — " +
                    "would produce a negative-cost subscription.",
                    "discountValue");
            }
        }
        if (durationMonths < MinDurationMonths || durationMonths > MaxDurationMonths)
        {
            throw new DiscountCommandException(
                "invalid_duration",
                $"DurationMonths must be {MinDurationMonths}..{MaxDurationMonths}.",
                "durationMonths");
        }
        if (string.IsNullOrWhiteSpace(issuedByAdminSubjectIdEncrypted))
        {
            throw new DiscountCommandException(
                "invalid_admin", "Issuer admin id is required.", "admin");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DiscountCommandException(
                "invalid_reason", "Reason is required for audit trail.", "reason");
        }
        if (reason.Length > MaxReasonLength)
        {
            throw new DiscountCommandException(
                "reason_too_long",
                $"Reason must be ≤ {MaxReasonLength} characters.",
                "reason");
        }

        return new DiscountIssued_V1(
            AssignmentId: assignmentId,
            TargetEmailNormalized: targetEmailNormalized,
            DiscountKind: kind,
            DiscountValue: value,
            DurationMonths: durationMonths,
            IssuedByAdminSubjectIdEncrypted: issuedByAdminSubjectIdEncrypted,
            Reason: reason,
            StripeCouponId: stripeCouponId ?? "",
            StripePromotionCodeId: stripePromotionCodeId ?? "",
            IssuedAt: issuedAt);
    }

    /// <summary>
    /// Validate that <paramref name="state"/> can transition to Redeemed and
    /// produce the corresponding event. Rejects post-terminal redemption.
    /// </summary>
    public static DiscountRedeemed_V1 Redeem(
        DiscountAssignmentState state,
        string parentSubjectIdEncrypted,
        string stripeSubscriptionId,
        DateTimeOffset redeemedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != DiscountStatus.Issued)
        {
            throw new DiscountCommandException(
                state.Status switch
                {
                    DiscountStatus.Redeemed => "already_redeemed",
                    DiscountStatus.Revoked => "already_revoked",
                    _ => "not_issued",
                },
                $"Cannot redeem assignment in state {state.Status}.");
        }
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            throw new DiscountCommandException(
                "invalid_parent", "Parent subject id is required for redemption.", "parent");
        }
        return new DiscountRedeemed_V1(
            AssignmentId: state.AssignmentId,
            TargetEmailNormalized: state.TargetEmailNormalized,
            ParentSubjectIdEncrypted: parentSubjectIdEncrypted,
            StripeSubscriptionId: stripeSubscriptionId ?? "",
            RedeemedAt: redeemedAt);
    }

    /// <summary>
    /// Validate that <paramref name="state"/> can transition to Revoked and
    /// produce the corresponding event. Rejects revocation after redemption
    /// (immutable post-redemption per task DoD).
    /// </summary>
    public static DiscountRevoked_V1 Revoke(
        DiscountAssignmentState state,
        string revokedByAdminSubjectIdEncrypted,
        string reason,
        DateTimeOffset revokedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != DiscountStatus.Issued)
        {
            throw new DiscountCommandException(
                state.Status switch
                {
                    DiscountStatus.Redeemed => "already_redeemed",
                    DiscountStatus.Revoked => "already_revoked",
                    _ => "not_issued",
                },
                $"Cannot revoke assignment in state {state.Status}.");
        }
        if (string.IsNullOrWhiteSpace(revokedByAdminSubjectIdEncrypted))
        {
            throw new DiscountCommandException(
                "invalid_admin", "Revoker admin id is required.", "admin");
        }
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? "admin_revoked" : reason.Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new DiscountCommandException(
                "reason_too_long",
                $"Reason must be ≤ {MaxReasonLength} characters.",
                "reason");
        }
        return new DiscountRevoked_V1(
            AssignmentId: state.AssignmentId,
            TargetEmailNormalized: state.TargetEmailNormalized,
            RevokedByAdminSubjectIdEncrypted: revokedByAdminSubjectIdEncrypted,
            Reason: trimmedReason,
            RevokedAt: revokedAt);
    }
}
