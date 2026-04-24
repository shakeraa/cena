// =============================================================================
// Cena Platform -- Student Profile Generator (EMU-001.1)
// Generates 1,000 unique student profiles with realistic demographics.
//
// Distribution:
//   Genius (3%), HighAchiever (12%), SteadyLearner (35%), Struggling (20%),
//   FastCareless (10%), SlowThorough (8%), Inconsistent (10%), VeryLowCognitive (2%)
//
// Tenant model: 5 simulated schools (school-alpha … school-epsilon)
// Language model: Hebrew (70%), Arabic (30%)
// Depth unit: 3-unit (20%), 4-unit (35%), 5-unit (45%) — weighted by archetype
// =============================================================================

namespace Cena.Emulator.Population;

/// <summary>
/// Immutable profile for a single simulated student.
/// Generated once at startup; not persisted to any database.
/// </summary>
public sealed record StudentProfile(
    string StudentId,
    string Archetype,
    string SchoolId,
    string Language,
    int DepthUnit,
    DateOnly BagrutExamDate,
    StudyHabitProfile HabitProfile,
    float VarianceFactor   // ±30% variance multiplier for this specific student
);

/// <summary>
/// Generates a deterministic population of student profiles.
/// Same seed always produces the same population (reproducible).
/// </summary>
public static class StudentProfileGenerator
{
    // ── Distribution tables ──────────────────────────────────────────────────

    // Archetype name → fraction of total population (must sum to 1.0)
    private static readonly (string Name, double Fraction)[] ArchetypeDistribution =
    {
        ("Genius",           0.03),
        ("HighAchiever",     0.12),
        ("SteadyLearner",    0.35),
        ("Struggling",       0.20),
        ("FastCareless",     0.10),
        ("SlowThorough",     0.08),
        ("Inconsistent",     0.10),
        ("VeryLowCognitive", 0.02),
    };

    private static readonly string[] Schools =
    {
        "school-alpha", "school-beta", "school-gamma", "school-delta", "school-epsilon"
    };

    // Depth unit weights per archetype: [3-unit, 4-unit, 5-unit]
    private static readonly Dictionary<string, double[]> DepthUnitWeights = new()
    {
        ["Genius"]           = new[] { 0.05, 0.15, 0.80 }, // Genius mostly 5-unit
        ["HighAchiever"]     = new[] { 0.10, 0.30, 0.60 },
        ["SteadyLearner"]    = new[] { 0.20, 0.40, 0.40 },
        ["Struggling"]       = new[] { 0.45, 0.35, 0.20 }, // Struggling skews 3-unit
        ["FastCareless"]     = new[] { 0.15, 0.35, 0.50 },
        ["SlowThorough"]     = new[] { 0.20, 0.40, 0.40 },
        ["Inconsistent"]     = new[] { 0.25, 0.40, 0.35 },
        ["VeryLowCognitive"] = new[] { 0.65, 0.25, 0.10 }, // VLC mostly 3-unit
    };

    // All students in the same cohort target the same Bagrut exam
    private static readonly DateOnly BagrutExamDate = new DateOnly(2026, 5, 12);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generate <paramref name="count"/> unique student profiles.
    /// Pass the same <paramref name="seed"/> for reproducible results.
    /// </summary>
    public static List<StudentProfile> Generate(int count = 1000, int seed = 42)
    {
        var rng = new Random(seed);
        var profiles = new List<StudentProfile>(count);

        // Build the slot list: archetype names in the right proportions
        var slots = BuildSlots(count);

        // Shuffle for realistic ordering
        for (int i = slots.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        // Counters per archetype for ID generation
        var seqByArchetype = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            var archetype = slots[i];

            seqByArchetype.TryGetValue(archetype, out var seq);
            seqByArchetype[archetype] = seq + 1;

            var shortName = ArchetypeShortName(archetype);
            var studentId = $"emu-{shortName}-{seq + 1:D3}";

            // Deterministic school assignment (hash-based for reproducibility)
            var schoolIdx = Math.Abs(studentId.GetHashCode()) % Schools.Length;
            var schoolId  = Schools[schoolIdx];

            // Language: 70% Hebrew, 30% Arabic
            var language = rng.NextDouble() < 0.70 ? "he" : "ar";

            // Depth unit: weighted by archetype
            var depthUnit = SampleDepthUnit(archetype, rng);

            // Per-student variance factor in [0.7, 1.3]
            var varianceFactor = (float)(0.7 + rng.NextDouble() * 0.6);

            var habitProfile = StudyHabitProfile.All[archetype];

            profiles.Add(new StudentProfile(
                StudentId: studentId,
                Archetype: archetype,
                SchoolId: schoolId,
                Language: language,
                DepthUnit: depthUnit,
                BagrutExamDate: BagrutExamDate,
                HabitProfile: habitProfile,
                VarianceFactor: varianceFactor));
        }

        return profiles;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static List<string> BuildSlots(int total)
    {
        var slots  = new List<string>(total);
        var allocated = 0;

        for (int i = 0; i < ArchetypeDistribution.Length; i++)
        {
            var (name, fraction) = ArchetypeDistribution[i];
            // Last archetype absorbs rounding remainder
            int n = (i == ArchetypeDistribution.Length - 1)
                ? total - allocated
                : (int)Math.Round(total * fraction);

            for (int j = 0; j < n; j++)
                slots.Add(name);

            allocated += n;
        }

        return slots;
    }

    private static int SampleDepthUnit(string archetype, Random rng)
    {
        var weights  = DepthUnitWeights[archetype];
        var roll     = rng.NextDouble();
        var cumul    = 0.0;
        var units    = new[] { 3, 4, 5 };

        for (int i = 0; i < weights.Length; i++)
        {
            cumul += weights[i];
            if (roll < cumul)
                return units[i];
        }

        return 5;
    }

    private static string ArchetypeShortName(string archetype) => archetype switch
    {
        "Genius"           => "genius",
        "HighAchiever"     => "hiach",
        "SteadyLearner"    => "steady",
        "Struggling"       => "strugg",
        "FastCareless"     => "fast",
        "SlowThorough"     => "slow",
        "Inconsistent"     => "incon",
        "VeryLowCognitive" => "vlc",
        _                  => archetype.ToLower()
    };
}
