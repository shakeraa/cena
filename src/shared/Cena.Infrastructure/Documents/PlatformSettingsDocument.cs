// =============================================================================
// Cena Platform -- Platform Settings Document (ADM-008)
// Singleton Marten doc holding platform-wide configuration.
// Replaces the static-field cache in SystemMonitoringService.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Singleton document (`Id = "platform"`) holding the whole platform
/// settings blob. Admins update it via PATCH; all hosts read the latest
/// version on demand. Flat schema so Marten can index simple fields.
/// </summary>
public class PlatformSettingsDocument
{
    public string Id { get; set; } = "platform";

    // ── Organization ──
    public string OrgName { get; set; } = "";
    public string? OrgLogoUrl { get; set; }
    public string OrgTimezone { get; set; } = "UTC";
    public string OrgDefaultLanguage { get; set; } = "en";
    public DateTimeOffset OrgUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string OrgUpdatedBy { get; set; } = "system";

    // ── Feature Flags ──
    public bool EnableFocusTracking { get; set; } = true;
    public bool EnableMicrobreaks { get; set; } = true;
    public bool EnableMethodologySwitching { get; set; } = true;
    public bool EnableOutreach { get; set; } = true;
    public bool EnableOfflineMode { get; set; } = true;
    public bool EnableParentDashboard { get; set; } = false;

    // ── Focus Engine ──
    public float FocusDegradationThreshold { get; set; } = 0.65f;
    public int FocusMicrobreakIntervalMinutes { get; set; } = 20;
    public float FocusMindWanderingThreshold { get; set; } = 0.35f;
    public float FocusScoreBaseline { get; set; } = 0.75f;

    // ── Mastery Engine ──
    public float MasteryThreshold { get; set; } = 0.85f;
    public float MasteryPrerequisiteGateThreshold { get; set; } = 0.95f;
    public float MasteryDecayRatePerDay { get; set; } = 0.02f;
    public int MasteryReviewIntervalDays { get; set; } = 7;
}
