// =============================================================================
// Cena Platform -- Admin Roles & Permissions Contracts (DB-05)
// Request/response types for role management endpoints.
// =============================================================================

namespace Cena.Api.Contracts.Admin.Roles;

// ---- Requests ----

public sealed record CreateRoleRequest(string Name, string Description, string? CopyFrom);

public sealed record UpdatePermissionsRequest(Dictionary<string, List<string>> Permissions);

public sealed record AssignRoleRequest(string Role);

// ---- Responses ----

public sealed record RoleDto(
    string Id,
    string Name,
    string Description,
    bool IsPredefined,
    int UserCount,
    int PermissionCount,
    Dictionary<string, List<string>> Permissions);

public sealed record PermissionCategoryDto(string Category, List<string> Actions);

public sealed record CaslAbilityRule(string Action, string Subject);
