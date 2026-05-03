// =============================================================================
// Cena Platform — DiscountAssignmentService (per-user discount-codes feature)
//
// Single transactional boundary for the discount-assignment workflow:
//
//   IssueAsync     — admin issues. Validates, normalizes email, checks the
//                    one-active-per-email rule, mints the gateway coupon
//                    via IDiscountCouponProvider, appends DiscountIssued_V1.
//                    Triggers the discount_issued transactional email.
//   RevokeAsync    — admin revokes. Validates current state allows revoke,
//                    revokes at gateway via IDiscountCouponProvider, appends
//                    DiscountRevoked_V1. Idempotent on gateway side.
//   RedeemAsync    — webhook handler invokes when Stripe carries back a
//                    completed checkout that attached the promotion code.
//                    Appends DiscountRedeemed_V1.
//   FindActiveForEmailAsync — student-side / admin-search lookup.
//   ListByEmailAsync / ListRecentAsync — admin UI list view.
//
// The service is the only thing endpoints + workers depend on. The store +
// provider + dispatcher are encapsulated.
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>Discount-assignment workflow exception.</summary>
public sealed class DiscountAssignmentException : Exception
{
    /// <summary>Stable machine-readable reason code for API surfacing.</summary>
    public string ReasonCode { get; }

    /// <summary>Optional field name the violation refers to.</summary>
    public string? Field { get; }

    public DiscountAssignmentException(
        string reasonCode, string message, string? field = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        Field = field;
    }
}

/// <summary>Result of <see cref="DiscountAssignmentService.IssueAsync"/>.</summary>
/// <param name="AssignmentId">Cena-side assignment id.</param>
/// <param name="PromotionCodeString">
/// Human-readable promotion code the admin can communicate to the user as
/// a fallback. Pre-bound to the email by Stripe metadata so it's not
/// strictly needed in the auto-applied flow, but the admin endpoint
/// returns it so the admin UI can show "code: CENA-XXXXX" if they want.
/// </param>
public sealed record DiscountIssueResult(
    string AssignmentId,
    string PromotionCodeString);

/// <summary>
/// Email dispatcher seam for "discount issued" transactional emails. Distinct
/// method from <see cref="ISubscriptionLifecycleEmailDispatcher"/> because:
///   - the recipient is identified by email (not parent subject id) — the
///     user may not even be a Cena parent yet at issuance time;
///   - the payload includes the discount terms (amount, duration) so the
///     template can render without re-querying the store.
/// </summary>
public interface IDiscountIssuedEmailDispatcher
{
    /// <summary>
    /// Send the discount-issued transactional email to <paramref name="targetEmailNormalized"/>.
    /// Returns true on success; false on transient failure (caller may retry).
    /// </summary>
    Task<bool> SendDiscountIssuedAsync(
        string targetEmailNormalized,
        DiscountKind kind,
        int value,
        int durationMonths,
        string promotionCodeString,
        CancellationToken ct);
}

/// <summary>Null dispatcher for dev/test composition.</summary>
public sealed class NullDiscountIssuedEmailDispatcher : IDiscountIssuedEmailDispatcher
{
    public Task<bool> SendDiscountIssuedAsync(
        string targetEmailNormalized,
        DiscountKind kind,
        int value,
        int durationMonths,
        string promotionCodeString,
        CancellationToken ct) => Task.FromResult(true);
}

/// <summary>
/// Façade over <see cref="IDiscountAssignmentStore"/> +
/// <see cref="IDiscountCouponProvider"/> + <see cref="IDiscountIssuedEmailDispatcher"/>
/// that enforces the per-user discount workflow invariants.
/// </summary>
public sealed class DiscountAssignmentService
{
    private readonly IDiscountAssignmentStore _store;
    private readonly IDiscountCouponProvider _couponProvider;
    private readonly IDiscountIssuedEmailDispatcher _emailDispatcher;
    private readonly TimeProvider _clock;

