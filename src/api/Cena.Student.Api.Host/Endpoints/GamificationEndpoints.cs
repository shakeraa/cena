// =============================================================================
// Cena Platform -- Gamification REST Endpoints (STB-03 Phase 1b + STB-03c)
// Badges, XP, streak, and leaderboard endpoints with real data
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Gamification;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Gamification;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Api.Host.Endpoints;

public static class GamificationEndpoints
{
    public static IEndpointRouteBuilder MapGamificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/gamification")
            .WithTags("Gamification")
            .RequireAuthorization();

        group.MapGet("/badges", GetBadges).WithName("GetBadges")
    .Produces<XpStatusDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/xp", GetXpStatus).WithName("GetXpStatus")
    .Produces<XpStatusDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/streak", GetStreakStatus).WithName("GetStreakStatus")
    .Produces<StreakStatusDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/leaderboard", GetLeaderboard).WithName("GetLeaderboard");
        group.MapGet("/leaderboard/ranks", GetLeaderboardRanks).WithName("GetLeaderboardRanks")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // GET /api/gamification/badges — returns earned and locked badges (STB-03c)
    // FIND-data-028: Uses real BadgeEarned_v1 events instead of fabricated timestamps
    private static async Task<IResult> GetBadges(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // FIND-data-028: Query only this student's events instead of global scan
        var studentEvents = await session.Events.FetchStreamAsync(studentId);

        // Get real badge earned events with actual timestamps
        var badgeEarnedEvents = studentEvents
            .Where(e => e.Data is BadgeEarned_V1)
            .Select(e => (BadgeEarned_V1)e.Data)
            .ToList();

        // Build lookup of earned badges by badge ID
        var earnedBadgeLookup = badgeEarnedEvents
            .GroupBy(b => b.BadgeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(b => b.Timestamp).First().Timestamp);

        // Get stats from student's own events only
        var sessionCount = studentEvents
            .Count(e => e.Data is LearningSessionStarted_V1);

        var studentAttempts = studentEvents
            .Where(e => e.Data is ConceptAttempted_V1)
            .Select(e => (ConceptAttempted_V1)e.Data)
            .ToList();
        var correctAnswers = studentAttempts.Count(a => a.IsCorrect);

        var currentStreak = await CalculateCurrentStreak(session, studentId);

        // Get friend count
        var friendCount = await session.Query<FriendshipDocument>()
            .CountAsync(f => f.StudentAId == studentId || f.StudentBId == studentId);

        // Get bosses defeated from student's challenge events
        var bossesDefeated = studentEvents
            .Count(e => e.Data is ChallengeCompleted_V1);

        // Get student profile for mastered concepts
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        // Evaluate all badges from catalog
        var earned = new List<Badge>();
        var locked = new List<Badge>();

        foreach (var badgeDef in BadgeCatalog.All)
        {
            var isEarned = EvaluateBadge(badgeDef,
                sessionCount,
                correctAnswers,
                currentStreak,
                friendCount,
                bossesDefeated,
                profile);

            // FIND-data-028: Use real timestamp from BadgeEarned_v1 event
            var earnedTimestamp = earnedBadgeLookup.TryGetValue(badgeDef.Id, out var ts) ? ts : (DateTimeOffset?)null;

            if (isEarned && earnedTimestamp.HasValue)
            {
                earned.Add(new Badge(
                    BadgeId: badgeDef.Id,
                    Name: badgeDef.Name,
                    Description: badgeDef.Description,
                    IconName: badgeDef.IconName,
                    Tier: badgeDef.Tier,
                    EarnedAt: earnedTimestamp.Value.UtcDateTime));
            }
            else
            {
                locked.Add(new Badge(
                    BadgeId: badgeDef.Id,
                    Name: badgeDef.Name,
                    Description: badgeDef.Description,
                    IconName: badgeDef.IconName,
                    Tier: badgeDef.Tier,
                    EarnedAt: null));
            }
        }

        return Results.Ok(new {
            earned = earned.OrderByDescending(b => b.EarnedAt).ToArray(),
            locked = locked.ToArray(),
            totalCount = BadgeCatalog.All.Count });
    }

