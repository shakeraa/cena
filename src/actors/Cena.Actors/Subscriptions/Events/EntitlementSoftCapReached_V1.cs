// =============================================================================
// Cena Platform — EntitlementSoftCapReached_V1 (EPIC-PRR-I PRR-313, PRR-J PRR-401)
//
// Telemetry event — a student's usage hit a per-period soft cap. Fires the
// shipgate-compliant upsell UX (no scarcity language). Event is fan-out for
// analytics; does NOT itself change entitlement state.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// A soft cap was reached for a specific student. Cap type names come from
/// <see cref="CapType"/> constants.
/// </summary>
public sealed record EntitlementSoftCapReached_V1(
    string ParentSubjectIdEncrypted,
    string StudentSubjectIdEncrypted,
    string CapType,
    int UsageCount,
    int CapLimit,
    DateTimeOffset ReachedAt)
{
    /// <summary>Well-known cap-type strings used by the soft-cap telemetry.</summary>
    public static class CapTypes
    {
        /// <summary>Photo diagnostic soft cap (100/mo on Premium).</summary>
        public const string PhotoDiagnosticMonthly = "photo_diagnostic_monthly";

        /// <summary>Sonnet escalations per week (20 on Basic).</summary>
        public const string SonnetEscalationsWeekly = "sonnet_escalations_weekly";

        /// <summary>Hint requests per month.</summary>
        public const string HintRequestsMonthly = "hint_requests_monthly";
    }
}
