// =============================================================================
// Cena Platform -- Study Habit Profiles
// Per-archetype study patterns: session duration, frequency, peak hours, weekend factor.
// Each archetype has characteristic timing and engagement patterns based on
// Israeli high-school student research data.
// =============================================================================

namespace Cena.Emulator.Population;

/// <summary>
/// Describes the study habit pattern for a particular student archetype.
/// Values are means; per-student variance is applied by StudentProfileGenerator.
/// </summary>
public sealed record StudyHabitProfile(
    string ArchetypeName,
    int MinSessionMinutes,
    int MaxSessionMinutes,
    int MinSessionsPerDay,
    int MaxSessionsPerDay,
    int[] PeakHours,           // Preferred start hours (24h)
    float WeekendMultiplier,   // Multiplier on session frequency for weekends
    float FocusDegradationRate // How quickly focus drops per minute (0 = never, 1 = very fast)
)
{
    /// <summary>
    /// Per-archetype study habit lookup keyed by archetype name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, StudyHabitProfile> All =
        new Dictionary<string, StudyHabitProfile>
        {
            ["Genius"] = new StudyHabitProfile(
                ArchetypeName: "Genius",
                MinSessionMinutes: 20, MaxSessionMinutes: 30,
                MinSessionsPerDay: 1,  MaxSessionsPerDay: 2,
                PeakHours: new[] { 21, 22, 23 },
                WeekendMultiplier: 0.5f,
                FocusDegradationRate: 0.002f),   // very slow degradation

            ["HighAchiever"] = new StudyHabitProfile(
                ArchetypeName: "HighAchiever",
                MinSessionMinutes: 30, MaxSessionMinutes: 45,
                MinSessionsPerDay: 2,  MaxSessionsPerDay: 3,
                PeakHours: new[] { 16, 17, 18, 19, 20 },
                WeekendMultiplier: 1.2f,
                FocusDegradationRate: 0.003f),

            ["SteadyLearner"] = new StudyHabitProfile(
                ArchetypeName: "SteadyLearner",
                MinSessionMinutes: 25, MaxSessionMinutes: 35,
                MinSessionsPerDay: 1,  MaxSessionsPerDay: 2,
                PeakHours: new[] { 17, 18, 19, 20, 21 },
                WeekendMultiplier: 0.8f,
                FocusDegradationRate: 0.004f),

            ["Struggling"] = new StudyHabitProfile(
                ArchetypeName: "Struggling",
                MinSessionMinutes: 15, MaxSessionMinutes: 25,
                MinSessionsPerDay: 1,  MaxSessionsPerDay: 1,
                PeakHours: new[] { 18, 19, 20 },
                WeekendMultiplier: 0.3f,
                FocusDegradationRate: 0.009f),   // fast degradation

            ["FastCareless"] = new StudyHabitProfile(
                ArchetypeName: "FastCareless",
                MinSessionMinutes: 10, MaxSessionMinutes: 15,
                MinSessionsPerDay: 2,  MaxSessionsPerDay: 3,
                PeakHours: new[] { 9, 13, 16, 19, 21 }, // scattered
                WeekendMultiplier: 0.5f,
                FocusDegradationRate: 0.006f),

            ["SlowThorough"] = new StudyHabitProfile(
                ArchetypeName: "SlowThorough",
                MinSessionMinutes: 40, MaxSessionMinutes: 60,
                MinSessionsPerDay: 1,  MaxSessionsPerDay: 1,
                PeakHours: new[] { 15, 16, 17, 18, 19 },
                WeekendMultiplier: 1.5f,
                FocusDegradationRate: 0.002f),

            ["Inconsistent"] = new StudyHabitProfile(
                ArchetypeName: "Inconsistent",
                MinSessionMinutes: 5,  MaxSessionMinutes: 45,  // high variance
                MinSessionsPerDay: 0,  MaxSessionsPerDay: 3,
                PeakHours: new[] { 10, 14, 18, 20, 22 },      // random
                WeekendMultiplier: 0.2f,                        // very inconsistent weekends
                FocusDegradationRate: 0.007f),

            ["VeryLowCognitive"] = new StudyHabitProfile(
                ArchetypeName: "VeryLowCognitive",
                MinSessionMinutes: 10, MaxSessionMinutes: 20,
                MinSessionsPerDay: 0,  MaxSessionsPerDay: 1,
                PeakHours: new[] { 18, 19 },
                WeekendMultiplier: 0.1f,
                FocusDegradationRate: 0.012f),  // fastest degradation
        };

    /// <summary>
    /// Sample a concrete session duration in minutes for a student,
    /// applying ±30% per-student variance around the archetype mean.
    /// </summary>
    public int SampleSessionMinutes(Random rng, float studentVarianceFactor)
    {
        var mean = (MinSessionMinutes + MaxSessionMinutes) / 2.0;
        var raw  = mean + (rng.NextDouble() - 0.5) * 2.0 * mean * 0.30 * studentVarianceFactor;
        return (int)Math.Clamp(raw, MinSessionMinutes * 0.5, MaxSessionMinutes * 1.5);
    }

    /// <summary>
    /// Sample a start hour from the peak hours pool (with small random offset).
    /// </summary>
    public int SampleStartHour(Random rng)
        => PeakHours[rng.Next(PeakHours.Length)];
}