    // GET /api/gamification/xp — returns XP status (real data)
    private static async Task<IResult> GetXpStatus(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Get real XP from profile snapshot
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var totalXp = profile?.TotalXp ?? 0;
        var level = ComputeLevel(totalXp);
        
        // Calculate XP for current level and next level
        var xpForCurrentLevel = ComputeXpForLevel(level);
        var xpForNextLevel = ComputeXpForLevel(level + 1);
        var xpInCurrentLevel = totalXp - xpForCurrentLevel;
        var xpNeededForNextLevel = xpForNextLevel - xpForCurrentLevel;
        var progressPercent = xpNeededForNextLevel > 0 
            ? (int)((double)xpInCurrentLevel / xpNeededForNextLevel * 100)
            : 100;

        var dto = new XpStatusDto(
            CurrentLevel: level,
            CurrentXp: xpInCurrentLevel,
            XpToNextLevel: xpNeededForNextLevel - xpInCurrentLevel,
            TotalXpEarned: totalXp);

        return Results.Ok(dto);
    }

    // GET /api/gamification/streak — returns streak status (real events)
    private static async Task<IResult> GetStreakStatus(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Compute streak from real LearningSessionStarted_V1 events
        var currentStreak = await CalculateCurrentStreak(session, studentId);
        
        // Get longest streak from profile
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var longestStreak = profile?.LongestStreak ?? currentStreak;

        // Get last activity date from events
        var lastSessionEvent = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "learning_session_started_v1")
            .Where(e => e.StreamKey == studentId)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();

        var lastActivityAt = lastSessionEvent?.Timestamp;
        var isAtRisk = lastActivityAt.HasValue &&
                       (DateTimeOffset.UtcNow - lastActivityAt.Value).TotalHours > 20;

        var dto = new StreakStatusDto(
            CurrentDays: currentStreak,
            LongestDays: longestStreak,
            LastActivityAt: lastActivityAt?.UtcDateTime,
            IsAtRisk: isAtRisk);

