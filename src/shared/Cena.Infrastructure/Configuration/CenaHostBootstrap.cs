// =============================================================================
// Cena Platform — Host Bootstrap Utilities (FIND-sec-007)
// Shared startup initialization for all API hosts.
// Ensures fail-fast behavior and proper seeding on application start.
// =============================================================================

using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Seed;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Configuration;

/// <summary>
/// Shared bootstrap utilities for Cena API hosts.
/// Provides standardized startup initialization with fail-fast behavior.
/// </summary>
public static class CenaHostBootstrap
{
    /// <summary>
    /// Initializes the host by seeding the database and validating critical services.
    /// Call from ApplicationStarted lifetime event.
    /// 
    /// Admin hosts should also call <see cref="InitializeFirebaseAsync"/> separately
    /// to sync admin claims (student hosts do not need this).
    /// </summary>
    /// <param name="store">Marten document store for database operations.</param>
    /// <param name="logger">Logger for bootstrap operations.</param>
    /// <param name="simulatedStudentCount">Number of simulated students to seed (default 300).</param>
    /// <param name="additionalSeeds">Optional additional seed functions.</param>
    public static async Task InitializeAsync(
        IDocumentStore store,
        ILogger logger,
        int simulatedStudentCount = 300,
        params Func<IDocumentStore, ILogger, Task>[] additionalSeeds)
    {
        logger.LogInformation("=== Cena Host Bootstrap starting ===");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Seed database with all core data
            await DatabaseSeeder.SeedAllAsync(store, logger, simulatedStudentCount, additionalSeeds);
            
            sw.Stop();
            logger.LogInformation(
                "=== Cena Host Bootstrap complete in {Duration}ms ===",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "=== Cena Host Bootstrap FAILED after {Duration}ms ===", sw.ElapsedMilliseconds);
            // Re-throw to fail-fast — host should not start if seeding fails
            throw;
        }
    }

    /// <summary>
    /// Initializes Firebase Admin SDK and syncs admin user claims.
    /// Only call this from admin hosts — student hosts should not manage admin claims.
    /// </summary>
    /// <param name="firebaseService">Firebase admin service (resolving forces SDK init).</param>
    /// <param name="logger">Logger for Firebase operations.</param>
    public static async Task InitializeFirebaseAsync(IFirebaseAdminService firebaseService, ILogger logger)
    {
        logger.LogInformation("=== Firebase Admin initialization starting ===");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Resolving the service forces Firebase Admin SDK initialization
            // This will throw if Firebase credentials are misconfigured
            _ = firebaseService;
            logger.LogInformation("Firebase Admin SDK initialized successfully");

            // Sync admin user claims
            await FirebaseClaimsSeeder.SyncAdminClaimsAsync(logger);
            
            sw.Stop();
            logger.LogInformation(
                "=== Firebase Admin initialization complete in {Duration}ms ===",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "=== Firebase Admin initialization FAILED after {Duration}ms ===", sw.ElapsedMilliseconds);
            // Re-throw to fail-fast — admin hosts require Firebase
            throw;
        }
    }
}
