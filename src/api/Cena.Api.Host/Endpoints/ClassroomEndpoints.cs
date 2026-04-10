// =============================================================================
// Cena Platform -- Classroom REST Endpoints (STB-00b)
// Student-facing classroom join and management endpoints
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Me;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class ClassroomEndpoints
{
    public static IEndpointRouteBuilder MapClassroomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/classrooms")
            .WithTags("Classrooms")
            .RequireAuthorization();

        group.MapPost("/join", JoinClassroom).WithName("JoinClassroom");

        return app;
    }

    // POST /api/classrooms/join
    private static async Task<IResult> JoinClassroom(
        HttpContext ctx,
        IDocumentStore store,
        ClassroomJoinRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Validate input
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.BadRequest(new { Error = "Join code is required" });
        }

        await using var session = store.QuerySession();

        // Look up classroom by join code (case-insensitive)
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.JoinCode.ToLower() == request.Code.ToLower() && c.IsActive);

        if (classroom is null)
        {
            return Results.NotFound(new { Error = "Invalid or expired join code" });
        }

        var response = new ClassroomJoinResponse(
            ClassroomId: classroom.ClassroomId,
            ClassroomName: classroom.Name,
            TeacherName: classroom.TeacherName);

        return Results.Ok(response);
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
