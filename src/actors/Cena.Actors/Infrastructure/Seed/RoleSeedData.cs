// =============================================================================
// Cena Platform -- Predefined Role Seed Data
// BKD-003: Seeds 6 predefined roles with default permission matrices on startup
// =============================================================================

using Cena.Actors.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Infrastructure.Seed;

public static class RoleSeedData
{
    // All permission categories and their actions
    public static readonly Dictionary<string, List<string>> AllPermissions = new()
    {
        ["Users"] = ["list", "view", "create", "edit", "delete", "suspend", "impersonate"],
        ["Content"] = ["list", "view", "create", "edit", "delete", "approve", "reject", "publish"],
        ["Questions"] = ["list", "view", "create", "edit", "delete", "review", "approve"],
        ["Analytics"] = ["view-own", "view-class", "view-school", "view-platform", "export"],
        ["Focus Data"] = ["view-own", "view-class", "view-aggregated", "configure-alerts"],
        ["Mastery Data"] = ["view-own", "view-class", "view-school", "configure-thresholds"],
        ["Settings"] = ["view", "edit-own", "edit-org", "edit-platform"],
        ["System"] = ["view-health", "manage-actors", "view-logs", "manage-config"],
    };

    private static readonly CenaRoleDefinition[] PredefinedRoles =
    [
        new CenaRoleDefinition
        {
            Id = "SUPER_ADMIN",
            Name = "Super Admin",
            Description = "Platform owner — full access to everything",
            IsPredefined = true,
            Permissions = AllPermissions
        },
        new CenaRoleDefinition
        {
            Id = "ADMIN",
            Name = "Admin",
            Description = "School administrator — manages users, content, and school settings",
            IsPredefined = true,
            Permissions = new()
            {
                ["Users"] = ["list", "view", "create", "edit", "delete", "suspend"],
                ["Content"] = ["list", "view", "create", "edit", "delete", "approve", "reject", "publish"],
                ["Questions"] = ["list", "view", "create", "edit", "delete", "review", "approve"],
                ["Analytics"] = ["view-own", "view-class", "view-school", "export"],
                ["Focus Data"] = ["view-own", "view-class", "view-aggregated", "configure-alerts"],
                ["Mastery Data"] = ["view-own", "view-class", "view-school", "configure-thresholds"],
                ["Settings"] = ["view", "edit-own", "edit-org"],
                ["System"] = ["view-health", "view-logs"],
            }
        },
        new CenaRoleDefinition
        {
            Id = "MODERATOR",
            Name = "Moderator",
            Description = "Content moderator — reviews and approves questions and content",
            IsPredefined = true,
            Permissions = new()
            {
                ["Users"] = ["list", "view"],
                ["Content"] = ["list", "view", "create", "edit", "approve", "reject", "publish"],
                ["Questions"] = ["list", "view", "create", "edit", "review", "approve"],
                ["Analytics"] = ["view-own", "view-class"],
                ["Focus Data"] = ["view-own", "view-class"],
                ["Mastery Data"] = ["view-own", "view-class"],
                ["Settings"] = ["view", "edit-own"],
            }
        },
        new CenaRoleDefinition
        {
            Id = "TEACHER",
            Name = "Teacher",
            Description = "Teacher — views class analytics, manages own content",
            IsPredefined = true,
            Permissions = new()
            {
                ["Content"] = ["list", "view", "create", "edit"],
                ["Questions"] = ["list", "view", "create", "edit"],
                ["Analytics"] = ["view-own", "view-class"],
                ["Focus Data"] = ["view-own", "view-class"],
                ["Mastery Data"] = ["view-own", "view-class"],
                ["Settings"] = ["view", "edit-own"],
            }
        },
        new CenaRoleDefinition
        {
            Id = "PARENT",
            Name = "Parent",
            Description = "Parent — views own children's progress",
            IsPredefined = true,
            Permissions = new()
            {
                ["Analytics"] = ["view-own"],
                ["Focus Data"] = ["view-own"],
                ["Mastery Data"] = ["view-own"],
                ["Settings"] = ["view", "edit-own"],
            }
        },
        new CenaRoleDefinition
        {
            Id = "STUDENT",
            Name = "Student",
            Description = "Student — learns and views own data",
            IsPredefined = true,
            Permissions = new()
            {
                ["Analytics"] = ["view-own"],
                ["Focus Data"] = ["view-own"],
                ["Mastery Data"] = ["view-own"],
                ["Settings"] = ["view", "edit-own"],
            }
        },
    ];

    /// <summary>
    /// Seeds predefined roles on startup. Upserts — existing roles are updated, new ones created.
    /// </summary>
    public static async Task SeedRolesAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();

        foreach (var role in PredefinedRoles)
        {
            var existing = await session.LoadAsync<CenaRoleDefinition>(role.Id);
            if (existing == null)
            {
                session.Store(role);
                logger.LogInformation("Seeded predefined role: {RoleId}", role.Id);
            }
            else
            {
                // Update permissions on existing predefined roles (schema evolution)
                var updated = existing with
                {
                    Name = role.Name,
                    Description = role.Description,
                    Permissions = role.Permissions,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                session.Store(updated);
            }
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Role seed complete. {Count} predefined roles ensured.", PredefinedRoles.Length);
    }
}
