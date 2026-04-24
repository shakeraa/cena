// =============================================================================
// Cena Platform -- AdminUser Marten Document
// BKD-002: User management data model stored in PostgreSQL via Marten
// =============================================================================

using Cena.Infrastructure.Compliance;

namespace Cena.Infrastructure.Documents;

public enum CenaRole
{
    STUDENT,
    TEACHER,
    PARENT,
    MODERATOR,
    ADMIN,
    SUPER_ADMIN
}

public enum UserStatus
{
    Active,
    Suspended,
    Pending
}

/// <summary>
/// Marten document for admin-managed users.
/// Id = Firebase UID.
/// </summary>
public record AdminUser
{
    public string Id { get; init; } = string.Empty;     // Firebase UID

    [Pii(PiiLevel.High, "contact")]
    public string Email { get; init; } = string.Empty;

    [Pii(PiiLevel.Medium, "identity")]
    public string FullName { get; init; } = string.Empty;

    public CenaRole Role { get; init; }
    public UserStatus Status { get; init; } = UserStatus.Active;
    public string? School { get; init; }
    public string? Grade { get; init; }
    public string Locale { get; init; } = "en";
    public string? AvatarUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public bool SoftDeleted { get; set; }
}
