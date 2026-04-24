// =============================================================================
// Cena Platform — MoneyBackGuaranteeWindow (EPIC-PRR-I PRR-294)
//
// Pure read-only "is this subscription still within the 30-day money-back
// window?" calculator. Used by the Student Billing UI to decide whether
// to surface the "Request refund" CTA, and by email templates to render
// "you have N days left" language honestly.
//
// Why a dedicated class (not a SubscriptionState extension method):
//   - SubscriptionState is an event-sourced fold — keeping it narrow to
//     event application + coarse predicates (IsActiveAsOf) matches the
//     ADR-0042 bounded-context pattern.
//   - The guarantee-window rule has a knob (window days) that should be
//     testable in isolation, and it composes with other rules (terminal
//     state, never-activated) that don't belong on the state itself.
//   - RefundPolicy.Evaluate is the authoritative refund decider but it
//     requires usage-count probes (diagnostic uploads + hint requests)
//     that a lightweight "is CTA visible?" check does not need. Asking
//     the full policy on every page render would be wasteful; this
//     window-only checker is the fast path.
//
// The 30-day window mirrors RefundPolicyOptions.GuaranteeWindowDays (PRR-306)
// so the two stay coherent: the CTA visibility and the eventual refund
// eligibility speak the same language about when the window closes.
//
// Honest-not-complimentary note (memory 2026-04-20): this checker does
// NOT surface abuse-denial reasons. A parent with 600 photo uploads in
// the window still sees the CTA because the CTA is a trust marker, not
// a pre-clearance. The actual refund request goes through RefundPolicy
// which can still deny on abuse grounds — denying silently by hiding
// the button would be the dark pattern (memory "Ship-gate banned terms"
// spirit, even though this isn't one of the named banned mechanics).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Stable reason code on the <see cref="MoneyBackGuaranteeWindowStatus"/>.
/// Machine-readable; the UI renders the locale-specific message.
/// </summary>
public static class MoneyBackGuaranteeWindowReason
{
    /// <summary>Subscription is active and within the 30-day window.</summary>
    public const string ActiveWithinWindow = "active_within_window";

    /// <summary>Window has elapsed; CTA hidden.</summary>
    public const string Expired = "expired";

    /// <summary>No activation event on the stream yet; CTA hidden.</summary>
    public const string NotActivated = "not_activated";

    /// <summary>
    /// Subscription is in a terminal state (Cancelled / Refunded); CTA hidden.
    /// The parent may no longer act on a money-back guarantee because the
    /// refund has already been processed or the subscription is terminated.
    /// </summary>
    public const string TerminalState = "terminal_state";

    /// <summary>
    /// Subscription is PastDue — payment failed during the window. The
    /// CTA is suppressed for PastDue because the refund flow requires a
    /// successful charge to reverse; a PastDue parent should resolve the
    /// payment first, after which the window (if still open) lights back up.
    /// </summary>
    public const string PastDue = "past_due";
}

/// <summary>
/// Result of a guarantee-window check. Suitable to return over the wire
/// without transformation — no PII, no internal ids.
/// </summary>
/// <param name="IsWithinWindow">True iff the CTA should be surfaced.</param>
/// <param name="DaysRemaining">
/// Whole days left until the window closes. Zero when outside the window
/// or for a non-active state. Equals <see cref="MoneyBackGuaranteeWindow.DefaultWindowDays"/>
/// at the exact activation instant.
/// </param>
/// <param name="WindowEndsAtUtc">
/// Absolute UTC instant the window closes. Null when the subscription was
/// never activated. Present even when <paramref name="IsWithinWindow"/> is
/// false so the UI can render "your window closed on X" truthfully.
/// </param>
/// <param name="Reason">One of <see cref="MoneyBackGuaranteeWindowReason"/>.</param>
public sealed record MoneyBackGuaranteeWindowStatus(
    bool IsWithinWindow,
    int DaysRemaining,
    DateTimeOffset? WindowEndsAtUtc,
    string Reason);

/// <summary>
/// Pure checker for the 30-day money-back guarantee window. No I/O,
/// no clock, no state of its own — caller passes <paramref name="now"/>.
/// </summary>
public static class MoneyBackGuaranteeWindow
{
    /// <summary>
    /// Default window length. Mirrors <see cref="RefundPolicyOptions.GuaranteeWindowDays"/>
    /// so CTA visibility and refund-policy eligibility stay coherent.
    /// </summary>
    public const int DefaultWindowDays = 30;

    /// <summary>
    /// Evaluate whether <paramref name="state"/> is inside the
    /// <paramref name="windowDays"/>-day guarantee window at <paramref name="now"/>.
    /// </summary>
    public static MoneyBackGuaranteeWindowStatus Evaluate(
        SubscriptionState state,
        DateTimeOffset now,
        int windowDays = DefaultWindowDays)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (windowDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowDays), windowDays,
                "Window must be at least 1 day.");
        }

        // Never activated → no window to speak of. UI hides the CTA.
        if (state.ActivatedAt is null)
        {
            return new MoneyBackGuaranteeWindowStatus(
                IsWithinWindow: false,
                DaysRemaining: 0,
                WindowEndsAtUtc: null,
                Reason: MoneyBackGuaranteeWindowReason.NotActivated);
        }

        var activatedAt = state.ActivatedAt.Value;
        var windowEndsAt = activatedAt.AddDays(windowDays);

        // Terminal states — refund already processed or subscription
        // terminated. No CTA, but report the historical window end so
        // the UI can show "your window ended on X".
        if (state.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Refunded)
        {
            return new MoneyBackGuaranteeWindowStatus(
                IsWithinWindow: false,
                DaysRemaining: 0,
                WindowEndsAtUtc: windowEndsAt,
                Reason: MoneyBackGuaranteeWindowReason.TerminalState);
        }

        // PastDue — suppress the CTA; parent must resolve payment first.
        if (state.Status == SubscriptionStatus.PastDue)
        {
            return new MoneyBackGuaranteeWindowStatus(
                IsWithinWindow: false,
                DaysRemaining: 0,
                WindowEndsAtUtc: windowEndsAt,
                Reason: MoneyBackGuaranteeWindowReason.PastDue);
        }

        // Active — test the window.
        if (now >= windowEndsAt)
        {
            return new MoneyBackGuaranteeWindowStatus(
                IsWithinWindow: false,
                DaysRemaining: 0,
                WindowEndsAtUtc: windowEndsAt,
                Reason: MoneyBackGuaranteeWindowReason.Expired);
        }

        // Whole days left. Round UP so "23 hours left" shows as "1 day
        // remaining" — the honest consumer-facing framing. Rounding down
        // would under-state the remaining time at the exact boundary.
        var remaining = windowEndsAt - now;
        var daysRemaining = (int)Math.Ceiling(remaining.TotalDays);
        if (daysRemaining < 1) daysRemaining = 1;  // guard numeric edge at sub-millisecond

        return new MoneyBackGuaranteeWindowStatus(
            IsWithinWindow: true,
            DaysRemaining: daysRemaining,
            WindowEndsAtUtc: windowEndsAt,
            Reason: MoneyBackGuaranteeWindowReason.ActiveWithinWindow);
    }
}
