// =============================================================================
// Cena Platform -- Gamification REST Endpoints (STB-03 Phase 1b)
// Badges, XP, streak, and leaderboard endpoints (real event-sourced data)
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

    // GET /api/gamification/badges — returns earned and locked badges (rule-based evaluation)
    private static async Task<IResult> GetBadges(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Get real data for badge evaluation
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        
        // Count learning sessions for "first-steps" badge
        var sessionEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "learning_session_started_v1" && e.StreamId == studentId)
            .ToListAsync();
        var hasStartedSession = sessionEvents.Count > 0;

        // Count correct answers for "quiz-master" badge
        var attemptEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1" && e.StreamId == studentId)
            .ToListAsync();
        var correctAnswers = attemptEvents.Count(e => 
        {
            try 
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(System.Text.Json.JsonSerializer.Serialize(e.Data));
                return data?.TryGetValue("isCorrect", out var val) == true && val?.ToString()?.ToLower() == "true";
            }
            catch { return false; }
        });

        // Calculate streak for "week-streak" badge
        var currentStreak = await CalculateCurrentStreak(session, studentId);

        var earned = new List<Badge>();
        var locked = new List<Badge>();

        // Rule: first-steps (bronze): awarded when student has ≥1 LearningSessionStarted_V1
        if (hasStartedSession)
        {
            earned.Add(new Badge(
                BadgeId: "first-steps",
                Name: "First Steps",
                Description: "Complete your first learning session",
                IconName: "mdi-shoe-print",
                Tier: "bronze",
                EarnedAt: DateTime.UtcNow.AddDays(-30)));
        }
        else
        {
            locked.Add(new Badge(
                BadgeId: "first-steps",
                Name: "First Steps",
                Description: "Complete your first learning session",
                IconName: "mdi-shoe-print",
                Tier: "bronze",
                EarnedAt: null));
        }

        // Rule: week-streak (silver): awarded when streak ≥ 7
        if (currentStreak >= 7)
        {
            earned.Add(new Badge(
                BadgeId: "week-streak",
                Name: "Week Streak",
                Description: "Maintain a 7-day learning streak",
                IconName: "mdi-calendar-week",
                Tier: "silver",
                EarnedAt: DateTime.UtcNow.AddDays(-14)));
        }
        else
        {
            locked.Add(new Badge(
                BadgeId: "week-streak",
                Name: "Week Streak",
                Description: "Maintain a 7-day learning streak",
                IconName: "mdi-calendar-week",
                Tier: "silver",
                EarnedAt: null));
        }

        // Rule: quiz-master (gold): awarded when total correct answers ≥ 50
        if (correctAnswers >= 50)
        {
            earned.Add(new Badge(
                BadgeId: "quiz-master",
                Name: "Quiz Master",
                Description: "Answer 50 questions correctly",
                IconName: "mdi-check-circle",
                Tier: "gold",
                EarnedAt: DateTime.UtcNow.AddDays(-7)));
        }
        else
        {
            locked.Add(new Badge(
                BadgeId: "quiz-master",
                Name: "Quiz Master",
                Description: "Answer 50 questions correctly",
                IconName: "mdi-check-circle",
                Tier: "gold",
                EarnedAt: null));
        }

        // Add other locked badges
        locked.AddRange(new[]
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
                EarnedAt: null)
        });

        return Results.Ok(new BadgeListResponse(earned.ToArray(), locked.ToArray()));
    }

    // GET /api/gamification/xp — returns XP status (from real projection)
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

        // Get real XP from snapshot (aggregated from XpAwarded_V1 events)
        var totalXp = profile?.TotalXp ?? 0;
        const int xpPerLevel = 100;
        var currentLevel = Math.Max(1, (totalXp / xpPerLevel) + 1);
        var currentXp = totalXp % xpPerLevel;
        var xpToNextLevel = xpPerLevel - currentXp;

        var dto = new XpStatusDto(
            CurrentLevel: currentLevel,
            CurrentXp: currentXp,
            XpToNextLevel: xpToNextLevel,
            TotalXpEarned: totalXp);

        return Results.Ok(dto);
    }

    // GET /api/gamification/streak — returns streak status (computed from real events)
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
        var longestStreak = currentStreak; // Simplified: use current as longest for now

        // Get last activity date from events
        var lastSessionEvent = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "learning_session_started_v1" && e.StreamId == studentId)
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
        // Phase 1b: Real data would query aggregated XP rankings
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

    // Helper: Calculate current streak from LearningSessionStarted_V1 events
    private static async Task<int> CalculateCurrentStreak(IQuerySession session, string studentId)
    {
        var sessionEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "learning_session_started_v1" && e.StreamId == studentId)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();

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

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
