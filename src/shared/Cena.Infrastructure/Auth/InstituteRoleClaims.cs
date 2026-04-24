// =============================================================================
// Cena Platform — Institute Role Claims (TENANCY-P3a)
// Firebase custom claims schema for per-institute role mapping.
//
// Replaces flat admin role with structured institutes array:
// { role: "MENTOR", institutes: [{ instituteId, role: "mentor" }] }
// =============================================================================

namespace Cena.Infrastructure.Auth;

/// <summary>
/// Role within an institute context.
/// </summary>
public enum InstituteRole
{
    /// <summary>Full mentoring + assignment + analytics access.</summary>
    Mentor,

    /// <summary>Classroom-only view + limited analytics.</summary>
    Instructor,

    /// <summary>Full institute admin (user management, settings).</summary>
    InstituteAdmin
}

/// <summary>
/// A single institute role binding in Firebase custom claims.
/// </summary>
public sealed record InstituteRoleBinding(
    string InstituteId,
    InstituteRole Role);

/// <summary>
/// TENANCY-P3a: Service interface for managing Firebase custom claims
/// with per-institute role mappings.
/// </summary>
public interface IFirebaseInstituteRoleService
{
    /// <summary>
    /// Sets the user's institute role bindings in Firebase custom claims.
    /// Replaces all existing institute bindings for this user.
    /// </summary>
    Task SetUserInstituteRolesAsync(
        string userId,
        IReadOnlyList<InstituteRoleBinding> roles,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a single institute role binding without removing existing ones.
    /// </summary>
    Task AddInstituteRoleAsync(
        string userId,
        string instituteId,
        InstituteRole role,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a user's role at a specific institute.
    /// </summary>
    Task RemoveInstituteRoleAsync(
        string userId,
        string instituteId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all institute roles for a user from Firebase custom claims.
    /// </summary>
    Task<IReadOnlyList<InstituteRoleBinding>> GetUserInstituteRolesAsync(
        string userId,
        CancellationToken ct = default);
}

/// <summary>
/// Constants for Firebase custom claim keys.
/// </summary>
public static class InstituteClaimKeys
{
    /// <summary>Claim key for the institutes array in Firebase JWT.</summary>
    public const string InstitutesClaimKey = "institutes";

    /// <summary>Claim key for the primary institute ID (Phase 1 compat).</summary>
    public const string InstituteIdClaimKey = "institute_id";

    /// <summary>Claim key for the primary role (backward compat).</summary>
    public const string RoleClaimKey = "role";
}
