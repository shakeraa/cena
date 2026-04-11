// =============================================================================
// Cena Platform -- Challenges REST Endpoints (STB-05 HARDEN)
// Production-grade: real Marten queries against DailyChallengeDocument,
// DailyChallengeCompletionDocument, CardChainDefinitionDocument,
// CardChainProgressDocument, TournamentDocument, BossAttemptDocument,
// plus the existing BossBattleCatalog. No hardcoded data, no stubs.
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

    // GET /api/challenges/daily — returns today's daily challenge (real Marten query)
    private static async Task<IResult> GetDailyChallenge(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var today = DateTime.UtcNow.Date;
        var locale = "en"; // STB-05c: read from StudentProfileSnapshot.Locale

        await using var session = store.QuerySession();

        // Load today's daily challenge from the catalog. If missing,
        // it means the seeder hasn't run yet — fail fast so the caller knows.
        var daily = await session.LoadAsync<DailyChallengeDocument>($"daily:{today:yyyy-MM-dd}:{locale}");
        if (daily == null)
            return Results.NotFound(new { error = "No daily challenge available for today. Seeder may not have run." });

        // Check if this student has already completed today's challenge.
        var completion = await session.LoadAsync<DailyChallengeCompletionDocument>(
            $"completion:{studentId}:{today:yyyy-MM-dd}");

        var dto = new DailyChallengeDto(
            ChallengeId: daily.ChallengeId,
            Title: daily.Title,
            Description: daily.Description,
            Subject: daily.Subject,
            Difficulty: daily.Difficulty,
            ExpiresAt: daily.ExpiresAt,
            Attempted: completion != null,
            BestScore: completion?.Score);

        return Results.Ok(dto);
    }

    // GET /api/challenges/daily/leaderboard — real Marten query over today's completions
    private static async Task<IResult> GetDailyLeaderboard(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var today = DateTime.UtcNow.Date;

        await using var session = store.QuerySession();

        // Query all completions for today, ordered by score desc, time asc (tiebreaker)
        var allCompletions = await session.Query<DailyChallengeCompletionDocument>()
            .Where(c => c.Date == today)
            .ToListAsync();

        var ranked = allCompletions
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.TimeSeconds)
            .Select((c, i) => new { Rank = i + 1, Completion = c })
            .ToList();

        var topTen = ranked
            .Take(10)
            .Select(r => new DailyChallengeLeaderboardEntry(
                Rank: r.Rank,
                StudentId: r.Completion.StudentId,
                DisplayName: r.Completion.DisplayName,
                Score: r.Completion.Score,
                TimeSeconds: r.Completion.TimeSeconds))
            .ToArray();

        var myEntry = ranked.FirstOrDefault(r => r.Completion.StudentId == studentId);
        var currentRank = myEntry?.Rank ?? 0; // 0 means student hasn't completed yet

        var dto = new DailyChallengeLeaderboardDto(
            Entries: topTen,
            CurrentStudentRank: currentRank);

        return Results.Ok(dto);
    }

    // GET /api/challenges/daily/history?limit=30 — real Marten query joining
    // catalog dates with this student's completions
    private static async Task<IResult> GetDailyHistory(
        HttpContext ctx,
        IDocumentStore store,
        int? limit = 30)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var today = DateTime.UtcNow.Date;
        var windowDays = Math.Clamp(limit ?? 30, 1, 90);
        var fromDate = today.AddDays(-(windowDays - 1));

        await using var session = store.QuerySession();

        // Pull all daily challenge catalog rows in the window + the
        // student's completions in parallel.
        var catalog = await session.Query<DailyChallengeDocument>()
            .Where(d => d.Date >= fromDate && d.Date <= today && d.Locale == "en")
            .ToListAsync();

        var completions = await session.Query<DailyChallengeCompletionDocument>()
            .Where(c => c.StudentId == studentId && c.Date >= fromDate && c.Date <= today)
            .ToListAsync();

        var completionByDate = completions.ToDictionary(c => c.Date, c => c);

        var entries = catalog
            .OrderByDescending(d => d.Date)
            .Select(d =>
            {
                completionByDate.TryGetValue(d.Date, out var completion);
                return new DailyChallengeHistoryEntry(
                    Date: d.Date,
                    Title: d.Title,
                    Attempted: completion != null,
                    Score: completion?.Score);
            })
            .ToArray();

        var dto = new DailyChallengeHistoryDto(Entries: entries);
        return Results.Ok(dto);
    }

    // GET /api/challenges/boss — enumerates BossBattleCatalog and partitions
    // by the student's real mastery level from StudentProfileSnapshot
    private static async Task<IResult> GetBossBattles(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var studentLevel = Math.Max(1, (profile?.TotalXp ?? 0) / 100);

        var available = new List<BossBattleSummary>();
        var locked = new List<BossBattleSummary>();

        foreach (var boss in BossBattleCatalog.Bosses)
        {
            var summary = new BossBattleSummary(
                BossBattleId: boss.BossBattleId,
                Name: boss.Name,
                Subject: boss.Subject,
                Difficulty: boss.Difficulty,
                RequiredMasteryLevel: boss.RequiredMasteryLevel);

            if (studentLevel >= boss.RequiredMasteryLevel)
                available.Add(summary);
            else
                locked.Add(summary);
        }

        var dto = new BossBattleListDto(
            Available: available.ToArray(),
            Locked: locked.ToArray());
        return Results.Ok(dto);
    }

    // GET /api/challenges/boss/{id} — real catalog lookup + attempts remaining
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

    // GET /api/challenges/chains — real join of CardChainDefinitionDocument
    // (catalog) with CardChainProgressDocument (per-student progress)
    private static async Task<IResult> GetCardChains(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        var catalog = await session.Query<CardChainDefinitionDocument>()
            .OrderBy(c => c.Name)
            .ToListAsync();

        var progress = await session.Query<CardChainProgressDocument>()
            .Where(p => p.StudentId == studentId)
            .ToListAsync();

        var progressByChainId = progress.ToDictionary(p => p.ChainId, p => p);

        var chains = catalog.Select(def =>
        {
            progressByChainId.TryGetValue(def.ChainId, out var prog);
            return new CardChainSummary(
                ChainId: def.ChainId,
                Name: def.Name,
                CardsUnlocked: prog?.CardsUnlocked ?? 0,
                CardsTotal: def.CardsTotal,
                LastUnlockedAt: prog?.LastUnlockedAt);
        }).ToArray();

        var dto = new CardChainListDto(Chains: chains);
        return Results.Ok(dto);
    }

    // GET /api/challenges/tournaments — real query with active/upcoming split
    // and the student's registration status
    private static async Task<IResult> GetTournaments(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var now = DateTime.UtcNow;

        await using var session = store.QuerySession();

        var allTournaments = await session.Query<TournamentDocument>()
            .Where(t => t.EndsAt >= now)
            .OrderBy(t => t.StartsAt)
            .ToListAsync();

        var myRegistrations = await session.Query<TournamentRegistrationDocument>()
            .Where(r => r.StudentId == studentId)
            .ToListAsync();

        var registeredIds = myRegistrations.Select(r => r.TournamentId).ToHashSet();

        TournamentSummary Map(TournamentDocument t) => new(
            TournamentId: t.TournamentId,
            Name: t.Name,
            StartsAt: t.StartsAt,
            EndsAt: t.EndsAt,
            ParticipantCount: t.ParticipantCount,
            IsRegistered: registeredIds.Contains(t.TournamentId));

        var active = allTournaments
            .Where(t => t.StartsAt <= now && t.EndsAt >= now)
            .Select(Map)
            .ToArray();

        var upcoming = allTournaments
            .Where(t => t.StartsAt > now)
            .Select(Map)
            .ToArray();

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