        return Results.Ok(dto);
    }

    // GET /api/gamification/leaderboard?scope=global|class|friends (STB-03c)
    private static async Task<IResult> GetLeaderboard(
        HttpContext ctx,
        [FromServices] ILeaderboardService leaderboardService,
        [FromServices] IDocumentStore store,
        string? scope = "class",
        int? limit = 50)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        LeaderboardView view;

        switch (scope?.ToLowerInvariant())
        {
            case "global":
                view = await leaderboardService.GetGlobalLeaderboardAsync(limit ?? 100);
                break;
            case "friends":
                view = await leaderboardService.GetFriendsLeaderboardAsync(studentId, limit ?? 50);
                break;
            case "class":
            default:
                // Try to find student's class by school
                await using (var session = store.QuerySession())
                {
                    var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
                    if (!string.IsNullOrEmpty(profile?.SchoolId))
                    {
                        var classroom = await session.Query<ClassroomDocument>()
                            .Where(c => c.SchoolId == profile.SchoolId)
                            .FirstOrDefaultAsync();
                        
                        if (classroom != null)
                        {
                            view = await leaderboardService.GetClassLeaderboardAsync(classroom.Id, limit ?? 50);
                        }
                        else
                        {
                            view = await leaderboardService.GetGlobalLeaderboardAsync(limit ?? 50);
                        }
                    }
                    else
                    {
                        view = await leaderboardService.GetGlobalLeaderboardAsync(limit ?? 50);
                    }
                }
                break;
        }

        // Convert to DTO with ranks
        var entries = view.Entries
            .Select((e, i) => new Contracts.Gamification.LeaderboardEntry(
                Rank: i + 1,
                StudentId: e.StudentId,
                DisplayName: e.DisplayName,
                Xp: e.TotalXp,
                AvatarUrl: e.AvatarUrl))
            .ToArray();

        var currentStudentRank = entries.FirstOrDefault(e => e.StudentId == studentId)?.Rank ?? 0;

        var dto = new LeaderboardDto(
            Scope: scope?.ToLowerInvariant() ?? "class",
            Entries: entries,
            CurrentStudentRank: currentStudentRank);

        return Results.Ok(dto);
    }

    // GET /api/gamification/leaderboard/ranks (STB-03c)
    private static async Task<IResult> GetLeaderboardRanks(
        HttpContext ctx,
        [FromServices] ILeaderboardService leaderboardService)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var ranks = await leaderboardService.GetStudentRanksAsync(studentId);

        return Results.Ok(new
        {
            GlobalRank = ranks.GlobalRank?.Rank,
            GlobalTotalXp = ranks.GlobalRank?.TotalXp,
            ClassRank = ranks.ClassRank?.Rank,
            ClassTotalXp = ranks.ClassRank?.TotalXp,
            FriendsRank = ranks.FriendsRank?.Rank,
            FriendsTotalXp = ranks.FriendsRank?.TotalXp
        });
    }

    // Helper: Evaluate if a badge is earned
    private static bool EvaluateBadge(
        BadgeDefinition badge,
        int sessionCount,
        int correctAnswers,
        int currentStreak,
        int friendCount,
        int bossesDefeated,
        StudentProfileSnapshot? profile)
    {
        return badge.Criteria.Type switch
        {
            BadgeCriteriaType.SessionCount => sessionCount >= badge.Criteria.Threshold,
            BadgeCriteriaType.CorrectAnswers => correctAnswers >= badge.Criteria.Threshold,
            BadgeCriteriaType.StreakDays => currentStreak >= badge.Criteria.Threshold,
            BadgeCriteriaType.FriendsCount => friendCount >= badge.Criteria.Threshold,
            BadgeCriteriaType.BossesDefeated => bossesDefeated >= badge.Criteria.Threshold,
            BadgeCriteriaType.MasteredConcepts => (profile?.ConceptMastery.Count(c => c.Value.IsMastered) ?? 0) >= badge.Criteria.Threshold,
            _ => false
        };
    }

    // Helper: Calculate current streak from LearningSessionStarted_V1 events
    // FIND-data-028: Uses FetchStreamAsync instead of QueryAllRawEvents
    private static async Task<int> CalculateCurrentStreak(IQuerySession session, string studentId)
    {
        var studentStream = await session.Events.FetchStreamAsync(studentId);
        var sessionEvents = studentStream
            .Where(e => e.Data is LearningSessionStarted_V1)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        if (sessionEvents.Count == 0)
            return 0;

        // Get unique dates with sessions
        var sessionDates = sessionEvents
            .Select(e => e.Timestamp.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        if (sessionDates.Count == 0)
            return 0;

        // Count consecutive days from today backwards
        var today = DateTime.UtcNow.Date;
        var streak = 0;
        var checkDate = today;

        // If no session today, check if there was one yesterday to maintain streak
        if (sessionDates[0] < today.AddDays(-1))
            return 0;

        foreach (var date in sessionDates)
        {
            if (date == checkDate || date == checkDate.AddDays(-1))
            {
                if (date == checkDate.AddDays(-1))
                    checkDate = date;
                streak++;
                checkDate = checkDate.AddDays(-1);
            }
            else
            {
                break;
            }
        }

        return streak;
    }

    // Helper: Compute level from XP
    private static int ComputeLevel(int totalXp)
    {
        if (totalXp <= 0) return 1;
        var level = (int)((1 + Math.Sqrt(1 + 8 * totalXp / 100.0)) / 2);
        return Math.Max(1, level);
    }

    // Helper: Compute XP required for a level
    private static int ComputeXpForLevel(int level)
    {
        if (level <= 1) return 0;
        // Formula: xp = 50 * (level^2 - level)
        return 50 * (level * level - level);
    }

    private static bool ExtractBool(dynamic evt, string propertyName)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return false;
            var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(propertyName, out var prop) ||
                json.RootElement.TryGetProperty(char.ToUpperInvariant(propertyName[0]) + propertyName.Substring(1), out prop))
                return prop.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch { }
        return false;
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
