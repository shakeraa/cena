// =============================================================================
// Cena Platform — SupportEscalationPolicy (EPIC-PRR-J PRR-391/392/393)
//
// Decides whether a newly-submitted dispute should be routed to the
// elevated-priority support queue (vs. the standard SME review queue).
//
// v1 policy (subject to calibration after first 4 weeks of dispute data):
//   - A student with >= 2 PRIOR disputes submitted within the last 7 days
//     gets their next dispute auto-escalated.
//   - A student whose Premium tier is active AND who has at least 1 prior
//     dispute this month gets escalated (Premium = priority support SLA).
//   - Students with only "Other"-category disputes don't escalate (those
//     are signal-noise; SMEs batch-review separately).
//
// Decision is a pure function over (prior-dispute history + current tier).
// No side effects — the dispute service records the outcome into the new
// dispute row's Status or a new "priority" tag field.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Escalation verdict + reason code for observability.</summary>
public sealed record SupportEscalationDecision(bool Escalate, string Reason);

public interface ISupportEscalationPolicy
{
    /// <summary>
    /// Decide whether the pending dispute should be priority-routed.
    /// Inputs: prior disputes for this student, current tier, submission time.
    /// </summary>
    SupportEscalationDecision Decide(
        IReadOnlyList<DiagnosticDisputeView> priorDisputes,
        DisputeReason pendingReason,
        SubscriptionTier tier,
        DateTimeOffset submittedAt);
}

public sealed class SupportEscalationPolicy : ISupportEscalationPolicy
{
    /// <summary>Rolling-window size for "recent" disputes.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);

    /// <summary>Per-window prior-dispute count that triggers escalation.</summary>
    public const int PersistentDisputeCount = 2;

    public SupportEscalationDecision Decide(
        IReadOnlyList<DiagnosticDisputeView> priorDisputes,
        DisputeReason pendingReason,
        SubscriptionTier tier,
        DateTimeOffset submittedAt)
    {
        ArgumentNullException.ThrowIfNull(priorDisputes);

        // Filter: ignore "Other" noise-disputes for both the count AND the
        // pending one; Other disputes never escalate.
        if (pendingReason is DisputeReason.Other)
        {
            return new SupportEscalationDecision(false, "pending_is_other");
        }

        var signalPriors = priorDisputes
            .Where(d => d.Reason != DisputeReason.Other)
            .ToList();

        // Rule 1: persistent-dispute threshold in the recent window.
        var windowStart = submittedAt - RecentWindow;
        var recentCount = signalPriors.Count(d => d.SubmittedAt >= windowStart);
        if (recentCount >= PersistentDisputeCount)
        {
            return new SupportEscalationDecision(true, "persistent_disputes_in_window");
        }

        // Rule 2: Premium tier with any prior this month.
        if (tier is SubscriptionTier.Premium)
        {
            var monthStart = new DateTimeOffset(submittedAt.Year, submittedAt.Month, 1, 0, 0, 0, TimeSpan.Zero);
            if (signalPriors.Any(d => d.SubmittedAt >= monthStart))
            {
                return new SupportEscalationDecision(true, "premium_tier_priority_sla");
            }
        }

        return new SupportEscalationDecision(false, "standard_queue");
    }
}
