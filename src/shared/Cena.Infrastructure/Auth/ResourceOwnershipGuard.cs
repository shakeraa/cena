// =============================================================================
// Cena Platform -- IDOR Prevention: Resource Ownership Guard
// SEC-002: Prevents cross-student data access (IDOR vulnerability C-4).
//
// Role hierarchy enforced:
//   SUPER_ADMIN  — unrestricted access to any student in any school
//   ADMIN        — school-scoped (TenantScope already filters the query layer)
//   MODERATOR    — school-scoped (same as ADMIN)
//   STUDENT      — own data only; any other studentId => 403
//   PARENT       — children listed in student_id claims only; other studentIds => 403
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Errors;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// Enforces per-resource ownership rules to prevent IDOR attacks.
/// Call from endpoint handlers BEFORE querying the actor/data layer.
/// Throws <see cref="UnauthorizedAccessException"/> (mapped to HTTP 403) on violation.
/// </summary>
public static class ResourceOwnershipGuard
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that the authenticated caller is allowed to access data for
    /// <paramref name="targetStudentId"/>.
    ///
    /// Rules:
    /// - SUPER_ADMIN: always allowed.
    /// - ADMIN / MODERATOR: school-scoped access is enforced by TenantScope at
    ///   the query layer; no additional check required here.
    /// - STUDENT: may only access their own data (caller sub == targetStudentId).
    /// - PARENT: may only access students listed in their student_id claims.
    /// - Unknown / missing role: denied.
    /// </summary>
    /// <param name="caller">The authenticated <see cref="ClaimsPrincipal"/>.</param>
    /// <param name="targetStudentId">The studentId from the route parameter.</param>
    /// <exception cref="ForbiddenException">
    /// Thrown (HTTP 403, code CENA_AUTH_IDOR_VIOLATION) when the caller does not
    /// have permission to access the requested student's data.
    /// </exception>
    public static void VerifyStudentAccess(ClaimsPrincipal caller, string targetStudentId)
    {
        var role = GetRole(caller);

        switch (role)
        {
            case "SUPER_ADMIN":
                return; // Unrestricted

            case "ADMIN":
            case "MODERATOR":
                // School-level scoping is handled by TenantScope.GetSchoolFilter().
                // These roles are legitimately allowed to view any student within
                // their school; no per-student ownership check is needed here.
                return;

            case "STUDENT":
                var callerId = caller.FindFirstValue("sub")
                    ?? caller.FindFirstValue("user_id")
                    ?? caller.FindFirstValue(ClaimTypes.NameIdentifier);

                if (callerId == null || !string.Equals(callerId, targetStudentId, StringComparison.Ordinal))
                {
                    throw new ForbiddenException(
                        ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                        $"STUDENT '{callerId}' attempted to access student '{targetStudentId}'. " +
                        "Students may only access their own data.");
                }
                return;

            case "PARENT":
                VerifyParentOwnsStudent(caller, targetStudentId);
                return;

            default:
                throw new ForbiddenException(
                    ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                    $"Caller has unrecognised or missing role '{role}'. Access denied.");
        }
    }

    /// <summary>
    /// Verifies that a PARENT caller has the given <paramref name="studentId"/>
    /// listed in their token's <c>student_id</c> claims (populated by
    /// <see cref="CenaClaimsTransformer"/> from the Firebase <c>student_ids</c> array).
    ///
    /// ADMIN-and-above callers bypass this check automatically.
    /// </summary>
    /// <exception cref="ForbiddenException">
    /// Thrown (HTTP 403, code CENA_AUTH_IDOR_VIOLATION) when the parent does not
    /// have a linked claim for the requested student.
    /// </exception>
    public static void VerifyParentStudentLink(ClaimsPrincipal caller, string studentId)
    {
        var role = GetRole(caller);

        if (role is "SUPER_ADMIN" or "ADMIN" or "MODERATOR")
            return;

        if (role == "STUDENT")
        {
            // A student accessing a parent-scoped path still needs ownership check.
            VerifyStudentAccess(caller, studentId);
            return;
        }

        if (role == "PARENT")
        {
            VerifyParentOwnsStudent(caller, studentId);
            return;
        }

        throw new ForbiddenException(
            ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
            $"Caller role '{role}' is not permitted to access student data.");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string? GetRole(ClaimsPrincipal caller)
        => caller.FindFirstValue(ClaimTypes.Role)
           ?? caller.FindFirstValue("role");

    private static void VerifyParentOwnsStudent(ClaimsPrincipal caller, string targetStudentId)
    {
        // CenaClaimsTransformer explodes the Firebase student_ids array into
        // individual "student_id" claims on the identity.
        var linkedIds = caller.FindAll("student_id")
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (!linkedIds.Contains(targetStudentId))
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                $"PARENT caller does not have access to student '{targetStudentId}'. " +
                "Only linked children are accessible.");
        }
    }
}
