// =============================================================================
// Cena Platform -- Classroom Seed Data (STB-00b)
// Dev-only sample classrooms for testing join codes
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Seed data for sample classrooms with join codes.
/// Safe to re-run (upsert by ClassroomId).
/// </summary>
public static class ClassroomSeedData
{
    /// <summary>
    /// Seed sample classrooms for testing classroom join functionality.
    /// </summary>
    public static async Task SeedClassroomsAsync(IDocumentStore store, ILogger logger)
    {
        logger.LogInformation("Seeding sample classrooms...");

        await using var session = store.LightweightSession();

        var classrooms = new[]
        {
            new ClassroomDocument
            {
                Id = "classroom_math_101",
                ClassroomId = "classroom_math_101",
                JoinCode = "MATH2024",
                Name = "Advanced Mathematics - Grade 10",
                TeacherId = "teacher_001",
                TeacherName = "Ms. Johnson",
                Subjects = new[] { "Mathematics", "Algebra", "Geometry" },
                Grade = "10",
                SchoolId = "school_001",
                IsActive = true
            },
            new ClassroomDocument
            {
                Id = "classroom_physics_101",
                ClassroomId = "classroom_physics_101",
                JoinCode = "PHYS2024",
                Name = "Physics Fundamentals",
                TeacherId = "teacher_002",
                TeacherName = "Mr. Chen",
                Subjects = new[] { "Physics", "Science" },
                Grade = "11",
                SchoolId = "school_001",
                IsActive = true
            },
            new ClassroomDocument
            {
                Id = "classroom_cs_101",
                ClassroomId = "classroom_cs_101",
                JoinCode = "CODE2024",
                Name = "Introduction to Computer Science",
                TeacherId = "teacher_003",
                TeacherName = "Dr. Smith",
                Subjects = new[] { "Computer Science", "Programming" },
                Grade = "9",
                SchoolId = "school_002",
                IsActive = true
            }
        };

        foreach (var classroom in classrooms)
        {
            session.Store(classroom);
            logger.LogDebug("Seeded classroom: {Name} (code: {Code})",
                classroom.Name, classroom.JoinCode);
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} sample classrooms", classrooms.Length);
    }
}
