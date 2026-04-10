// =============================================================================
// Cena Platform -- Me/Profile REST Endpoints (STB-00)
// Student-facing bootstrap and profile endpoints
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Me;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("Me")
            .RequireAuthorization();

        group.MapGet("", GetBootstrap).WithName("GetMe");
        group.MapGet("/profile", GetProfile).WithName("GetMeProfile");
        group.MapPatch("/profile", UpdateProfile).WithName("UpdateMeProfile");
        group.MapPost("/onboarding", SubmitOnboarding).WithName("SubmitOnboarding");

        return app;
    }

    // GET /api/me — bootstrap payload
    private static async Task<IResult> GetBootstrap(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
            return Results.NotFound();

        var dto = new MeBootstrapDto(
            StudentId: profile.StudentId,
            DisplayName: profile.DisplayName ?? profile.FullName ?? "Student",
            Role: profile.Role ?? "student",
            Locale: profile.Locale ?? "en",
            OnboardedAt: profile.OnboardedAt,
            Subjects: profile.Subjects,
            Level: CalculateLevel(profile.TotalXp),
            StreakDays: profile.CurrentStreak,
            AvatarUrl: null); // TODO: Avatar storage in STB-00b

        return Results.Ok(dto);
    }

    // GET /api/me/profile — full profile view
    private static async Task<IResult> GetProfile(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
            return Results.NotFound();

        var dto = new ProfileDto(
            StudentId: profile.StudentId,
            DisplayName: profile.DisplayName ?? profile.FullName ?? "Student",
            Email: ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? "",
            AvatarUrl: null, // TODO: Avatar storage in STB-00b
            Bio: profile.Bio,
            FavoriteSubjects: profile.Subjects,
            Visibility: profile.Visibility);

        return Results.Ok(dto);
    }

    // PATCH /api/me/profile — update profile
    private static async Task<IResult> UpdateProfile(
        HttpContext ctx,
        IDocumentStore store,
        ProfilePatchDto patch)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
            return Results.NotFound();

        // Apply patch
        if (patch.DisplayName is not null)
            profile.DisplayName = patch.DisplayName;
        if (patch.Bio is not null)
            profile.Bio = patch.Bio;
        if (patch.FavoriteSubjects is not null)
            profile.Subjects = patch.FavoriteSubjects;
        if (patch.Visibility is not null)
            profile.Visibility = patch.Visibility;

        session.Store(profile);
        await session.SaveChangesAsync();

        return Results.NoContent();
    }

    // POST /api/me/onboarding — submit onboarding
    private static async Task<IResult> SubmitOnboarding(
        HttpContext ctx,
        IDocumentStore store,
        OnboardingRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
        {
            // Create new profile for this student
            profile = new StudentProfileSnapshot
            {
                StudentId = studentId,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        // Idempotency: if already onboarded, return success without re-appending event
        if (profile.OnboardedAt.HasValue)
        {
            return Results.Ok(new OnboardingResponse(Success: true, RedirectTo: "/home"));
        }

        // Create and append onboarding event
        var onboardingEvent = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: request.Role,
            Locale: request.Locale,
            Subjects: request.Subjects,
            DailyTimeGoalMinutes: request.DailyTimeGoalMinutes,
            CompletedAt: DateTimeOffset.UtcNow);

        session.Events.Append(studentId, onboardingEvent);

        // Apply to snapshot immediately (inline projection)
        profile.Apply(onboardingEvent);
        session.Store(profile);

        await session.SaveChangesAsync();

        return Results.Ok(new OnboardingResponse(Success: true, RedirectTo: "/home"));
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static int CalculateLevel(int totalXp)
    {
        // Simple level calculation: 100 XP per level
        return Math.Max(1, totalXp / 100);
    }
}
