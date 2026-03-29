// =============================================================================
// Cena Platform -- Tenant Scope Helper
// REV-014: Single source of truth for school-level tenant filtering.
// =============================================================================

using System.Security.Claims;

namespace Cena.Infrastructure.Tenancy;

/// <summary>
/// Extracts the effective school_id filter from the authenticated user's claims.
/// SUPER_ADMIN returns null (unrestricted -- sees all schools).
/// All other roles must have a school_id claim; missing claim throws.
/// </summary>
public static class TenantScope
{
    /// <summary>
    /// Returns the school_id to filter by, or null if the caller is SUPER_ADMIN.
    /// Throws <see cref="UnauthorizedAccessException"/> when a non-SUPER_ADMIN
    /// user is missing the school_id claim.
    /// </summary>
    public static string? GetSchoolFilter(ClaimsPrincipal user)
    {
        var role = user.FindFirstValue("cena_role");
        if (role == "SUPER_ADMIN") return null; // No filter -- sees everything

        var schoolId = user.FindFirstValue("school_id");
        if (string.IsNullOrEmpty(schoolId))
            throw new UnauthorizedAccessException(
                "User has no school_id claim. Cannot determine tenant scope.");

        return schoolId;
    }
}
