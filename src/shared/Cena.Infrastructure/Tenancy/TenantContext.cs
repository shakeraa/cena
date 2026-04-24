// =============================================================================
// Cena Platform -- Tenant Context (REV-014: multi-tenant isolation)
// =============================================================================

namespace Cena.Infrastructure.Tenancy;

/// <summary>
/// Resolves the current tenant (school) from the authenticated user's claims.
/// </summary>
public interface ITenantResolver
{
    string? GetSchoolId(System.Security.Claims.ClaimsPrincipal user);
}

public sealed class ClaimsTenantResolver : ITenantResolver
{
    public string? GetSchoolId(System.Security.Claims.ClaimsPrincipal user)
        => user.FindFirst("school_id")?.Value
        ?? user.FindFirst("schoolId")?.Value;
}
