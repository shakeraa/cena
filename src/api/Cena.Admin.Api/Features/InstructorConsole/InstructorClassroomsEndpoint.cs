// =============================================================================
// Cena Platform — Instructor classrooms endpoint (BG-05)
//
// Closes the BG-05 production 404 documented in the EPIC-G admin smoke
// allowlist (TASK-E2E-BG-05). The admin SPA's instructor view at
// /instructor/index.vue calls GET /api/instructor/classrooms on mount;
// before this endpoint, the call returned 404, the SPA showed a blank
// table, and F-02..F-05 e2e tests had no path forward.
//
// Surface contract (must match the SPA's TS interface in
// src/admin/full-version/src/pages/instructor/index.vue:8-16):
//
//   GET /api/instructor/classrooms
//     → 200 ClassroomOverview[]
//        { id, name, mode, status, studentCount, joinCode }
//     → 401 if unauthenticated
//     → empty [] (NOT 404) if the caller has no classrooms — empty-state
//       is a normal first-day-on-the-job state
//
// Scope: returns ONLY classrooms where the calling user is the
// TeacherId. There is no admin-style cross-tenant override here — an
// admin who wants to see *all* classrooms uses the mentor surface, not
// this one. This is a teacher-self-view endpoint.
//
// Privacy: this surface returns COUNTS only (studentCount). The actual
// roster lookup (which would need k=10 floor + per-student fields) lives
// behind the existing /classrooms/{id}/analytics/aggregate surface which
// is already k-floor-gated. We do NOT enforce k=10 on studentCount itself
// because it's a single integer reflecting the size of the teacher's
// own roster — they already know how many students they have.
//
// IDOR / cross-tenant: the only protection that matters here is the
// TeacherId match. Even if a malicious teacher knew another teacher's
// ClassroomId, this endpoint never returns rows where TeacherId !=
// caller.uid, so no cross-tenant data can leak.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.InstructorConsole;

/// <summary>
/// Wire-format DTO. Field names match the SPA's TypeScript
/// <c>ClassroomOverview</c> interface verbatim — ASP.NET's default
/// JSON serializer camelCases automatically so the C# PascalCase
/// fields below land as the SPA expects.
/// </summary>
public sealed record InstructorClassroomDto(
    string Id,
    string Name,
    string Mode,
    string Status,
    int StudentCount,
    string JoinCode);

public static class InstructorClassroomsEndpoint
{
    public const string Route = "/api/instructor/classrooms";

    public static IEndpointRouteBuilder MapInstructorClassroomsEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetInstructorClassrooms")
            .WithTags("Instructor Console")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .Produces<List<InstructorClassroomDto>>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext ctx,
        IDocumentStore store,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("InstructorClassrooms");
        var teacherId = GetCallerUid(ctx.User);
        if (string.IsNullOrEmpty(teacherId))
            return Results.Unauthorized();

        await using var session = store.QuerySession();

        // Filter to classrooms owned by this teacher. ClassroomDocument
        // already carries TeacherId — we filter at the query layer so
        // the wire never carries another teacher's row.
        var classrooms = await session.Query<ClassroomDocument>()
            .Where(c => c.TeacherId == teacherId)
            .ToListAsync(ct);

        // Collect roster sizes via the canonical resolver. We could
        // batch-load with a single events query but the cardinality
        // per teacher is small (a few classrooms × tens of students)
        // and the per-classroom call keeps the data path identical to
        // the analytics surface — one source of truth for "who is in
        // this classroom".
        var rows = new List<InstructorClassroomDto>(classrooms.Count);
        foreach (var c in classrooms)
        {
            var roster = await TeacherConsole.TeacherDashboardRosterResolver
                .ResolveRosterAsync(session, c.ClassroomId, ct);
            rows.Add(new InstructorClassroomDto(
                Id: c.ClassroomId,
                Name: c.Name,
                Mode: c.Mode.ToString(),
                Status: c.Status.ToString(),
                StudentCount: roster.Count,
                JoinCode: c.JoinCode ?? string.Empty));
        }

        logger.LogInformation(
            "[instructor] {ClassroomCount} classroom(s) for teacher {TeacherId}",
            rows.Count, teacherId);

        return Results.Ok(rows);
    }

    /// <summary>
    /// Pulls the caller's Firebase uid from the JWT. Falls back through
    /// the standard claim names; sub-claim is the canonical Firebase
    /// localId. Mirrors the pattern used in SessionEndpoints.GetStudentId
    /// so the same uid wire is honored across all caller-self-view
    /// endpoints.
    /// </summary>
    private static string? GetCallerUid(ClaimsPrincipal user)
    {
        return user.FindFirstValue("user_id")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
    }
}
