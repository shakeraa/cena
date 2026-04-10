// =============================================================================
// Cena Platform -- Admin User Management Contracts (DB-05)
// Request/response types for user CRUD endpoints.
// =============================================================================

namespace Cena.Api.Contracts.Admin.Users;

// ---- Requests ----

public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string Role,
    string? School,
    string? Grade,
    string Locale = "en",
    string? Password = null);

public sealed record UpdateUserRequest(
    string? FullName,
    string? Email,
    string? Role,
    string? School,
    string? Grade,
    string? Locale);

public sealed record SuspendUserRequest(string Reason);

public sealed record InviteUserRequest(string Email, string Role, string? School);

// ---- Responses ----

public sealed record AdminUserDto(
    string Id,
    string Email,
    string FullName,
    string Role,
    string Status,
    string? School,
    string? Grade,
    string Locale,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    string? SuspensionReason,
    DateTimeOffset? SuspendedAt);

public sealed record UserListResponse(
    IReadOnlyList<AdminUserDto> Users,
    int TotalUsers,
    int TotalPages,
    int Page);

public sealed record UserStatsResponse(
    int TotalUsers,
    int NewThisWeek,
    int ActiveToday,
    int PendingReview,
    Dictionary<string, int> ByRole);

public sealed record UserActivityEntry(
    DateTimeOffset Timestamp,
    string Action,
    string Description,
    Dictionary<string, object>? Metadata);

public sealed record BulkInviteResult(int Created, IReadOnlyList<BulkInviteFailure> Failed);
public sealed record BulkInviteFailure(string Email, string Error);

// ---- Sessions ----

public sealed record UserSessionDto(
    string SessionId,
    string Device,
    string Browser,
    string Ip,
    string? Location,
    DateTimeOffset LastActive,
    string Status);  // active, expired
