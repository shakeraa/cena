// =============================================================================
// Cena Platform -- System Monitoring DTOs
// ADM-008: System health, settings, and audit logging
// =============================================================================

namespace Cena.Admin.Api;

// System Health Dashboard
public sealed record SystemHealthResponse(
    DateTimeOffset Timestamp,
    IReadOnlyList<ServiceHealth> Services,
    IReadOnlyList<ActorSystemStatus> ActorSystems,
    IReadOnlyList<QueueDepth> QueueDepths,
    ErrorRateMetrics ErrorRates);

public sealed record ServiceHealth(
    string Name,
    string Status,  // healthy, degraded, unhealthy
    string? Version,
    TimeSpan? Latency,
    DateTimeOffset? LastChecked,
    string? ErrorMessage);

public sealed record ActorSystemStatus(
    string NodeId,
    string Status,
    int ActiveActors,
    int TotalMessages,
    float CpuUsagePercent,
    long MemoryUsageBytes);

public sealed record QueueDepth(
    string QueueName,
    int Depth,
    string Status,  // normal, warning, critical
    DateTimeOffset Timestamp);

public sealed record ErrorRateMetrics(
    float ErrorsPerMinute,
    float ErrorRatePercent,
    IReadOnlyList<ErrorPoint> Trend);

public sealed record ErrorPoint(
    string Timestamp,
    int ErrorCount,
    int RequestCount);

// Platform Settings
public sealed record PlatformSettingsResponse(
    OrganizationSettings Organization,
    FeatureFlagSettings Features,
    FocusEngineSettings FocusEngine,
    MasteryEngineSettings MasteryEngine);

public sealed record OrganizationSettings(
    string Name,
    string? LogoUrl,
    string Timezone,
    string DefaultLanguage,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record FeatureFlagSettings(
    bool EnableFocusTracking,
    bool EnableMicrobreaks,
    bool EnableMethodologySwitching,
    bool EnableOutreach,
    bool EnableOfflineMode,
    bool EnableParentDashboard);

public sealed record FocusEngineSettings(
    float DegradationThreshold,
    int MicrobreakIntervalMinutes,
    float MindWanderingThreshold,
    float FocusScoreBaseline);

public sealed record MasteryEngineSettings(
    float MasteryThreshold,
    float PrerequisiteGateThreshold,
    float DecayRatePerDay,
    int ReviewIntervalDays);

public sealed record UpdateSettingsRequest(
    OrganizationSettings? Organization,
    FeatureFlagSettings? Features,
    FocusEngineSettings? FocusEngine,
    MasteryEngineSettings? MasteryEngine);

// Audit Log
public sealed record AuditLogResponse(
    IReadOnlyList<AuditLogEntry> Entries,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record AuditLogEntry(
    string Id,
    DateTimeOffset Timestamp,
    string UserId,
    string UserName,
    string Action,
    string TargetType,
    string TargetId,
    string Details,
    string? IpAddress);

public sealed record AuditLogFilterRequest(
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    string? UserId,
    string? ActionType,
    string? TargetType);
