// =============================================================================
// Cena Platform -- Admin Roles & Permissions DTOs
// BKD-003: Request/response types for role management endpoints
// =============================================================================

using Cena.Actors.Infrastructure.Documents;

namespace Cena.Actors.Api.Admin;

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
    Dictionary<string, List<string>> Permissions)
{
    public static RoleDto From(CenaRoleDefinition r, int userCount) => new(
        r.Id, r.Name, r.Description, r.IsPredefined, userCount,
        r.Permissions.Values.Sum(v => v.Count), r.Permissions);
}

public sealed record PermissionCategoryDto(string Category, List<string> Actions);

public sealed record CaslAbilityRule(string Action, string Subject);
