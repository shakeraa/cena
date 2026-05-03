// =============================================================================
// Cena Platform — RefundPolicy (EPIC-PRR-I PRR-306)
//
// Pure-function refund-eligibility + refund-amount calculator. No I/O, no
// clock beyond the `now` argument, no side effects. All policy knobs are
// configurable via RefundPolicyOptions so finance can tune the abuse
// thresholds without a code change.
//
// Why a dedicated policy object: the PRR-306 DoD covers three orthogonal
// concerns — the 30-day statutory window, pro-rata math on annual cycles,
// and the abuse rule. Interleaving them in the endpoint or the aggregate
// would make each one hard to test in isolation. Folding them into a pure
// function lets us cover every branch in unit tests and keeps the endpoint
// a thin composition over load → policy → gateway → event.
//
// Pro-rata rationale: Israeli Consumer Protection Law gives 14 days
// statutory. We promise 30 days as a trust marker (PRR-294). For annual
// cycles, refunding the FULL amount after 29 days of usage is the wrong
// fairness answer — the parent consumed ~1/12 of a year's value. Pro-rata
// = full - (daily_rate × days_used) credits the unused remainder and
// respects the money-back promise for the portion not consumed.
//
// Abuse rule rationale: a small minority abuse money-back guarantees by
// running a month of heavy usage then refunding. The thresholds here
// (500 photo diagnostics or 50 hint requests in the window) are well
// above any plausible legitimate single-student month (Premium cap is
// 300/mo, hint cap 500/mo *aggregate* — but across a household the
// aggregate in 30 days would approach these numbers only under
// systematic use-then-refund gaming). Thresholds are public in
// pricing/terms so denials are never a surprise.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Knobs for <see cref="RefundPolicy"/>. All defaults are PRR-306 DoD values.</summary>
public sealed record RefundPolicyOptions(
    int GuaranteeWindowDays = 30,
    long MaxDiagnosticUploadsInWindow = 500,
    long MaxHintRequestsInWindow = 50,
    int AnnualPeriodDays = 365)
{
    /// <summary>Default PRR-306 options — do not mutate; create a new record to tune.</summary>
    public static readonly RefundPolicyOptions Default = new();
}

/// <summary>Terminal decision from <see cref="RefundPolicy"/>.</summary>
/// <param name="Allowed">True when the request clears both window and abuse checks.</param>
/// <param name="RefundAmountAgorot">Gross amount (VAT-inclusive) to refund. Positive when allowed; 0 when denied.</param>
/// <param name="DenialReason">
/// Stable machine-readable code when denied: <c>"never_activated" | "outside_window" |
/// "abuse_diagnostic_uploads" | "abuse_hint_requests"</c>. Null when allowed.
/// </param>
public sealed record RefundDecision(
    bool Allowed,
    long RefundAmountAgorot,
    string? DenialReason)
{
    /// <summary>Build an allow decision with the computed refund amount.</summary>
    public static RefundDecision Allow(long refundAmountAgorot) =>
        new(true, refundAmountAgorot, null);

    /// <summary>Build a deny decision with a stable machine-readable reason.</summary>
    public static RefundDecision Deny(string reason) =>
        new(false, 0, reason);
}

/// <summary>
/// Pure refund-eligibility and pro-rata calculator. Called from the
/// <c>RefundService</c>; has no state of its own. Every knob is in
/// <see cref="RefundPolicyOptions"/>.
/// </summary>
public static class RefundPolicy
{
    /// <summary>
    /// Evaluate a refund request. Returns an <see cref="RefundDecision"/>
    /// that the caller uses to either (a) call the gateway and emit
    /// <c>SubscriptionRefunded_V1</c>, or (b) reject with the denial reason.
    /// </summary>
    /// <param name="state">The loaded subscription aggregate state.</param>
    /// <param name="now">Wall-clock of the request.</param>
    /// <param name="fullChargeAmountAgorot">
    /// What the parent was originally charged for the current cycle
    /// (monthly price or annual price — ALWAYS VAT-inclusive agorot).
    /// Required even for monthly because we can't assume a stable tier
    /// price across tier changes during the window.
    /// </param>
    /// <param name="usedDiagnosticUploads">Diagnostic uploads across all
    /// linked students counted from the activation timestamp forward.</param>
    /// <param name="usedHintRequests">Hint requests across all linked
    /// students counted from the activation timestamp forward.</param>
    /// <param name="options">Policy knobs; defaults to <see cref="RefundPolicyOptions.Default"/>.</param>
    public static RefundDecision Evaluate(
        SubscriptionState state,
        DateTimeOffset now,
        long fullChargeAmountAgorot,
        long usedDiagnosticUploads,
        long usedHintRequests,
        RefundPolicyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        options ??= RefundPolicyOptions.Default;

        // Eligibility #1: never activated → cannot refund.
        if (state.ActivatedAt is null)
        {
            return RefundDecision.Deny("never_activated");
        }
        var activatedAt = state.ActivatedAt.Value;

        // Eligibility #2: within the 30-day money-back window.
        var windowEnd = activatedAt.AddDays(options.GuaranteeWindowDays);
        if (now > windowEnd)
        {
            return RefundDecision.Deny("outside_window");
        }

        // Abuse rule: above the threshold is a denial. Surfaced with a
        // stable code per denial dimension so the UI can render a precise
        // honest reason ("you used 604 photo diagnostics this month; the
        // money-back guarantee excludes accounts exceeding 500") instead
        // of a generic "request denied".
        if (usedDiagnosticUploads > options.MaxDiagnosticUploadsInWindow)
        {
            return RefundDecision.Deny("abuse_diagnostic_uploads");
        }
        if (usedHintRequests > options.MaxHintRequestsInWindow)
        {
            return RefundDecision.Deny("abuse_hint_requests");
        }

        // Amount: monthly = full charge; annual = charge − (daily rate ×
        // days used), floor 0. Floor-0 is defensive — if the caller ever
        // passes an activation in the future (clock skew / test), we don't
        // return a negative refund. The daily rate is computed as integer
        // agorot per day using truncating division so the refund sum is
        // deterministic and never exceeds the charge.
        var amount = state.CurrentCycle switch
        {
            BillingCycle.Monthly => fullChargeAmountAgorot,
            BillingCycle.Annual => ComputeAnnualProRata(
                fullChargeAmountAgorot, activatedAt, now, options.AnnualPeriodDays),
            _ => fullChargeAmountAgorot,
        };

        return RefundDecision.Allow(amount);
    }

    /// <summary>
    /// Pro-rata refund for an annual cycle: <c>charge − (daily × days_used)</c>
    /// where <c>daily = charge / period_days</c>. Integer-agorot truncation
    /// means the refund is always ≤ the original charge; the unrefunded
    /// remainder (at most N agorot) stays with us — acceptable per finance
    /// because it's ≤ ₪0.N on a ₪X00 annual charge.
    /// </summary>
    public static long ComputeAnnualProRata(
        long fullChargeAmountAgorot,
        DateTimeOffset activatedAt,
        DateTimeOffset now,
        int periodDays)
    {
        if (fullChargeAmountAgorot <= 0) return 0;
        if (periodDays <= 0) return fullChargeAmountAgorot;

        var daysUsed = Math.Max(0, (int)(now - activatedAt).TotalDays);
        if (daysUsed >= periodDays) return 0;

        var dailyRateAgorot = fullChargeAmountAgorot / periodDays;
        var consumed = dailyRateAgorot * daysUsed;
        var refund = fullChargeAmountAgorot - consumed;
        return refund < 0 ? 0 : refund;
    }
}
