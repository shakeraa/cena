// =============================================================================
// Cena Platform — StudentTrialConsumption (Phase 1D, trial-then-paywall §5.5)
//
// Per-student snapshot of trial-feature consumption. Drives the cap-hit
// decision in RequireEntitlementFilter (Phase 1D) and surfaces on the
// /api/me/entitlement read path so the SPA renders factual usage. Counters
// are monotonic during a trial — ResetAsync is called once at conversion
// or fresh-trial-start so the next trial cycle (admin-override path) gets
// a clean slate.
//
// DaysActive is derived from the set of distinct UTC calendar dates on
// which any feature was incremented; the store tracks the date set
// internally and exposes the count here.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Immutable snapshot of a student's trial consumption. Returned by
/// <see cref="IStudentTrialConsumptionStore.GetAsync"/>; consumed by the
/// resolver path that builds <c>TrialStateDto</c> and by
/// <c>SubscriptionCommands.ConvertTrial</c> as the analytics payload.
/// </summary>
/// <param name="TutorTurnsUsed">Tutor turns consumed during the current trial.</param>
/// <param name="PhotoDiagnosticsUsed">Photo diagnostic uploads consumed.</param>
/// <param name="SessionsStarted">Practice sessions started.</param>
/// <param name="DaysActive">
/// Distinct UTC calendar dates on which any feature was incremented.
/// Drives the §5.18 funnel-analytics field of the same name.
/// </param>
public sealed record StudentTrialConsumption(
    int TutorTurnsUsed,
    int PhotoDiagnosticsUsed,
    int SessionsStarted,
    int DaysActive)
{
    /// <summary>The all-zero snapshot — used as the "no consumption yet" default.</summary>
    public static StudentTrialConsumption Empty { get; } = new(0, 0, 0, 0);
}