    public DiscountAssignmentService(
        IDiscountAssignmentStore store,
        IDiscountCouponProvider couponProvider,
        IDiscountIssuedEmailDispatcher emailDispatcher,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _couponProvider = couponProvider ?? throw new ArgumentNullException(nameof(couponProvider));
        _emailDispatcher = emailDispatcher ?? throw new ArgumentNullException(nameof(emailDispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Maximum AmountOff value allowed (annual price of the most expensive
    /// retail tier). Pulled from <see cref="TierCatalog"/> so it stays in
    /// lockstep with the canonical pricing.
    /// </summary>
    public static long MaxAmountOffAgorot()
    {
        // Take the maximum across retail tiers' annual price.
        var max = 0L;
        foreach (var tier in new[] {
            SubscriptionTier.Basic, SubscriptionTier.Plus, SubscriptionTier.Premium })
        {
            var def = TierCatalog.Get(tier);
            if (!def.IsRetail) continue;
            if (def.AnnualPrice.Amount > max) max = def.AnnualPrice.Amount;
        }
        return max;
    }

    /// <summary>
    /// Issue a new discount assignment.
    /// </summary>
    /// <exception cref="DiscountAssignmentException">
    /// Thrown with ReasonCode:
    ///   "invalid_email_format"     — fails <see cref="EmailNormalizer.IsValidShape"/>
    ///   "discount_already_active"  — another Issued assignment exists for the email
    ///   "invalid_*"                — propagated from <see cref="DiscountAssignmentCommands.Issue"/>
    /// </exception>
    public async Task<DiscountIssueResult> IssueAsync(
        string rawTargetEmail,
        DiscountKind kind,
        int value,
        int durationMonths,
        string issuedByAdminSubjectIdEncrypted,
        string reason,
        CancellationToken ct)
    {
        if (!EmailNormalizer.IsValidShape(rawTargetEmail))
        {
            throw new DiscountAssignmentException(
                "invalid_email_format",
                "Target email must be a valid local@domain address.",
                "targetEmail");
        }
        var emailNormalized = EmailNormalizer.Normalize(rawTargetEmail);
        if (string.IsNullOrEmpty(emailNormalized))
        {
            throw new DiscountAssignmentException(
                "invalid_email_format",
                "Target email failed normalisation.",
                "targetEmail");
        }

        var existing = await _store.FindActiveByEmailAsync(emailNormalized, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new DiscountAssignmentException(
                "discount_already_active",
                $"An active discount already exists for {emailNormalized} " +
                $"(assignment {existing.AssignmentId}). Revoke it first if you want to issue a new one.",
                "targetEmail");
        }

        // Mint the assignment id BEFORE creating the coupon so the gateway
        // metadata can carry it. Server-minted ULID-shaped guid keeps the
        // stream key compact + URL-safe.
        var assignmentId = NewAssignmentId();

        // Create the coupon at the gateway. If this fails (network/Stripe
        // outage), no event is appended — caller can retry safely.
        var couponResult = await _couponProvider.CreateCouponAsync(
            new CouponCreateRequest(
                AssignmentId: assignmentId,
                TargetEmailNormalized: emailNormalized,
                DiscountKind: kind,
                DiscountValue: value,
                DurationMonths: durationMonths),
            ct).ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        DiscountIssued_V1 evt;
        try
        {
            evt = DiscountAssignmentCommands.Issue(
                assignmentId: assignmentId,
                targetEmailNormalized: emailNormalized,
                kind: kind,
                value: value,
                durationMonths: durationMonths,
                tierAnnualPriceAgorotForAmountOffCheck: MaxAmountOffAgorot(),
                issuedByAdminSubjectIdEncrypted: issuedByAdminSubjectIdEncrypted,
                reason: reason,
                stripeCouponId: couponResult.CouponId,
                stripePromotionCodeId: couponResult.PromotionCodeId,
                issuedAt: now);
        }
        catch (DiscountCommandException cmdEx)
        {
            // Validation rejected post-coupon-creation. Best effort to clean
            // up the orphan gateway coupon so we don't leak.
            try
            {
                await _couponProvider.RevokeCouponAsync(new CouponRevokeRequest(
                    AssignmentId: assignmentId,
                    CouponId: couponResult.CouponId,
                    PromotionCodeId: couponResult.PromotionCodeId), ct).ConfigureAwait(false);
            }
            catch
            {
                // Swallow — we surfaced the original validation error first.
                // The orphan coupon is harmless (no assignment references it).
            }
            throw new DiscountAssignmentException(
                cmdEx.ReasonCode, cmdEx.Message, cmdEx.Field);
        }

        await _store.AppendAsync(assignmentId, evt, ct).ConfigureAwait(false);

        // Fire-and-best-effort the transactional email. Failure here is
        // non-fatal — the discount is real even if the email bounced; the
        // admin sees the success toast + can communicate the discount
        // out-of-band. Logging is the dispatcher's responsibility.
        try
        {
            await _emailDispatcher.SendDiscountIssuedAsync(
                emailNormalized, kind, value, durationMonths,
                couponResult.PromotionCodeString, ct).ConfigureAwait(false);
        }
        catch
        {
            // ignored — see above
        }

        return new DiscountIssueResult(
            AssignmentId: assignmentId,
            PromotionCodeString: couponResult.PromotionCodeString);
    }

    /// <summary>
    /// Revoke an active discount assignment.
    /// </summary>
    /// <exception cref="DiscountAssignmentException">
    /// Thrown with ReasonCode:
    ///   "not_found"        — no stream for the given assignmentId
    ///   "already_redeemed" — assignment was already redeemed (cannot revoke)
    ///   "already_revoked"  — assignment was already revoked (idempotent rejection)
    /// </exception>
    public async Task RevokeAsync(
        string assignmentId,
        string revokedByAdminSubjectIdEncrypted,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new DiscountAssignmentException(
                "invalid_assignment_id", "Assignment id is required.");
        }

        var aggregate = await _store.LoadAsync(assignmentId, ct).ConfigureAwait(false);
        if (aggregate.State.Status == DiscountStatus.None)
        {
            throw new DiscountAssignmentException(
                "not_found", $"No discount assignment with id {assignmentId}.");
        }

        var now = _clock.GetUtcNow();
        DiscountRevoked_V1 evt;
        try
        {
            evt = DiscountAssignmentCommands.Revoke(
                aggregate.State, revokedByAdminSubjectIdEncrypted, reason, now);
        }
        catch (DiscountCommandException cmdEx)
        {
            throw new DiscountAssignmentException(
                cmdEx.ReasonCode, cmdEx.Message, cmdEx.Field);
        }

        // Revoke at gateway BEFORE appending so a gateway failure leaves
        // the discount live + retryable. Provider impls are idempotent so
        // a retry after a partial failure is safe.
        await _couponProvider.RevokeCouponAsync(new CouponRevokeRequest(
            AssignmentId: assignmentId,
            CouponId: aggregate.State.StripeCouponId,
            PromotionCodeId: aggregate.State.StripePromotionCodeId), ct)
            .ConfigureAwait(false);

        await _store.AppendAsync(assignmentId, evt, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mark a discount as redeemed. Called by the Stripe checkout-completed
    /// webhook handler when the session metadata carries our assignment id.
    /// Idempotent: a second call after Redeemed/Revoked is a no-op return
    /// (does NOT raise), since webhook redeliveries are normal.
    /// </summary>
    public async Task RedeemAsync(
        string assignmentId,
        string parentSubjectIdEncrypted,
        string stripeSubscriptionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId)) return;

        var aggregate = await _store.LoadAsync(assignmentId, ct).ConfigureAwait(false);
        if (aggregate.State.Status != DiscountStatus.Issued)
        {
            // Already terminal — webhook redelivery or admin revoke raced.
            // Drop silently rather than raising; Stripe retries are routine.
            return;
        }

        var now = _clock.GetUtcNow();
        DiscountRedeemed_V1 evt;
        try
        {
            evt = DiscountAssignmentCommands.Redeem(
                aggregate.State, parentSubjectIdEncrypted, stripeSubscriptionId, now);
        }
        catch (DiscountCommandException)
        {
            return; // race-loser — terminal state on retry
        }
        await _store.AppendAsync(assignmentId, evt, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Find the single active discount applicable to <paramref name="rawEmail"/>.
    /// Returns null when no active assignment matches. Email is normalized
    /// internally so the caller can pass whatever form they have.
    /// </summary>
    public async Task<DiscountAssignmentSummary?> FindActiveForEmailAsync(
        string rawEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawEmail)) return null;
        var emailNormalized = EmailNormalizer.Normalize(rawEmail);
        if (string.IsNullOrEmpty(emailNormalized)) return null;
        return await _store.FindActiveByEmailAsync(emailNormalized, ct).ConfigureAwait(false);
    }

    /// <summary>List all assignments for an email (any status).</summary>
    public async Task<IReadOnlyList<DiscountAssignmentSummary>> ListByEmailAsync(
        string rawEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
        {
            return Array.Empty<DiscountAssignmentSummary>();
        }
        var emailNormalized = EmailNormalizer.Normalize(rawEmail);
        if (string.IsNullOrEmpty(emailNormalized))
        {
            return Array.Empty<DiscountAssignmentSummary>();
        }
        return await _store.ListByEmailAsync(emailNormalized, ct).ConfigureAwait(false);
    }

    /// <summary>List the most recent <paramref name="limit"/> assignments across all emails.</summary>
    public Task<IReadOnlyList<DiscountAssignmentSummary>> ListRecentAsync(
        int limit, CancellationToken ct) => _store.ListRecentAsync(limit, ct);

    /// <summary>Mint a new assignment id (URL-safe, server-only).</summary>
    private static string NewAssignmentId()
    {
        // Compact-ish, URL-safe. 32-char hex GUID without dashes is plenty
        // for collision avoidance at expected volume; matches the shape of
        // ids elsewhere in the codebase.
        return "da_" + Guid.NewGuid().ToString("N");
    }
}
