// =============================================================================
// Cena Platform -- GDPR Resource Guard
// FIND-sec-011: Prevents cross-tenant access to GDPR endpoints
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Tenancy;
using Marten;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Guards GDPR operations against cross-tenant access. Reads school
/// membership directly from the Marten snapshot table via raw SQL —
/// the snapshot type lives in Cena.Actors and pulling it across the
/// project boundary would create a cycle. The previous LoadAsync&lt;T&gt;
/// approach silently mapped to a non-existent mt_doc_studentprofilesnapshotref
/// table; this rewrite hits the real mt_doc_studentprofilesnapshot row.
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
        await using var cmd = session.Connection!.CreateCommand();
        cmd.CommandText = "SELECT data->>'SchoolId' FROM cena.mt_doc_studentprofilesnapshot WHERE id = @sid LIMIT 1";
        var p = cmd.CreateParameter();
        p.ParameterName = "sid";
        p.Value = studentId;
        cmd.Parameters.Add(p);

        var schoolId = await cmd.ExecuteScalarAsync() as string;

        if (string.IsNullOrEmpty(schoolId))
        {
            throw new KeyNotFoundException($"Student '{studentId}' not found");
        }

        if (schoolId != callerSchoolId)
        {
            // Return 404 to avoid leaking existence
            throw new KeyNotFoundException($"Student '{studentId}' not found");
        }
    }
}
