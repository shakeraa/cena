// =============================================================================
// Cena Platform — Enrollment Backfill Service (TENANCY-P1e)
//
// WHY THIS EXISTS:
// Before multi-institute tenancy (Phase 1), students had no enrollment record.
// Their event streams contain OnboardingCompleted_V1, ConceptAttempted_V1, etc.
// but no EnrollmentCreated_V1. This service runs once on startup and appends a
// synthetic EnrollmentCreated_V1 to any student stream that lacks one, binding
// the student to cena-platform + BAGRUT-GENERAL (a placeholder track).
//
// WHY BAGRUT-GENERAL (not a specific track):
// We don't know which track existing students belong to. Assigning them to e.g.
// MATH-BAGRUT-806 would be a false assumption. BAGRUT-GENERAL is a Draft-status
// placeholder. When the student starts their next session, the topic selection
// screen (architecture doc section 24, Improvement #26) determines the real
// track and a follow-up migration moves them from BAGRUT-GENERAL to it.
//
// IDEMPOTENCY: If the student stream already has an EnrollmentCreated_V1, this
// service skips it. Two runs = same result.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.Seed;
using Marten;
using Marten.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Configuration;

/// <summary>
/// Hosted service that backfills EnrollmentCreated_V1 for legacy student streams.
/// Runs once on application startup, then stops.
/// </summary>
public sealed class EnrollmentBackfillService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EnrollmentBackfillService> _logger;

    public EnrollmentBackfillService(
        IServiceProvider serviceProvider,
        ILogger<EnrollmentBackfillService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TENANCY-P1e: Starting enrollment backfill for legacy students");

        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        await using var querySession = store.QuerySession();
        await using var writeSession = store.LightweightSession();

        // Find all student profile snapshots (these represent students with event streams)
        var profiles = await querySession.Query<StudentProfileSnapshot>()
            .Where(p => p.OnboardedAt != null) // Only students who completed onboarding
            .ToListAsync(cancellationToken);

        int backfilled = 0;
        int skipped = 0;

        foreach (var profile in profiles)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                // Check if this stream already has an EnrollmentCreated_V1
                var events = await querySession.Events.FetchStreamAsync(profile.StudentId, token: cancellationToken);
                var hasEnrollment = events.Any(e => e.Data is EnrollmentCreated_V1);

                if (hasEnrollment)
                {
                    skipped++;
                    continue;
                }

                // Append synthetic enrollment event to the student's existing stream
                var enrollmentId = $"enroll-{profile.StudentId}-bagrut-general";
                var syntheticEvent = new EnrollmentCreated_V1(
                    StudentId: profile.StudentId,
                    EnrollmentId: enrollmentId,
                    InstituteId: PlatformSeedData.PlatformInstituteId,
                    TrackId: "track-bagrut-general",
                    ClassroomId: "classroom-bagrut-general",
                    EnrolledAt: profile.CreatedAt != default ? profile.CreatedAt : DateTimeOffset.UtcNow
                );

                writeSession.Events.Append(profile.StudentId, syntheticEvent);
                backfilled++;

                if (backfilled % 100 == 0)
                {
                    await writeSession.SaveChangesAsync(cancellationToken);
                    _logger.LogDebug("TENANCY-P1e: Backfilled {Count} students so far...", backfilled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "TENANCY-P1e: Failed to backfill enrollment for {StudentId}, skipping",
                    profile.StudentId);
            }
        }

        // Final save for any remaining
        if (backfilled > 0)
        {
            await writeSession.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "TENANCY-P1e: Enrollment backfill complete. Backfilled={Backfilled}, Skipped={Skipped}, Total={Total}",
            backfilled, skipped, profiles.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Extension method to register the enrollment backfill service.
/// </summary>
public static class EnrollmentBackfillExtensions
{
    public static IServiceCollection AddEnrollmentBackfill(this IServiceCollection services)
    {
        services.AddHostedService<EnrollmentBackfillService>();
        return services;
    }
}
