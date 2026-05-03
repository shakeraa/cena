// =============================================================================
// Cena Platform — Platform Seed Data (TENANCY-P1d)
// Seeds the platform-level institute, Bagrut curriculum tracks, and one
// self-paced classroom per track. Idempotent — safe to re-run on startup.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Seeds the multi-institute tenancy foundation:
///   1. Platform institute (cena-platform)
///   2. Three Bagrut 5-unit tracks (math 806, math 807, physics 036)
///   3. One BAGRUT-GENERAL placeholder track (Draft status)
///   4. One self-paced classroom per track
/// </summary>
public static class PlatformSeedData
{
    public const string PlatformInstituteId = "cena-platform";

    private static readonly CurriculumTrackDocument[] Tracks = new[]
    {
        new CurriculumTrackDocument
        {
            Id = "track-math-bagrut-806",
            TrackId = "track-math-bagrut-806",
            Code = "MATH-BAGRUT-806",
            Title = "Mathematics — Calculus & Analytic Geometry (5 units)",
            Subject = "math",
            TargetExam = "Bagrut Mathematics 806",
            Status = CurriculumTrackStatus.Seeding,
            LearningObjectiveIds = Array.Empty<string>(),
            StandardMappings = new[] { "IL-BAGRUT-MATH-806" }
        },
        new CurriculumTrackDocument
        {
            Id = "track-math-bagrut-807",
            TrackId = "track-math-bagrut-807",
            Code = "MATH-BAGRUT-807",
            Title = "Mathematics — Calculus & Probability (5 units)",
            Subject = "math",
            TargetExam = "Bagrut Mathematics 807",
            Status = CurriculumTrackStatus.Seeding,
            LearningObjectiveIds = Array.Empty<string>(),
            StandardMappings = new[] { "IL-BAGRUT-MATH-807" }
        },
        new CurriculumTrackDocument
        {
            Id = "track-physics-bagrut-036",
            TrackId = "track-physics-bagrut-036",
            Code = "PHYSICS-BAGRUT-036",
            Title = "Physics (5 units)",
            Subject = "physics",
            TargetExam = "Bagrut Physics 036",
            Status = CurriculumTrackStatus.Seeding,
            LearningObjectiveIds = Array.Empty<string>(),
            StandardMappings = new[] { "IL-BAGRUT-PHYS-036" }
        },
        new CurriculumTrackDocument
        {
            Id = "track-bagrut-general",
            TrackId = "track-bagrut-general",
            Code = "BAGRUT-GENERAL",
            Title = "General Bagrut (placeholder for unassigned students)",
            Subject = "general",
            TargetExam = null,
            Status = CurriculumTrackStatus.Draft,
            LearningObjectiveIds = Array.Empty<string>(),
            StandardMappings = Array.Empty<string>()
        }
    };

    /// <summary>
    /// Seeds platform institute, 4 curriculum tracks, and 4 self-paced classrooms.
    /// All operations are idempotent upserts.
    /// </summary>
    public static async Task SeedPlatformAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();

        // 1. Platform institute
        var existingInstitute = await session.LoadAsync<InstituteDocument>(PlatformInstituteId);
        if (existingInstitute is null)
        {
            session.Store(new InstituteDocument
            {
                Id = PlatformInstituteId,
                InstituteId = PlatformInstituteId,
                Name = "Cena Platform",
                Type = InstituteType.Platform,
                Country = "IL",
                MentorId = "",
                CreatedAt = DateTimeOffset.UtcNow
            });
            logger.LogInformation("Seeded platform institute: {Id}", PlatformInstituteId);
        }
        else
        {
            logger.LogDebug("Platform institute already exists: {Id}", PlatformInstituteId);
        }

        // 2. Curriculum tracks
        foreach (var track in Tracks)
        {
            var existing = await session.LoadAsync<CurriculumTrackDocument>(track.Id);
            if (existing is null)
            {
                session.Store(track);
                logger.LogInformation("Seeded curriculum track: {Code} ({Id})", track.Code, track.Id);
            }
            else
            {
                logger.LogDebug("Curriculum track already exists: {Code}", track.Code);
            }
        }

        // 3. One self-paced classroom per track
        foreach (var track in Tracks)
        {
            var classroomId = $"classroom-{track.Code.ToLowerInvariant()}";
            var existing = await session.LoadAsync<ClassroomDocument>(classroomId);
            if (existing is null)
            {
                session.Store(new ClassroomDocument
                {
                    Id = classroomId,
                    ClassroomId = classroomId,
                    JoinCode = GenerateJoinCode(track.Code),
                    Name = $"{track.Title} — Self-Paced",
                    TeacherId = "",
                    TeacherName = "Platform",
                    Subjects = new[] { track.Subject },
                    Grade = "11-12",
                    SchoolId = null,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    InstituteId = PlatformInstituteId,
                    ProgramId = track.Id,
                    Mode = ClassroomMode.SelfPaced,
                    MentorIds = Array.Empty<string>(),
                    JoinApproval = track.Status == CurriculumTrackStatus.Draft
                        ? JoinApprovalMode.InviteOnly
                        : JoinApprovalMode.AutoApprove,
                    Status = ClassroomStatus.Active,
                    StartDate = null,
                    EndDate = null
                });
                logger.LogInformation("Seeded classroom: {Id} for track {Code}", classroomId, track.Code);
            }
            else
            {
                logger.LogDebug("Classroom already exists: {Id}", classroomId);
            }
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Platform seed complete: 1 institute, {TrackCount} tracks, {TrackCount} classrooms",
            Tracks.Length, Tracks.Length);
    }

    /// <summary>
    /// Generates a deterministic 6-char join code from the track code.
    /// Deterministic so re-seeding produces the same codes.
    /// </summary>
    private static string GenerateJoinCode(string trackCode)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"cena-join-{trackCode}"));
        return Convert.ToHexString(hash)[..6].ToUpperInvariant();
    }
}
