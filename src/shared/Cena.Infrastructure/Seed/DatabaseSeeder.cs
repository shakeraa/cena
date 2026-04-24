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
///   3. Simulated students (300 students across 8 archetypes, 60-day history)
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
    /// <param name="simulatedStudentCount">Number of simulated students (default 300).</param>
    /// <param name="additionalSeeds">
    /// Optional extra seed functions (e.g., QuestionBankSeedData.SeedQuestionsAsync)
    /// that depend on types not available in Cena.Infrastructure. Legacy
    /// signature that only needs store + logger.
    /// </param>
    public static Task SeedAllAsync(
        IDocumentStore store,
        ILogger logger,
        int simulatedStudentCount = 300,
        params Func<IDocumentStore, ILogger, Task>[] additionalSeeds)
        => SeedAllAsync(
            store,
            logger,
            services: null,
            simulatedStudentCount: simulatedStudentCount,
            additionalSeeds: additionalSeeds.Select<Func<IDocumentStore, ILogger, Task>, Func<SeedContext, Task>>(
                legacy => ctx => legacy(ctx.Store, ctx.Logger)).ToArray());

    /// <summary>
    /// RDY-037: overload that accepts an <see cref="IServiceProvider"/> so
    /// optional seed delegates that need extra services (e.g.
    /// <c>ICasGatedQuestionPersister</c> for the question bank seeder) can
    /// resolve them via <see cref="SeedContext.Services"/>.
    /// </summary>
    public static async Task SeedAllAsync(
        IDocumentStore store,
        ILogger logger,
        IServiceProvider? services,
        int simulatedStudentCount = 300,
        params Func<SeedContext, Task>[] additionalSeeds)
    {
        logger.LogInformation("=== Database seeding started ===");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Roles (must be first — users reference roles)
        await RoleSeedData.SeedRolesAsync(store, logger);

        // 2. Demo users (admins, teachers, parents, hand-crafted students)
        await UserSeedData.SeedUsersAsync(store, logger);

        // 2b. Platform institute + Bagrut curriculum tracks + classrooms (TENANCY-P1d)
        //     Must run before classroom seed so demo classrooms can reference tracks.
        await PlatformSeedData.SeedPlatformAsync(store, logger);

        // 3. Sample classrooms for testing join codes (STB-00b)
        await ClassroomSeedData.SeedClassroomsAsync(store, logger);

        // 4. Social data (feed items, peer solutions, friendships, study rooms)
        await SocialSeedData.SeedSocialDataAsync(store, logger);

        // 4b. Challenge catalog (daily, card chains, tournaments)
        await ChallengeSeedData.SeedAsync(store, logger);

        // 4b1. Learning objectives (FIND-pedagogy-008) — must run BEFORE the
        //      question seeders so each question can reference a stable LO id.
        await LearningObjectiveSeedData.SeedLearningObjectivesAsync(store, logger);

        // 4c. Session questions (HARDEN SessionEndpoints)
        await SessionQuestionSeedData.SeedSessionQuestionsAsync(store, logger);

        // 5. Simulated students (300 across 8 archetypes)
        await UserSeedData.SeedSimulatedStudentsAsync(store, logger, simulatedStudentCount);

        // 6. Admin analytics rollup seeds (ADM-014/015/016/018)
        //    Idempotent rollups that power admin dashboards without
        //    requiring the simulation pipeline to run first.
        await AdminAnalyticsSeedData.SeedAllAsync(store, logger);

        // 7. Additional seeds (simulation events, questions, etc.)
        //    RDY-037: seeds receive SeedContext so they can resolve services
        //    (e.g. the CAS-gated question persister) via Services when needed.
        var ctx = new SeedContext(
            store,
            services ?? EmptyServiceProvider.Instance,
            logger);
        foreach (var seed in additionalSeeds)
        {
            await seed(ctx);
        }

        sw.Stop();
        logger.LogInformation(
            "=== Database seeding complete in {Duration}ms ===",
            sw.ElapsedMilliseconds);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
