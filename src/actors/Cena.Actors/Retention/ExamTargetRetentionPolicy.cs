// =============================================================================
// Cena Platform — ExamTarget retention policy (prr-229, ADR-0050 §6)
//
// Codifies the 24-month post-archive retention rule + user-extendable
// opt-in to 60 months. This class is pure policy: given an
// ArchivedAtUtc + opt-in flag + "now", it returns whether the target is
// past its retention horizon and the horizon itself for audit.
//
// Anchors:
//   - 24 months default (PPL Amendment 13 purpose-limitation + GDPR
//     Art. 5(1)(e)). ADR-0050 §6 is the Cena-specific codification.
//   - 60 months maximum user-extended window. The student opts in on
//     their settings page ("Keep exam history for 5 years instead of 2").
//     Any extension beyond 60 months is clamped.
//   - Months are calendar months, not 30-day approximations — computed
//     via DateTimeOffset.AddMonths so "24 months after 2026-04-21 is
//     2028-04-21" holds regardless of month-length drift.
// =============================================================================

namespace Cena.Actors.Retention;

/// <summary>
/// Pure policy object for the prr-229 / ADR-0050 §6 retention rule.
/// </summary>
public sealed class ExamTargetRetentionPolicy
{
    /// <summary>Default retention window (ADR-0050 §6).</summary>
    public const int DefaultRetentionMonths = 24;

    /// <summary>Maximum user-extended window (ADR-0050 §6).</summary>
    public const int MaxExtendedRetentionMonths = 60;

    /// <summary>
    /// Notification lead-time for the "retention expiring soon" admin
    /// visibility surface (task body §Admin visibility: "within 60 days").
    /// </summary>
    public static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(60);

    /// <summary>
    /// Compute the retention horizon (the wall-clock at which the archived
    /// target's events become eligible for crypto-shred).
    /// </summary>
    /// <param name="archivedAtUtc">The target's <c>ArchivedAtUtc</c>.</param>
    /// <param name="extendedRetention">
    /// Whether the student opted in to the 60-month extension.
    /// </param>
    public static DateTimeOffset ComputeHorizon(
        DateTimeOffset archivedAtUtc,
        bool extendedRetention)
    {
        var months = extendedRetention
            ? MaxExtendedRetentionMonths
            : DefaultRetentionMonths;
        return archivedAtUtc.AddMonths(months);
    }

    /// <summary>
    /// Is the target past its retention horizon as of <paramref name="nowUtc"/>?
    /// </summary>
    public static bool IsBeyondRetention(
        DateTimeOffset archivedAtUtc,
        bool extendedRetention,
        DateTimeOffset nowUtc)
        => nowUtc >= ComputeHorizon(archivedAtUtc, extendedRetention);

    /// <summary>
    /// Is the target within <see cref="ExpiringSoonWindow"/> of its
    /// retention horizon? Used by the admin dashboard's
    /// "expiring soon" filter.
    /// </summary>
    public static bool IsExpiringSoon(
        DateTimeOffset archivedAtUtc,
        bool extendedRetention,
        DateTimeOffset nowUtc)
    {
        var horizon = ComputeHorizon(archivedAtUtc, extendedRetention);
        if (nowUtc >= horizon)
        {
            // Already expired — the retention worker should shred on its
            // next run; the expiring-soon surface is for UN-expired
            // targets only.
            return false;
        }
        return horizon - nowUtc <= ExpiringSoonWindow;
    }
}
