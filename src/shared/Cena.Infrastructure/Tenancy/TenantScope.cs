// =============================================================================
// Cena Platform -- Tenant Scope Helper
// REV-014: Single source of truth for school-level tenant filtering.
// TENANCY-P1f: Added GetInstituteFilter for multi-institute scoping.
// =============================================================================

using System.Security.Claims;

namespace Cena.Infrastructure.Tenancy;

/// <summary>
/// Extracts effective tenant filters from the authenticated user's claims.
/// SUPER_ADMIN returns null/empty (unrestricted -- sees all tenants).
/// All other roles are scoped to their school or institute.
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
        // Check both the direct Firebase "role" claim and the mapped ClaimTypes.Role
        var role = user.FindFirstValue(ClaimTypes.Role)
                ?? user.FindFirstValue("role");
        if (role == "SUPER_ADMIN") return null; // No filter -- sees everything

        var schoolId = user.FindFirstValue("school_id");
        if (string.IsNullOrEmpty(schoolId))
            throw new UnauthorizedAccessException(
                "User has no school_id claim. Cannot determine tenant scope.");

        return schoolId;
    }

    /// <summary>
    /// TENANCY-P1f: Returns the institute IDs this user is authorized to access.
    /// Phase 1 returns a single-element list from the "institute_id" claim
    /// (set by the enrollment backfill or onboarding). SUPER_ADMIN returns empty
    /// (unrestricted). Students without an institute claim get the platform default.
    ///
    /// Phase 2 will expand this to support multi-institute membership via
    /// Firebase custom claims with an array of institute IDs.
    /// </summary>
    /// <param name="user">Authenticated user's claims principal.</param>
    /// <param name="defaultInstituteId">
    /// Fallback institute ID for users without an institute_id claim.
    /// Typically "cena-platform" (the platform institute seeded in P1d).
    /// Pass null to return an empty list for unenrolled users.
    /// </param>
    /// <returns>
    /// Single-element list with the user's institute ID, or empty for SUPER_ADMIN
    /// or unenrolled users (when no default is provided).
    /// </returns>
    public static IReadOnlyList<string> GetInstituteFilter(
        ClaimsPrincipal user,
        string? defaultInstituteId = "cena-platform")
    {
        var role = user.FindFirstValue(ClaimTypes.Role)
                ?? user.FindFirstValue("role");
        if (role == "SUPER_ADMIN") return Array.Empty<string>();

        // Phase 1: single institute from claim
        var instituteId = user.FindFirstValue("institute_id");
        if (!string.IsNullOrEmpty(instituteId))
            return new[] { instituteId };

        // Fallback to default institute for users without the claim
        if (!string.IsNullOrEmpty(defaultInstituteId))
            return new[] { defaultInstituteId };

        return Array.Empty<string>();
    }
}
