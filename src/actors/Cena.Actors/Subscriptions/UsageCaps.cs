// =============================================================================
// Cena Platform — UsageCaps (EPIC-PRR-I PRR-291, ADR-0057)
//
// Per-tier caps consumed by the LLM router (ADR-0026), diagnostic intake
// (EPIC-PRR-J), and hint-ladder. Values:
//   -1 = effectively unlimited (caller still applies soft-cap UX at high
//        thresholds to detect abuse)
//   >= 0 = hard cap for that counter over its canonical period
//
// Canonical periods:
//   SonnetEscalationsPerWeek: ISO-week
//   PhotoDiagnosticsPerMonth: calendar month aligned to billing anchor
//   HintRequestsPerMonth:     calendar month aligned to billing anchor
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Immutable per-tier usage caps. Consumed in-hot-path; do not mutate.
/// </summary>
/// <param name="SonnetEscalationsPerWeek">
/// Max high-tier LLM escalations per ISO-week. -1 = unlimited.
/// </param>
/// <param name="PhotoDiagnosticsPerMonth">
/// Max photo diagnostic uploads per month. -1 = unlimited. For Premium this
/// is the SOFT cap; <see cref="PhotoDiagnosticsHardCapPerMonth"/> is the hard.
/// </param>
/// <param name="PhotoDiagnosticsHardCapPerMonth">
/// Hard cap for photo diagnostics per month. -1 = none (matches the soft
/// cap). Used by EPIC-PRR-J PRR-402 hard-cap UX.
/// </param>
/// <param name="HintRequestsPerMonth">
/// Max hint-ladder requests per month. -1 = unlimited.
/// </param>
public sealed record UsageCaps(
    int SonnetEscalationsPerWeek,
    int PhotoDiagnosticsPerMonth,
    int PhotoDiagnosticsHardCapPerMonth,
    int HintRequestsPerMonth)
{
    /// <summary>Sentinel value meaning "no cap".</summary>
    public const int Unlimited = -1;
}

/// <summary>
/// Per-tier feature flags consumed by endpoint-level authorization
/// (<c>SkuFeatureAuthorizer</c>) and the parent dashboard visibility gate.
/// </summary>
/// <param name="ParentDashboard">Whether the parent dashboard is accessible.</param>
/// <param name="TutorHandoffPdf">Whether tutor-handoff PDF export is enabled.</param>
/// <param name="ArabicDashboard">
/// Whether the parent dashboard ships with full Arabic parity. At launch
/// Premium has this true (per persona #6 PRR-I §2 decision #3); Basic/Plus
/// false because they have no dashboard at all.
/// </param>
/// <param name="PrioritySupport">Whether priority support is entitled.</param>
/// <param name="ClassroomDashboard">School SKU only: teacher/classroom admin.</param>
/// <param name="TeacherAssignedPractice">School SKU only.</param>
/// <param name="Sso">School SKU only: enterprise SSO enabled.</param>
public sealed record TierFeatureFlags(
    bool ParentDashboard,
    bool TutorHandoffPdf,
    bool ArabicDashboard,
    bool PrioritySupport,
    bool ClassroomDashboard,
    bool TeacherAssignedPractice,
    bool Sso);
