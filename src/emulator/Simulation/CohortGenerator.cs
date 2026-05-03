// =============================================================================
// Cena Platform -- Cohort Generator
// Bridges StudentProfileGenerator → MasterySimulator.
// Produces a merged view of demographic profiles + simulated mastery histories.
// =============================================================================

using Cena.Actors.Simulation;
using Cena.Emulator.Population;

namespace Cena.Emulator.Simulation;

/// <summary>
/// A student with both a demographic profile and a full simulated mastery history.
/// </summary>
public sealed record CohortMember(
    StudentProfile Profile,
    SimulatedStudent Simulation);

/// <summary>
/// Generates the full simulation cohort by combining:
/// - Demographic profiles (StudentProfileGenerator)
/// - Mastery histories (MasterySimulator)
/// </summary>
public static class CohortGenerator
{
    /// <summary>
    /// Generate a cohort of <paramref name="count"/> students.
    /// Deterministic: same seed = same cohort.
    /// </summary>
    public static IReadOnlyList<CohortMember> Generate(
        int count          = 1000,
        int simulationDays = 60,
        int seed           = 42)
    {
        // Build demographic profiles
        var profiles = StudentProfileGenerator.Generate(count, seed);

        // Build mastery simulation using the archetype mapping
        var archetypeMap = StudentArchetype.All.ToDictionary(a => a.Name);
        var graphCache   = CurriculumSeedData.BuildGraphCache();
        var members      = new List<CohortMember>(count);

        for (int i = 0; i < profiles.Count; i++)
        {
            var profile   = profiles[i];
            var archetype = archetypeMap[profile.Archetype];

            // Unique seed per student, matching the profile seed so IDs align
            var studentSeed = seed + (i + 1) * 1000;

            var sim = MasterySimulator.SimulateStudent(
                profile.StudentId,
                archetype,
                graphCache,
                simulationDays,
                studentSeed);

            members.Add(new CohortMember(profile, sim));
        }

        return members;
    }

    /// <summary>
    /// Print a distribution summary to the console.
    /// </summary>
    public static void LogDistribution(IReadOnlyList<CohortMember> cohort)
    {
        var byArchetype = cohort
            .GroupBy(m => m.Profile.Archetype)
            .OrderByDescending(g => g.Count());

        Log.Information("  Archetype distribution ({Total} students):", cohort.Count);
        foreach (var g in byArchetype)
        {
            var pct = g.Count() * 100.0 / cohort.Count;
            Log.Information("    {Archetype,-20} {Count,4}  ({Pct:F1}%)",
                g.Key, g.Count(), pct);
        }

        var bySchool = cohort
            .GroupBy(m => m.Profile.SchoolId)
            .OrderBy(g => g.Key);

        Log.Information("  School distribution:");
        foreach (var g in bySchool)
            Log.Information("    {School,-20} {Count,4}", g.Key, g.Count());
    }
}
