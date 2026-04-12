// =============================================================================
// Cena Platform -- GDPR Resource Guard
// FIND-sec-011: Prevents cross-tenant access to GDPR endpoints
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Tenancy;
using Marten;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Guards GDPR operations against cross-tenant access.
/// </summary>
public static class GdprResourceGuard
{
    /// <summary>
    /// Verifies that the student belongs to the caller's school.
    /// Throws <see cref="KeyNotFoundException"/> if student not found or not in caller's school.
    /// </summary>
    public static async Task VerifyStudentBelongsToCallerSchoolAsync(
        string studentId, 
        ClaimsPrincipal caller,
        IDocumentStore store)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(caller);
        if (callerSchoolId is null)
        {
            // SUPER_ADMIN - no restriction
            return;
        }

        await using var session = store.QuerySession();
        var student = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        
        if (student is null)
        {
            throw new KeyNotFoundException($"Student '{studentId}' not found");
        }

        if (student.SchoolId != callerSchoolId)
        {
            // Return 404 to avoid leaking existence
            throw new KeyNotFoundException($"Student '{studentId}' not found");
        }
    }
}
