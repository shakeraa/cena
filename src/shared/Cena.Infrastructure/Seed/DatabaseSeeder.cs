// =============================================================================
// Cena Platform -- Database Seeder (Single Entry Point)
// Orchestrates all seed data in correct dependency order.
// Call DatabaseSeeder.SeedAllAsync() from any host's startup.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Single entry point for all database seeding. Executes seeds in dependency order:
///   1. Roles (required by users for role validation)
///   2. Demo users (admins, teachers, parents, hand-crafted students)
///   3. Simulated students (100 students across 8 archetypes, 60-day history)
///   4. Additional seeds (questions, etc.) via optional delegates
///
/// All seeds are idempotent (upsert) — safe to re-run on every startup.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seed all core data. Call from Program.cs ApplicationStarted handler.
    /// </summary>
    /// <param name="store">Marten document store.</param>
    /// <param name="logger">Logger for seed progress.</param>
    /// <param name="simulatedStudentCount">Number of simulated students (default 100).</param>
    /// <param name="additionalSeeds">
    /// Optional extra seed functions (e.g., QuestionBankSeedData.SeedQuestionsAsync)
    /// that depend on types not available in Cena.Infrastructure.
    /// </param>
    public static async Task SeedAllAsync(
        IDocumentStore store,
        ILogger logger,
        int simulatedStudentCount = 100,
        params Func<IDocumentStore, ILogger, Task>[] additionalSeeds)
    {
        logger.LogInformation("=== Database seeding started ===");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Roles (must be first — users reference roles)
        await RoleSeedData.SeedRolesAsync(store, logger);

        // 2. Demo users (admins, teachers, parents, hand-crafted students)
        await UserSeedData.SeedUsersAsync(store, logger);

        // 3. Simulated students (100 across 8 archetypes)
        await UserSeedData.SeedSimulatedStudentsAsync(store, logger, simulatedStudentCount);

        // 4. Additional seeds (questions, etc.)
        foreach (var seed in additionalSeeds)
        {
            await seed(store, logger);
        }

        sw.Stop();
        logger.LogInformation(
            "=== Database seeding complete in {Duration}ms ===",
            sw.ElapsedMilliseconds);
    }
}
