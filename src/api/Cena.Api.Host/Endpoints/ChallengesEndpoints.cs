// =============================================================================
// Cena Platform -- Challenges REST Endpoints (STB-05 Phase 1)
// Daily challenges, boss battles, card chains, and tournaments (stub data)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Challenges;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Challenges;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class ChallengesEndpoints
{
    public static IEndpointRouteBuilder MapChallengesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/challenges")
            .WithTags("Challenges")
            .RequireAuthorization();

        // Daily challenge endpoints
        group.MapGet("/daily", GetDailyChallenge).WithName("GetDailyChallenge");
        group.MapPost("/daily/start", StartDailyChallenge).WithName("StartDailyChallenge");
        group.MapGet("/daily/leaderboard", GetDailyLeaderboard).WithName("GetDailyLeaderboard");
        group.MapGet("/daily/history", GetDailyHistory).WithName("GetDailyHistory");

        // Boss battle endpoints
        group.MapGet("/boss", GetBossBattles).WithName("GetBossBattles");
        group.MapGet("/boss/{id}", GetBossBattleDetail).WithName("GetBossBattleDetail");
        group.MapPost("/boss/{id}/start", StartBossBattle).WithName("StartBossBattle");

        // Card chain endpoints
        group.MapGet("/chains", GetCardChains).WithName("GetCardChains");

        // Tournament endpoints
        group.MapGet("/tournaments", GetTournaments).WithName("GetTournaments");

        return app;
    }

    // GET /api/challenges/daily — returns today's daily challenge
    private static async Task<IResult> GetDailyChallenge(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return fixed "Mental Math Sprint" challenge that expires at end-of-day UTC
        var today = DateTime.UtcNow.Date;
        var expiresAt = today.AddDays(1).AddTicks(-1); // End of today

        var dto = new DailyChallengeDto(
            ChallengeId: "daily_math_001",
            Title: "Mental Math Sprint",
            Description: "Solve 20 arithmetic problems as fast as you can! Test your speed and accuracy with addition, subtraction, multiplication, and division.",
            Subject: "Mathematics",
            Difficulty: "medium",
            ExpiresAt: expiresAt,
            Attempted: false, // Phase 1: always false
            BestScore: null);

        return Results.Ok(dto);
    }

    // GET /api/challenges/daily/leaderboard — returns today's leaderboard
    private static async Task<IResult> GetDailyLeaderboard(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var displayName = profile?.DisplayName ?? profile?.FullName ?? "Student";

        // Phase 1: Return 10 hardcoded mock entries + current student at rank 5
        var entries = new[]
        {
            new DailyChallengeLeaderboardEntry(Rank: 1, StudentId: "student_001", DisplayName: "SpeedDemon", Score: 2000, TimeSeconds: 45),
            new DailyChallengeLeaderboardEntry(Rank: 2, StudentId: "student_002", DisplayName: "MathWizard", Score: 1950, TimeSeconds: 48),
            new DailyChallengeLeaderboardEntry(Rank: 3, StudentId: "student_003", DisplayName: "QuickThinker", Score: 1900, TimeSeconds: 52),
            new DailyChallengeLeaderboardEntry(Rank: 4, StudentId: "student_004", DisplayName: "Brainiac", Score: 1850, TimeSeconds: 55),
            new DailyChallengeLeaderboardEntry(Rank: 5, StudentId: studentId, DisplayName: displayName, Score: 1800, TimeSeconds: 58),
            new DailyChallengeLeaderboardEntry(Rank: 6, StudentId: "student_006", DisplayName: "NumberNinja", Score: 1750, TimeSeconds: 62),
            new DailyChallengeLeaderboardEntry(Rank: 7, StudentId: "student_007", DisplayName: "CalcKing", Score: 1700, TimeSeconds: 65),
            new DailyChallengeLeaderboardEntry(Rank: 8, StudentId: "student_008", DisplayName: "AlgebraAce", Score: 1650, TimeSeconds: 68),
            new DailyChallengeLeaderboardEntry(Rank: 9, StudentId: "student_009", DisplayName: "GeoGenius", Score: 1600, TimeSeconds: 72),
            new DailyChallengeLeaderboardEntry(Rank: 10, StudentId: "student_010", DisplayName: "TrigMaster", Score: 1550, TimeSeconds: 75)
        };

        var dto = new DailyChallengeLeaderboardDto(
            Entries: entries,
            CurrentStudentRank: 5);

        return Results.Ok(dto);
    }

    // GET /api/challenges/daily/history?limit=30 — returns challenge history
    private static async Task<IResult> GetDailyHistory(
        HttpContext ctx,
        IDocumentStore store,
        int? limit = 30)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return 7 entries, one per day, current day attempted=true
        var today = DateTime.UtcNow.Date;
        var entries = new[]
        {
            new DailyChallengeHistoryEntry(Date: today, Title: "Mental Math Sprint", Attempted: true, Score: 1800),
            new DailyChallengeHistoryEntry(Date: today.AddDays(-1), Title: "Fraction Frenzy", Attempted: true, Score: 1650),
            new DailyChallengeHistoryEntry(Date: today.AddDays(-2), Title: "Geometry Dash", Attempted: false, Score: null),
            new DailyChallengeHistoryEntry(Date: today.AddDays(-3), Title: "Algebra Challenge", Attempted: true, Score: 1900),
            new DailyChallengeHistoryEntry(Date: today.AddDays(-4), Title: "Word Problems", Attempted: true, Score: 1550),
            new DailyChallengeHistoryEntry(Date: today.AddDays(-5), Title: "Pattern Recognition", Attempted: false, Score: null),
            new DailyChallengeHistoryEntry(Date: today.AddDays(-6), Title: "Logic Puzzles", Attempted: true, Score: 1700)
        };

        var dto = new DailyChallengeHistoryDto(Entries: entries);
        return Results.Ok(dto);
    }

    // GET /api/challenges/boss — returns available and locked boss battles
    private static async Task<IResult> GetBossBattles(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return 3 available, 2 locked
        var available = new[]
        {
            new BossBattleSummary(
                BossBattleId: "boss_algebra_001",
                Name: "The Algebraic Dragon",
                Subject: "Mathematics",
                Difficulty: "medium",
                RequiredMasteryLevel: 5),
            new BossBattleSummary(
                BossBattleId: "boss_physics_001",
                Name: "Newton's Nemesis",
                Subject: "Physics",
                Difficulty: "hard",
                RequiredMasteryLevel: 8),
            new BossBattleSummary(
                BossBattleId: "boss_chemistry_001",
                Name: "The Elemental Guardian",
                Subject: "Chemistry",
                Difficulty: "medium",
                RequiredMasteryLevel: 6)
        };

        var locked = new[]
        {
            new BossBattleSummary(
                BossBattleId: "boss_calculus_001",
                Name: "The Calculus Colossus",
                Subject: "Mathematics",
                Difficulty: "expert",
                RequiredMasteryLevel: 15),
            new BossBattleSummary(
                BossBattleId: "boss_biology_001",
                Name: "The Bio-Behemoth",
                Subject: "Biology",
                Difficulty: "hard",
                RequiredMasteryLevel: 12)
        };

        var dto = new BossBattleListDto(Available: available, Locked: locked);
        return Results.Ok(dto);
    }

    // GET /api/challenges/boss/{id} — returns boss battle details (Phase 1b: real catalog + attempts)
    private static async Task<IResult> GetBossBattleDetail(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Look up boss in catalog
        var boss = BossBattleCatalog.GetById(id);
        if (boss is null)
            return Results.NotFound(new { error = "Boss battle not found" });

        // Get attempts remaining
        await using var session = store.QuerySession();
        var attemptDoc = await session.Query<BossAttemptDocument>()
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.BossBattleId == id && a.Date == DateTime.UtcNow.Date);

        var attemptsRemaining = attemptDoc?.AttemptsRemaining ?? boss.MaxAttemptsPerDay;

        var dto = new BossBattleDetailDto(
            BossBattleId: id,
            Name: boss.Name,
            Description: boss.Description,
            Subject: boss.Subject,
            Difficulty: boss.Difficulty,
            AttemptsRemaining: attemptsRemaining,
            AttemptsMax: boss.MaxAttemptsPerDay,
            Rewards: boss.Rewards.Select(r => new Contracts.Challenges.BossBattleReward(Type: r.Type, Amount: r.Amount)).ToArray());

        return Results.Ok(dto);
    }

    // GET /api/challenges/chains — returns card chains
    private static async Task<IResult> GetCardChains(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return 2 active chains
        var chains = new[]
        {
            new CardChainSummary(
                ChainId: "chain_algebra_fundamentals",
                Name: "Algebra Fundamentals",
                CardsUnlocked: 12,
                CardsTotal: 20,
                LastUnlockedAt: DateTime.UtcNow.AddDays(-2)),
            new CardChainSummary(
                ChainId: "chain_physics_core",
                Name: "Physics Core",
                CardsUnlocked: 8,
                CardsTotal: 15,
                LastUnlockedAt: DateTime.UtcNow.AddDays(-5))
        };

        var dto = new CardChainListDto(Chains: chains);
        return Results.Ok(dto);
    }

    // GET /api/challenges/tournaments — returns tournaments
    private static async Task<IResult> GetTournaments(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return 1 upcoming, 1 active
        var upcoming = new[]
        {
            new TournamentSummary(
                TournamentId: "tournament_spring_math_2026",
                Name: "Spring Math Championship 2026",
                StartsAt: DateTime.UtcNow.AddDays(7),
                EndsAt: DateTime.UtcNow.AddDays(14),
                ParticipantCount: 0,
                IsRegistered: false)
        };

        var active = new[]
        {
            new TournamentSummary(
                TournamentId: "tournament_weekly_blitz_042",
                Name: "Weekly Math Blitz #42",
                StartsAt: DateTime.UtcNow.AddDays(-2),
                EndsAt: DateTime.UtcNow.AddDays(5),
                ParticipantCount: 156,
                IsRegistered: true)
        };

        var dto = new TournamentListDto(Upcoming: upcoming, Active: active);
        return Results.Ok(dto);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // STB-05b: Write Endpoints
    // ═════════════════════════════════════════════════════════════════════════

    // POST /api/challenges/daily/start — begin today's daily challenge
    private static async Task<IResult> StartDailyChallenge(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        // Create a new learning session for the challenge
        var sessionId = $"challenge_daily_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var expiresAt = now.Date.AddDays(1).AddTicks(-1);

        // Append ChallengeStarted event
        var challengeEvent = new ChallengeStarted_V1(
            StudentId: studentId,
            ChallengeId: "daily_math_001",
            Kind: "daily",
            BossBattleId: null,
            StartedAt: now);

        session.Events.Append(studentId, challengeEvent);

        // Also start a learning session
        var learningSessionEvent = new LearningSessionStarted_V1(
            StudentId: studentId,
            SessionId: sessionId,
            Subjects: new[] { "Mathematics" },
            Mode: "challenge",
            DurationMinutes: 15,
            StartedAt: now);

        session.Events.Append(studentId, learningSessionEvent);

        await session.SaveChangesAsync();

        return Results.Ok(new
        {
            SessionId = sessionId,
            ChallengeId = "daily_math_001",
            ExpiresAt = expiresAt
        });
    }

    // POST /api/challenges/boss/{id}/start — begin a boss battle
    private static async Task<IResult> StartBossBattle(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Look up boss in catalog
        var boss = BossBattleCatalog.GetById(id);
        if (boss is null)
            return Results.NotFound(new { error = "Boss battle not found" });

        await using var session = store.LightweightSession();

        // Check if student meets mastery requirement
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var studentLevel = Math.Max(1, (profile?.TotalXp ?? 0) / 100);

        if (studentLevel < boss.RequiredMasteryLevel)
        {
            return Results.StatusCode(403);
        }

        // Check/get attempts
        var today = DateTime.UtcNow.Date;
        var attemptDoc = await session.Query<BossAttemptDocument>()
            .FirstOrDefaultAsync(a => a.StudentId == studentId && a.BossBattleId == id && a.Date == today);

        if (attemptDoc == null)
        {
            attemptDoc = new BossAttemptDocument
            {
                Id = $"boss_attempt_{studentId}_{id}_{today:yyyyMMdd}",
                StudentId = studentId,
                BossBattleId = id,
                Date = today,
                AttemptsUsed = 0,
                AttemptsMax = boss.MaxAttemptsPerDay
            };
        }
        else if (!attemptDoc.HasAttemptsRemaining)
        {
            return Results.StatusCode(429); // Too many requests / attempts exhausted
        }

        // Consume attempt
        attemptDoc.AttemptsUsed++;
        attemptDoc.LastAttemptAt = DateTime.UtcNow;
        session.Store(attemptDoc);

        // Create learning session
        var sessionId = $"challenge_boss_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        // Append events
        var challengeEvent = new ChallengeStarted_V1(
            StudentId: studentId,
            ChallengeId: id,
            Kind: "boss",
            BossBattleId: id,
            StartedAt: now);

        session.Events.Append(studentId, challengeEvent);

        var bossAttemptEvent = new BossAttemptConsumed_V1(
            StudentId: studentId,
            BossBattleId: id,
            AttemptsRemaining: attemptDoc.AttemptsRemaining,
            ConsumedAt: now);

        session.Events.Append(studentId, bossAttemptEvent);

        var learningSessionEvent = new LearningSessionStarted_V1(
            StudentId: studentId,
            SessionId: sessionId,
            Subjects: new[] { boss.Subject },
            Mode: "challenge",
            DurationMinutes: 30,
            StartedAt: now);

        session.Events.Append(studentId, learningSessionEvent);

        await session.SaveChangesAsync();

        return Results.Ok(new
        {
            SessionId = sessionId,
            BossBattleId = id,
            AttemptsRemaining = attemptDoc.AttemptsRemaining
        });
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
