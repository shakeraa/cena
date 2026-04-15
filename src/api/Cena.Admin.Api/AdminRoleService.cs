// =============================================================================
// Cena Platform -- Admin Roles & Permissions Service
// BKD-003: Business logic for role CRUD, permission matrix, CASL mapping
// =============================================================================

using System.Security.Claims;
using System.Text.RegularExpressions;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Seed;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IAdminRoleService
{
    Task<IReadOnlyList<RoleDto>> ListRolesAsync();
    Task<RoleDto?> GetRoleAsync(string id);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request);
    Task<RoleDto> UpdatePermissionsAsync(string id, UpdatePermissionsRequest request);
    Task DeleteRoleAsync(string id);
    Task<IReadOnlyList<PermissionCategoryDto>> ListPermissionsAsync();
    Task AssignRoleToUserAsync(string userId, AssignRoleRequest request, ClaimsPrincipal caller);
    Task<IReadOnlyList<CaslAbilityRule>> GetUserAbilitiesAsync(string userId);
}

public sealed class AdminRoleService : IAdminRoleService
{
    private readonly IDocumentStore _store;
    private readonly IFirebaseAdminService _firebase;
    private readonly ILogger<AdminRoleService> _logger;

    public AdminRoleService(
        IDocumentStore store,
        IFirebaseAdminService firebase,
        ILogger<AdminRoleService> logger)
    {
        _store = store;
        _firebase = firebase;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync()
    {
        await using var session = _store.QuerySession();

        var roles = await session.Query<CenaRoleDefinition>()
            .Where(r => !r.SoftDeleted)
            .ToListAsync();

        var users = await session.Query<AdminUser>()
            .Where(u => !u.SoftDeleted)
            .ToListAsync();

        var countByRole = users
            .GroupBy(u => u.Role.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return roles.Select(r => RoleDto.From(r, countByRole.GetValueOrDefault(r.Id, 0))).ToList();
    }

    public async Task<RoleDto?> GetRoleAsync(string id)
    {
        await using var session = _store.QuerySession();
        var role = await session.LoadAsync<CenaRoleDefinition>(id);
        if (role is null or { SoftDeleted: true }) return null;

        var userCount = await session.Query<AdminUser>()
            .Where(u => !u.SoftDeleted && u.Role.ToString() == id)
            .CountAsync();

        return RoleDto.From(role, userCount);
    }

    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request)
    {
        var id = Slugify(request.Name);

        await using var session = _store.LightweightSession();

        var existing = await session.LoadAsync<CenaRoleDefinition>(id);
        if (existing != null)
            throw new InvalidOperationException($"Role '{id}' already exists");

        var permissions = new Dictionary<string, List<string>>();
        if (!string.IsNullOrEmpty(request.CopyFrom))
        {
            var source = await session.LoadAsync<CenaRoleDefinition>(request.CopyFrom)
                ?? throw new KeyNotFoundException($"Source role '{request.CopyFrom}' not found");
            permissions = source.Permissions.ToDictionary(
                kv => kv.Key, kv => kv.Value.ToList());
        }

        var role = new CenaRoleDefinition
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
            IsPredefined = false,
            Permissions = permissions,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        session.Store(role);
        await session.SaveChangesAsync();

        _logger.LogInformation("Created custom role: {RoleId}", id);
        return RoleDto.From(role, 0);
    }

    public async Task<RoleDto> UpdatePermissionsAsync(string id, UpdatePermissionsRequest request)
    {
        await using var session = _store.LightweightSession();
        var role = await session.LoadAsync<CenaRoleDefinition>(id)
            ?? throw new KeyNotFoundException($"Role '{id}' not found");

        var updated = role with
        {
            Permissions = request.Permissions,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        session.Store(updated);
        await session.SaveChangesAsync();

        var userCount = await session.Query<AdminUser>()
            .Where(u => !u.SoftDeleted && u.Role.ToString() == id)
            .CountAsync();

        return RoleDto.From(updated, userCount);
    }

    public async Task DeleteRoleAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var role = await session.LoadAsync<CenaRoleDefinition>(id)
            ?? throw new KeyNotFoundException($"Role '{id}' not found");

        if (role.IsPredefined)
            throw new InvalidOperationException("Cannot delete a predefined role");

        var assignedCount = await session.Query<AdminUser>()
            .Where(u => !u.SoftDeleted && u.Role.ToString() == id)
            .CountAsync();

        if (assignedCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete role '{id}' — {assignedCount} user(s) are assigned to it");

        var deleted = role with { SoftDeleted = true, UpdatedAt = DateTimeOffset.UtcNow };
        session.Store(deleted);
        await session.SaveChangesAsync();
    }

    public Task<IReadOnlyList<PermissionCategoryDto>> ListPermissionsAsync()
    {
        IReadOnlyList<PermissionCategoryDto> result = RoleSeedData.AllPermissions
            .Select(kv => new PermissionCategoryDto(kv.Key, kv.Value))
            .ToList();
        return Task.FromResult(result);
    }

    public async Task AssignRoleToUserAsync(string userId, AssignRoleRequest request, ClaimsPrincipal caller)
    {
        if (!Enum.TryParse<CenaRole>(request.Role, true, out var newRole))
            throw new ArgumentException($"Invalid role: {request.Role}");

        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(userId)
            ?? throw new KeyNotFoundException($"User '{userId}' not found");

        // FIND-sec-010: Get caller's role and school for privilege escalation checks
        var callerRole = caller.FindFirstValue(ClaimTypes.Role)
            ?? caller.FindFirstValue("role")
            ?? throw new UnauthorizedAccessException("Caller has no role claim");

        var callerSchool = TenantScope.GetSchoolFilter(caller);
        var isSuperAdmin = callerRole == "SUPER_ADMIN";

        // FIND-sec-010: Only SUPER_ADMIN can assign SUPER_ADMIN role
        if (newRole == CenaRole.SUPER_ADMIN && !isSuperAdmin)
        {
            _logger.LogWarning("Privilege escalation attempt: caller {Caller} with role {CallerRole} attempted to assign SUPER_ADMIN to user {UserId}",
                caller.FindFirstValue("sub") ?? "unknown", callerRole, userId);
            throw new UnauthorizedAccessException("Only SUPER_ADMIN can assign the SUPER_ADMIN role");
        }

        // FIND-sec-010: ADMIN can only assign roles to users in their own school
        if (!isSuperAdmin && callerSchool != null && user.School != callerSchool)
        {
            _logger.LogWarning("Cross-school role assignment attempt: caller from school {CallerSchool} attempted to assign role to user {UserId} in school {TargetSchool}",
                callerSchool, userId, user.School);
            throw new KeyNotFoundException($"User '{userId}' not found");
        }

        // Safety: cannot remove last SUPER_ADMIN
        if (user.Role == CenaRole.SUPER_ADMIN && newRole != CenaRole.SUPER_ADMIN)
        {
            var superAdminCount = await session.Query<AdminUser>()
                .Where(u => !u.SoftDeleted && u.Role == CenaRole.SUPER_ADMIN)
                .CountAsync();

            if (superAdminCount <= 1)
                throw new InvalidOperationException("Cannot remove the last SUPER_ADMIN");
        }

        var updated = user with { Role = newRole };
        session.Store(updated);
        await session.SaveChangesAsync();

        await _firebase.SetCustomClaimsAsync(userId, new Dictionary<string, object>
        {
            ["role"] = newRole.ToString(),
            ["school_id"] = user.School ?? "",
            ["locale"] = user.Locale
        });

        // FIND-sec-010: Emit audit log for FERPA compliance
        var accessLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = DateTimeOffset.UtcNow,
            AccessedBy = caller.FindFirstValue("sub") ?? caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
            AccessorRole = callerRole,
            AccessorSchool = callerSchool,
            StudentId = userId,
            Endpoint = "/api/v1/admin/users/{id}/role",
            HttpMethod = "POST",
            StatusCode = 204,
            Category = "privileged_action"
        };
        session.Store(accessLog);
        await session.SaveChangesAsync();

        _logger.LogInformation("Assigned role {Role} to user {UserId} by caller {Caller} with role {CallerRole}",
            newRole, userId, accessLog.AccessedBy, callerRole);
    }

    public async Task<IReadOnlyList<CaslAbilityRule>> GetUserAbilitiesAsync(string userId)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(userId)
            ?? throw new KeyNotFoundException($"User '{userId}' not found");

        var roleDef = await session.LoadAsync<CenaRoleDefinition>(user.Role.ToString());

        if (roleDef == null)
            return [];

        // SUPER_ADMIN gets manage:all
        if (user.Role == CenaRole.SUPER_ADMIN)
            return [new CaslAbilityRule("manage", "all")];

        var rules = new List<CaslAbilityRule>();
        foreach (var (category, actions) in roleDef.Permissions)
        {
            foreach (var action in actions)
            {
                rules.Add(new CaslAbilityRule(MapCaslAction(action), category));
            }
        }

        return rules;
    }

    private static string MapCaslAction(string action) => action switch
    {
        "list" or "view" or "view-own" or "view-class" or "view-school" or "view-platform" or "view-aggregated" or "view-health" or "view-logs" => "read",
        "create" => "create",
        "edit" or "edit-own" or "edit-org" or "edit-platform" or "configure-alerts" or "configure-thresholds" or "manage-actors" or "manage-config" => "update",
        "delete" => "delete",
        "approve" or "reject" or "publish" or "review" => "manage",
        "suspend" or "impersonate" => "manage",
        "export" => "read",
        _ => action
    };

    private static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Replace(' ', '_');
        slug = Regex.Replace(slug, @"[^a-z0-9_]", "");
        return $"custom_{slug}";
    }
}
