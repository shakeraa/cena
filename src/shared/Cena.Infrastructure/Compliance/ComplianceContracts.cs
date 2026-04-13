// =============================================================================
// Cena Platform -- Compliance Contracts
// Types required by RetentionWorker and ErasureWorker that were previously
// defined only in test code. Now canonical in the Infrastructure assembly.
// =============================================================================

using Marten;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Categories of data subject to retention policies.
/// </summary>
public enum DataCategory
{
    StudentRecord,
    AuditLog,
    Analytics,
    Engagement
}

// DataRetentionPolicy is defined in DataRetentionPolicy.cs

/// <summary>
/// Service for querying tenant-specific retention overrides.
/// </summary>
public interface IRetentionPolicyService
{
    Task<TimeSpan> GetRetentionPeriodAsync(string tenantId, DataCategory category, CancellationToken ct = default);
    Task<TenantRetentionPolicy?> GetTenantPolicyAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Per-tenant retention policy overrides stored in Marten.
/// </summary>
public sealed class TenantRetentionPolicy
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public TimeSpan? StudentRecordRetentionOverride { get; set; }
    public TimeSpan? AuditLogRetentionOverride { get; set; }
    public TimeSpan? AnalyticsRetentionOverride { get; set; }
    public TimeSpan? EngagementRetentionOverride { get; set; }
}

/// <summary>
/// Summary of a single retention category's purge results.
/// </summary>
public sealed class RetentionCategorySummary
{
    public string Category { get; set; } = "";
    public TimeSpan RetentionPeriod { get; set; }
    public int ExpiredCount { get; set; }
    public int PurgedCount { get; set; }
}

/// <summary>
/// Status of a retention worker run.
/// </summary>
public enum RetentionRunStatus
{
    Running,
    Completed,
    Failed
}

/// <summary>
/// Audit document stored after each retention worker run.
/// </summary>
public sealed class RetentionRunHistory
{
    public Guid Id { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RetentionRunStatus Status { get; set; }
    public int DocumentsScanned { get; set; }
    public int DocumentsPurged { get; set; }
    public int ErasureRequestsAccelerated { get; set; }
    public string? ErrorMessage { get; set; }
    public List<RetentionCategorySummary> CategorySummaries { get; set; } = new();
}
