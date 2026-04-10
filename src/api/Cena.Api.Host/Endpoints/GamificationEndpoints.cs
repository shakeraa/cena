// =============================================================================
// Cena Platform -- Gamification REST Endpoints (STB-03 Phase 1)
// Badges, XP, streak, and leaderboard endpoints (stub data)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Gamification;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class GamificationEndpoints
{
    public static IEndpointRouteBuilder MapGamificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/gamification")
            .WithTags("Gamification")
            .RequireAuthorization();

        group.MapGet("/badges", GetBadges).WithName("GetBadges");
        group.MapGet("/xp", GetXpStatus).WithName("GetXpStatus");
        group.MapGet("/streak", GetStreakStatus).WithName("GetStreakStatus");
        group.MapGet("/leaderboard", GetLeaderboard).WithName("GetLeaderboard");

        return app;
    }

    // GET /api/gamification/badges — returns earned and locked badges
    private static async Task<IResult> GetBadges(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return deterministic stub badges (same for every student)
        var earned = new[]
        {
            new Badge(
                BadgeId: "first-steps",
                Name: "First Steps",
                Description: "Complete your first learning session",
                IconName: "mdi-shoe-print",
                Tier: "bronze",
                EarnedAt: DateTime.UtcNow.AddDays(-30)),
            new Badge(
                BadgeId: "week-streak",
                Name: "Week Streak",
                Description: "Maintain a 7-day learning streak",
                IconName: "mdi-calendar-week",
                Tier: "silver",
                EarnedAt: DateTime.UtcNow.AddDays(-14)),
            new Badge(
                BadgeId: "quiz-master",
                Name: "Quiz Master",
                Description: "Answer 50 questions correctly",
                IconName: "mdi-check-circle",
                Tier: "gold",
                EarnedAt: DateTime.UtcNow.AddDays(-7))
        };

        var locked = new[]
        {
            new Badge(
                BadgeId: "month-streak",
                Name: "Month Streak",
                Description: "Maintain a 30-day learning streak",
                IconName: "mdi-calendar-month",
                Tier: "gold",
                EarnedAt: null),
            new Badge(
                BadgeId: "scholar",
                Name: "Scholar",
                Description: "Complete 100 learning sessions",
                IconName: "mdi-school",
                Tier: "platinum",
                EarnedAt: null),
            new Badge(
                BadgeId: "speed-demon",
                Name: "Speed Demon",
                Description: "Complete a session in under 10 minutes",
                IconName: "mdi-lightning-bolt",
                Tier: "silver",
                EarnedAt: null),
            new Badge(
                BadgeId: "perfectionist",
                Name: "Perfectionist",
                Description: "Get 100% on 10 quizzes in a row",
                IconName: "mdi-star",
                Tier: "gold",
                EarnedAt: null),
            new Badge(
                BadgeId: "early-bird",
                Name: "Early Bird",
                Description: "Complete 5 sessions before 8 AM",
                IconName: "mdi-weather-sunset-up",
                Tier: "bronze",
                EarnedAt: null)
        };

        return Results.Ok(new BadgeListResponse(earned, locked));
    }

    // GET /api/gamification/xp — returns XP status
    private static async Task<IResult> GetXpStatus(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        // Phase 1: Compute from TotalXp (100 XP per level base, linear)
        var totalXp = profile?.TotalXp ?? 0;
        const int xpPerLevel = 100;
        var currentLevel = Math.Max(1, totalXp / xpPerLevel) + 1;
        var currentXp = totalXp % xpPerLevel;
        var xpToNextLevel = xpPerLevel - currentXp;

        var dto = new XpStatusDto(
            CurrentLevel: currentLevel,
            CurrentXp: currentXp,
            XpToNextLevel: xpToNextLevel,
            TotalXpEarned: totalXp);

        return Results.Ok(dto);
    }

    // GET /api/gamification/streak — returns streak status
    private static async Task<IResult> GetStreakStatus(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        // Phase 1: Read from snapshot; isAtRisk if no activity in last 20h
        var currentDays = profile?.CurrentStreak ?? 0;
        var longestDays = profile?.LongestStreak ?? 0;
        var lastActivityAt = profile?.LastActivityDate;
        var isAtRisk = lastActivityAt.HasValue &&
                       (DateTimeOffset.UtcNow - lastActivityAt.Value).TotalHours > 20;

        var dto = new StreakStatusDto(
            CurrentDays: currentDays,
            LongestDays: longestDays,
            LastActivityAt: lastActivityAt?.UtcDateTime,
            IsAtRisk: isAtRisk);

        return Results.Ok(dto);
    }

    // GET /api/gamification/leaderboard?scope=class|friends|global
    private static async Task<IResult> GetLeaderboard(
        HttpContext ctx,
        IDocumentStore store,
        string? scope = "class")
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var qsession = store.QuerySession();
        var profile = await qsession.LoadAsync<StudentProfileSnapshot>(studentId);
        var displayName = profile?.DisplayName ?? profile?.FullName ?? "Student";

        // Phase 1: Return hard-coded mock entries (same for every request)
        var entries = new[]
        {
            new LeaderboardEntry(Rank: 1, StudentId: "student-001", DisplayName: "Alex", Xp: 5000, AvatarUrl: null),
            new LeaderboardEntry(Rank: 2, StudentId: "student-002", DisplayName: "Jordan", Xp: 4500, AvatarUrl: null),
            new LeaderboardEntry(Rank: 3, StudentId: "student-003", DisplayName: "Taylor", Xp: 4200, AvatarUrl: null),
            new LeaderboardEntry(Rank: 4, StudentId: "student-004", DisplayName: "Morgan", Xp: 3800, AvatarUrl: null),
            new LeaderboardEntry(Rank: 5, StudentId: "student-005", DisplayName: "Casey", Xp: 3500, AvatarUrl: null),
            new LeaderboardEntry(Rank: 6, StudentId: "student-006", DisplayName: "Riley", Xp: 3200, AvatarUrl: null),
            new LeaderboardEntry(Rank: 7, StudentId: studentId, DisplayName: displayName, Xp: profile?.TotalXp ?? 0, AvatarUrl: null),
            new LeaderboardEntry(Rank: 8, StudentId: "student-008", DisplayName: "Quinn", Xp: 2500, AvatarUrl: null),
            new LeaderboardEntry(Rank: 9, StudentId: "student-009", DisplayName: "Avery", Xp: 2200, AvatarUrl: null),
            new LeaderboardEntry(Rank: 10, StudentId: "student-010", DisplayName: "Sam", Xp: 2000, AvatarUrl: null)
        };

        var dto = new LeaderboardDto(
            Scope: scope?.ToLowerInvariant() ?? "class",
            Entries: entries,
            CurrentStudentRank: 7);

        return Results.Ok(dto);
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
