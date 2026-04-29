// =============================================================================
// Cena Platform — Mentor institutes endpoints (BG-05)
//
// Closes BG-05's mentor surface 404s. The admin SPA's mentor dashboard
// at /mentor/index.vue + /mentor/institutes/[id]/index.vue calls three
// endpoints:
//
//   GET /api/mentor/institutes                     — list
//   GET /api/mentor/institutes/{instituteId}       — drilldown detail
//   GET /api/mentor/institutes/{instituteId}/classrooms
//                                                  — classrooms in institute
//
// Before this file all three returned 404, the SPA showed blank dashboards,
// and the tenant-3-Phase-3b/c features documented in the templates above
// had no end-to-end coverage path.
//
// AUTHORIZATION:
//   • SUPER_ADMIN: sees all institutes / institute-detail / institute-classrooms.
//   • ADMIN: sees only institutes where InstituteDocument.MentorId == caller.uid.
//     Detail + classrooms are filtered the same way — a request for an
//     institute the caller does not mentor returns 404 (NOT 403; we don't
//     leak existence).
//   • TEACHER / STUDENT / PARENT: 403 (the AdminOnly policy gates this).
//
// CROSS-TENANT GUARD:
//   Every drilldown verifies MentorId match BEFORE returning institute
//   data. Since classrooms carry InstituteId, the classroom listing
//   inherits the same scope: a request for /api/mentor/institutes/{otherId}/classrooms
//   when the caller doesn't mentor {otherId} returns 404 with no row leak.
//
// PRIVACY: returns counts only (classroomCount, studentCount). No per-
// student fields, no per-classroom analytics. Drilldown to those surfaces
// goes through /api/v1/institutes/{id}/classrooms/{cid}/analytics/aggregate
// which is already k-floor-gated.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.MentorConsole;

/// <summary>
/// Wire-format DTO for the mentor institute list. Field names match the
/// SPA's TypeScript <c>InstituteOverview</c> interface in
/// src/admin/full-version/src/pages/mentor/index.vue:7-13.
/// </summary>
public sealed record MentorInstituteDto(
    string Id,
    string Name,
    string Type,
    int ClassroomCount,
    int StudentCount);

/// <summary>
/// Wire-format DTO for the institute drilldown. Field names match the
/// SPA's <c>institute</c> object usage in
/// src/admin/full-version/src/pages/mentor/institutes/[id]/index.vue.
/// </summary>
public sealed record MentorInstituteDetailDto(
    string Id,
    string Name,
    string Type,
    string Country,
    string MentorId);

/// <summary>
/// Wire-format DTO for classrooms-in-institute. Same shape as the
/// instructor list to keep the SPA's classroom-card component reusable
/// across the two surfaces.
/// </summary>
public sealed record MentorInstituteClassroomDto(
    string Id,
    string Name,
    string Mode,
    string Status,
    int StudentCount,
    string JoinCode);

public static class MentorInstitutesEndpoint
{
    public const string ListRoute = "/api/mentor/institutes";
    public const string DetailRoute = "/api/mentor/institutes/{instituteId}";
    public const string ClassroomsRoute = "/api/mentor/institutes/{instituteId}/classrooms";

    public static IEndpointRouteBuilder MapMentorInstitutesEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(ListRoute, ListAsync)
            .WithName("GetMentorInstitutes")
            .WithTags("Mentor Console")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api")
            .Produces<List<MentorInstituteDto>>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        app.MapGet(DetailRoute, DetailAsync)
            .WithName("GetMentorInstituteDetail")
            .WithTags("Mentor Console")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api")
            .Produces<MentorInstituteDetailDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        app.MapGet(ClassroomsRoute, ClassroomsAsync)
            .WithName("GetMentorInstituteClassrooms")
            .WithTags("Mentor Console")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api")
            .Produces<List<MentorInstituteClassroomDto>>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext ctx,
        IDocumentStore store,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("MentorInstitutes.List");
        var callerUid = GetCallerUid(ctx.User);
        if (string.IsNullOrEmpty(callerUid))
            return Results.Unauthorized();

        var isSuperAdmin = IsSuperAdmin(ctx.User);

        await using var session = store.QuerySession();

        var institutes = isSuperAdmin
            ? await session.Query<InstituteDocument>().ToListAsync(ct)
            : await session.Query<InstituteDocument>()
                .Where(i => i.MentorId == callerUid)
                .ToListAsync(ct);

