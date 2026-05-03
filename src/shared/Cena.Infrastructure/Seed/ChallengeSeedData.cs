// =============================================================================
// Cena Platform -- Challenge Seed Data (STB-05 HARDEN)
// Idempotent seeder for challenge catalog docs. Populates today's daily
// challenge, baseline card chain definitions, and a pair of tournaments
// on first boot. Subsequent boots skip if rows already exist.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

public static class ChallengeSeedData
{
    public static async Task SeedAsync(
        IDocumentStore store,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();

        await SeedDailyChallengeAsync(session, logger, ct);
        await SeedCardChainsAsync(session, logger, ct);
        await SeedTournamentsAsync(session, logger, ct);

        await session.SaveChangesAsync(ct);
    }

    // ── Daily challenge for today (idempotent by date) ─────────────────
    private static async Task SeedDailyChallengeAsync(
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var id = $"daily:{today:yyyy-MM-dd}:en";

        var existing = await session.LoadAsync<DailyChallengeDocument>(id, ct);
        if (existing != null)
            return;

        session.Store(new DailyChallengeDocument
        {
            Id = id,
            Date = today,
            Locale = "en",
            ChallengeId = $"daily_math_{today:yyyyMMdd}",
            Title = "Mental Math Sprint",
            Description = "Solve 20 arithmetic problems as fast as you can! Test your speed and accuracy with addition, subtraction, multiplication, and division.",
            Subject = "Mathematics",
            Difficulty = "medium",
            ExpiresAt = today.AddDays(1).AddTicks(-1),
            QuestionIds = Array.Empty<string>(),
        });

        logger.LogInformation("ChallengeSeedData: seeded daily challenge for {Date}", today);
    }

    // ── Card chains (idempotent by chain id) ──────────────────────────
    private static async Task SeedCardChainsAsync(
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var chains = new[]
        {
            new CardChainDefinitionDocument
            {
                Id = "chain_algebra_fundamentals",
                ChainId = "chain_algebra_fundamentals",
                Name = "Algebra Fundamentals",
                Description = "Master variables, expressions, and linear equations through a progressive 20-card sequence.",
                Subject = "Mathematics",
                CardsTotal = 20,
                ConceptIds = new[] { "math-algebra", "math-quadratics" },
            },
            new CardChainDefinitionDocument
            {
                Id = "chain_physics_core",
                ChainId = "chain_physics_core",
                Name = "Physics Core",
                Description = "The building blocks of mechanics: kinematics, forces, energy, and waves.",
                Subject = "Physics",
                CardsTotal = 15,
                ConceptIds = new[] { "physics-kinematics", "physics-forces", "physics-energy" },
            },
            new CardChainDefinitionDocument
            {
                Id = "chain_chemistry_atoms",
                ChainId = "chain_chemistry_atoms",
                Name = "Atomic Foundations",
                Description = "From atomic structure to chemical bonds.",
                Subject = "Chemistry",
                CardsTotal = 12,
                ConceptIds = new[] { "chem-atoms", "chem-bonds" },
            },
        };

        foreach (var chain in chains)
        {
            var existing = await session.LoadAsync<CardChainDefinitionDocument>(chain.Id, ct);
            if (existing == null)
                session.Store(chain);
        }

        logger.LogInformation("ChallengeSeedData: seeded {Count} card chain definitions", chains.Length);
    }

    // ── Tournaments: 1 active (current week) + 1 upcoming (next week) ─
    private static async Task SeedTournamentsAsync(
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var active = new TournamentDocument
        {
            Id = "tournament_weekly_blitz",
            TournamentId = "tournament_weekly_blitz",
            Name = "Weekly Math Blitz",
            Description = "A week-long speed competition across daily challenges. Top 10 earn XP bonuses.",
            Subject = "Mathematics",
            StartsAt = now.Date.AddDays(-(int)now.Date.DayOfWeek), // Sunday start
            EndsAt = now.Date.AddDays(7 - (int)now.Date.DayOfWeek).AddTicks(-1),
            IsActive = true,
            ParticipantCount = 0,
        };

        var upcoming = new TournamentDocument
        {
            Id = "tournament_spring_math_2026",
            TournamentId = "tournament_spring_math_2026",
            Name = "Spring Math Championship 2026",
            Description = "Our flagship seasonal event. Seven days of increasing difficulty with leaderboard prizes.",
            Subject = "Mathematics",
            StartsAt = now.Date.AddDays(7),
            EndsAt = now.Date.AddDays(14),
            IsActive = false,
            ParticipantCount = 0,
        };

        foreach (var tournament in new[] { active, upcoming })
        {
            var existing = await session.LoadAsync<TournamentDocument>(tournament.Id, ct);
            if (existing == null)
                session.Store(tournament);
        }

        logger.LogInformation("ChallengeSeedData: seeded 2 tournaments (active + upcoming)");
    }
}
