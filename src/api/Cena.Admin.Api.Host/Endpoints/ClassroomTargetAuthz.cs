// =============================================================================
// Cena Platform — Classroom target authorisation guard (PRR-236)
//
// Pure helper that decides whether a caller is allowed to assign exam
// targets to a specific ClassroomDocument. Extracted from the endpoint so
// the IDOR unit test can exercise the logic without a WebApplicationFactory.
//
// Rules (PRR-236 scope):
//   - SUPER_ADMIN: always allowed (ops / support path).
//   - TEACHER: allowed only when caller id equals ClassroomDocument.TeacherId
//     OR is in MentorIds. Mirrors TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess.
//   - Anyone else (STUDENT, MODERATOR, ADMIN, unknown): 403.
//
// ADMIN is explicitly excluded because PRR-236 is teacher-specific — the
// institute-wide assignment path is a different endpoint (Source=Tenant,
// future PRR-238) governed by a different ADR paragraph.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>
/// Verify teacher ownership of a classroom before writing classroom-assigned
/// exam targets. Throws <see cref="ForbiddenException"/> on failure (the
/// global exception middleware maps that to 403 + CenaError payload).
/// </summary>
public static class ClassroomTargetAuthz
{
    /// <summary>
    /// Throws <see cref="ForbiddenException"/> unless the caller is allowed
    /// to write targets to <paramref name="classroom"/>.
    /// </summary>
    public static void VerifyTeacherOwnership(
        ClaimsPrincipal caller, ClassroomDocument classroom)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(classroom);

        var role = caller.FindFirstValue(ClaimTypes.Role)
            ?? caller.FindFirstValue("role");

        if (role == "SUPER_ADMIN")
            return;

        if (role == "TEACHER")
        {
            var callerId = caller.FindFirstValue("user_id")
                ?? caller.FindFirstValue("sub")
                ?? caller.FindFirstValue(ClaimTypes.NameIdentifier);

            var isOwner = !string.IsNullOrEmpty(callerId)
                && string.Equals(classroom.TeacherId, callerId, StringComparison.Ordinal);
            var isMentor = classroom.MentorIds?
                .Contains(callerId ?? "", StringComparer.Ordinal) == true;

            if (isOwner || isMentor)
                return;

            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                $"TEACHER '{callerId}' cannot assign targets to classroom '{classroom.ClassroomId}' " +
                $"owned by '{classroom.TeacherId}'.");
        }

        throw new ForbiddenException(
            ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE,
            $"Role '{role}' is not permitted to assign classroom targets. Requires TEACHER.");
    }
}