        // Bulk-load all classrooms for these institutes once instead of
        // N+1 round-trips. The set is bounded by mentor-visible scope so
        // the in-memory grouping is O(rooms) not O(all rooms).
        var instituteIds = institutes.Select(i => i.InstituteId).ToHashSet(StringComparer.Ordinal);
        var classroomsByInstitute = (await session.Query<ClassroomDocument>()
            .Where(c => c.InstituteId != null)
            .ToListAsync(ct))
            .Where(c => instituteIds.Contains(c.InstituteId!))
            .GroupBy(c => c.InstituteId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var rows = new List<MentorInstituteDto>(institutes.Count);
        foreach (var inst in institutes)
        {
            var rooms = classroomsByInstitute.GetValueOrDefault(inst.InstituteId, new List<ClassroomDocument>());
            var studentCount = await CountStudentsAcrossClassroomsAsync(session, rooms, ct);

            rows.Add(new MentorInstituteDto(
                Id: inst.InstituteId,
                Name: inst.Name,
                Type: inst.Type.ToString(),
                ClassroomCount: rooms.Count,
                StudentCount: studentCount));
        }

        logger.LogInformation(
            "[mentor] {InstituteCount} institute(s) visible to caller {CallerUid} (super_admin={IsSuperAdmin})",
            rows.Count, callerUid, isSuperAdmin);

        return Results.Ok(rows);
    }

    private static async Task<IResult> DetailAsync(
        string instituteId,
        HttpContext ctx,
        IDocumentStore store,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("MentorInstitutes.Detail");
        var callerUid = GetCallerUid(ctx.User);
        if (string.IsNullOrEmpty(callerUid))
            return Results.Unauthorized();

        await using var session = store.QuerySession();
        var inst = await session.Query<InstituteDocument>()
            .FirstOrDefaultAsync(i => i.InstituteId == instituteId, ct);

        if (inst is null)
            throw new EntityNotFoundException($"institute '{instituteId}' not found");

        // Scope: SUPER_ADMIN sees all; otherwise must be the institute's
        // mentor. Return 404 (not 403) on cross-mentor reads so we don't
        // leak existence.
        if (!IsSuperAdmin(ctx.User) && !string.Equals(inst.MentorId, callerUid, StringComparison.Ordinal))
            throw new EntityNotFoundException($"institute '{instituteId}' not found");

        return Results.Ok(new MentorInstituteDetailDto(
            Id: inst.InstituteId,
            Name: inst.Name,
            Type: inst.Type.ToString(),
            Country: inst.Country,
            MentorId: inst.MentorId));
    }

    private static async Task<IResult> ClassroomsAsync(
        string instituteId,
        HttpContext ctx,
        IDocumentStore store,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("MentorInstitutes.Classrooms");
        var callerUid = GetCallerUid(ctx.User);
        if (string.IsNullOrEmpty(callerUid))
            return Results.Unauthorized();

        await using var session = store.QuerySession();

        // Scope check first — we read the institute even on the
        // classrooms call so we can enforce the same mentor match.
        var inst = await session.Query<InstituteDocument>()
            .FirstOrDefaultAsync(i => i.InstituteId == instituteId, ct);
        if (inst is null)
            throw new EntityNotFoundException($"institute '{instituteId}' not found");

        if (!IsSuperAdmin(ctx.User) && !string.Equals(inst.MentorId, callerUid, StringComparison.Ordinal))
            throw new EntityNotFoundException($"institute '{instituteId}' not found");

        var classrooms = await session.Query<ClassroomDocument>()
            .Where(c => c.InstituteId == instituteId)
            .ToListAsync(ct);

        var rows = new List<MentorInstituteClassroomDto>(classrooms.Count);
        foreach (var c in classrooms)
        {
            var roster = await TeacherConsole.TeacherDashboardRosterResolver
                .ResolveRosterAsync(session, c.ClassroomId, ct);
            rows.Add(new MentorInstituteClassroomDto(
                Id: c.ClassroomId,
                Name: c.Name,
                Mode: c.Mode.ToString(),
                Status: c.Status.ToString(),
                StudentCount: roster.Count,
                JoinCode: c.JoinCode ?? string.Empty));
        }

        return Results.Ok(rows);
    }

    private static async Task<int> CountStudentsAcrossClassroomsAsync(
        IQuerySession session,
        IReadOnlyList<ClassroomDocument> classrooms,
        CancellationToken ct)
    {
        var total = 0;
        foreach (var c in classrooms)
        {
            var roster = await TeacherConsole.TeacherDashboardRosterResolver
                .ResolveRosterAsync(session, c.ClassroomId, ct);
            total += roster.Count;
        }
        return total;
    }

    private static string? GetCallerUid(ClaimsPrincipal user) =>
        user.FindFirstValue("user_id")
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub");

    private static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        var role = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role);
        return string.Equals(role, "SUPER_ADMIN", StringComparison.OrdinalIgnoreCase);
    }
}
