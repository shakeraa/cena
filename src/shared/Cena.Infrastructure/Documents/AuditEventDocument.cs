// =============================================================================
// FIND-data-024: Audit Event Document
//
// Dedicated document for security audit events.
// Captures who did what, when, from where, with full context for forensics.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Security audit event persisted for compliance and forensics.
/// </summary>
public class AuditEventDocument
{
    public string Id { get; set; } = "";
    
    /// <summary>When the event occurred (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>Event type/category for filtering.</summary>
    public string EventType { get; set; } = "";
    
    /// <summary>User ID who performed the action.</summary>
    public string UserId { get; set; } = "";
    
    /// <summary>Display name of the user.</summary>
    public string UserName { get; set; } = "";
    
    /// <summary>User's role (e.g., SUPER_ADMIN, ADMIN, STUDENT).</summary>
    public string UserRole { get; set; } = "";
    
    /// <summary>Tenant/school ID for tenant-scoped queries.</summary>
    public string TenantId { get; set; } = "";
    
    /// <summary>Action performed (e.g., "gdpr_export", "assign_role").</summary>
    public string Action { get; set; } = "";
    
    /// <summary>Type of resource affected (e.g., "Student", "Question").</summary>
    public string TargetType { get; set; } = "";
    
    /// <summary>ID of the resource affected.</summary>
    public string TargetId { get; set; } = "";
    
    /// <summary>Human-readable description.</summary>
    public string Description { get; set; } = "";
    
    /// <summary>Client IP address.</summary>
    public string IpAddress { get; set; } = "";
    
    /// <summary>User agent string.</summary>
    public string UserAgent { get; set; } = "";
    
    /// <summary>Whether the action succeeded.</summary>
    public bool Success { get; set; } = true;
    
    /// <summary>Error message if action failed.</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>Additional context as JSON.</summary>
    public string? MetadataJson { get; set; }
}

// Note: AuditLogFilterRequest, AuditLogEntry, and AuditLogResponse are defined in
// Cena.Api.Contracts.Admin.System namespace (SystemDtos.cs) to avoid duplication.
