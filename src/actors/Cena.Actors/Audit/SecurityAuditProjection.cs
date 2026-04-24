// =============================================================================
// FIND-data-024: Security Audit Projection
//
// Projects security-relevant events into AuditEventDocument for efficient
// querying and filtering. Only captures events that matter for compliance
// and forensics, not every domain event.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Marten;
using Marten.Events.Projections;

namespace Cena.Actors.Audit;

/// <summary>
/// Projection that transforms security-relevant domain events into audit documents.
/// </summary>
public class SecurityAuditProjection : EventProjection
{
    public SecurityAuditProjection()
    {
        // EventProjection routes via reflection on Project(...) overloads
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GDPR / Privacy Events
    // ═══════════════════════════════════════════════════════════════════════════

    public void Project(GdprDataExported_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:gdpr-export:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "gdpr_export",
            UserId = e.RequestedByAdminId,
            UserName = e.AdminEmail,
            Action = "gdpr_export",
            TargetType = "Student",
            TargetId = e.StudentId,
            Description = $"GDPR data export for student {e.StudentId}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }

    public void Project(GdprErasureExecuted_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:gdpr-erasure:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "gdpr_erasure",
            UserId = e.ExecutedByAdminId,
            UserName = e.AdminEmail,
            Action = "gdpr_erasure",
            TargetType = "Student",
            TargetId = e.StudentId,
            Description = $"GDPR right-to-erasure executed for student {e.StudentId}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Admin / Role Management Events
    // ═══════════════════════════════════════════════════════════════════════════

    public void Project(AdminRoleAssigned_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:role-assign:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "role_assigned",
            UserId = e.AssignedByAdminId,
            UserName = e.AdminEmail,
            Action = "assign_role",
            TargetType = "User",
            TargetId = e.TargetUserId,
            Description = $"Role {e.Role} assigned to user {e.TargetUserId}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }

    public void Project(AdminRoleRevoked_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:role-revoke:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "role_revoked",
            UserId = e.RevokedByAdminId,
            UserName = e.AdminEmail,
            Action = "revoke_role",
            TargetType = "User",
            TargetId = e.TargetUserId,
            Description = $"Role {e.Role} revoked from user {e.TargetUserId}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // User Management Events
    // ═══════════════════════════════════════════════════════════════════════════

    public void Project(UserSuspended_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:user-suspend:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "user_suspended",
            UserId = e.SuspendedByAdminId,
            UserName = e.AdminEmail,
            Action = "suspend_user",
            TargetType = "User",
            TargetId = e.TargetUserId,
            Description = $"User {e.TargetUserId} suspended. Reason: {e.Reason}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }

    public void Project(SessionForceRevoked_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:session-revoke:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "session_revoked",
            UserId = e.RevokedByAdminId,
            UserName = e.AdminEmail,
            Action = "revoke_session",
            TargetType = "Session",
            TargetId = e.TargetUserId,
            Description = $"Sessions revoked for user {e.TargetUserId}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Security Events
    // ═══════════════════════════════════════════════════════════════════════════

    public void Project(SecurityAlertTriggered_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:security-alert:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "security_alert",
            UserId = e.TriggeredByUserId ?? "system",
            Action = "security_alert",
            TargetType = "System",
            TargetId = e.AlertType,
            Description = $"Security alert: {e.AlertType}. {e.Description}",
            TenantId = e.TenantId,
            Success = true,
            IpAddress = e.IpAddress ?? "unknown",
            MetadataJson = $"{{\"severity\":\"{e.Severity}\"}}",
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Failed Login / Auth Events
    // ═══════════════════════════════════════════════════════════════════════════

    public void Project(FirebaseAuthFailed_V1 e, IDocumentOperations ops)
    {
        ops.Store(new AuditEventDocument
        {
            Id = $"audit:auth-failed:{e.EventId}",
            Timestamp = e.Timestamp,
            EventType = "auth_failed",
            UserId = e.AttemptedUserId ?? "unknown",
            Action = "firebase_auth_failed",
            TargetType = "Auth",
            TargetId = e.AttemptedEmail ?? "unknown",
            Description = $"Firebase auth failed: {e.FailureReason}",
            TenantId = e.TenantId,
            Success = false,
            ErrorMessage = e.FailureReason,
            IpAddress = e.IpAddress ?? "unknown",
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Event Records (these would normally be in the Events namespace)
// Adding here for compilation - in real implementation these exist elsewhere
// ═══════════════════════════════════════════════════════════════════════════

public record GdprDataExported_V1(
    Guid EventId,
    string StudentId,
    string RequestedByAdminId,
    string AdminEmail,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record GdprErasureExecuted_V1(
    Guid EventId,
    string StudentId,
    string ExecutedByAdminId,
    string AdminEmail,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record AdminRoleAssigned_V1(
    Guid EventId,
    string TargetUserId,
    string Role,
    string AssignedByAdminId,
    string AdminEmail,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record AdminRoleRevoked_V1(
    Guid EventId,
    string TargetUserId,
    string Role,
    string RevokedByAdminId,
    string AdminEmail,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record UserSuspended_V1(
    Guid EventId,
    string TargetUserId,
    string Reason,
    string SuspendedByAdminId,
    string AdminEmail,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record SessionForceRevoked_V1(
    Guid EventId,
    string TargetUserId,
    string RevokedByAdminId,
    string AdminEmail,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record SecurityAlertTriggered_V1(
    Guid EventId,
    string AlertType,
    string Severity,
    string Description,
    string? TriggeredByUserId,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);

public record FirebaseAuthFailed_V1(
    Guid EventId,
    string? AttemptedUserId,
    string? AttemptedEmail,
    string FailureReason,
    string TenantId,
    DateTimeOffset Timestamp,
    string? IpAddress = null);
