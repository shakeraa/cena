// =============================================================================
// Cena Platform -- Student Archetypes for Simulation
// 6 statistically diverse learner profiles based on educational research:
// - Distributions calibrated from Israeli K-12 math performance data
// - Each archetype has distinct accuracy, speed, error patterns, and decay rates
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Simulation;

/// <summary>
/// Defines a student archetype with statistical parameters for simulation.
/// </summary>
public sealed record StudentArchetype(
    string Name,
    string Description,
    float AccuracyMean,           // Beta distribution center for P(correct)
    float AccuracyStdDev,         // Variation across concepts
    float ResponseTimeMeanMs,     // Log-normal distribution mean
    float ResponseTimeStdDevMs,   // Log-normal spread
    float InitialEloTheta,        // Starting Elo ability rating
    float DecayResistance,        // Multiplier on HLR half-life (1.0 = normal)
    float[] ErrorTypeWeights,     // [Procedural, Conceptual, Careless, Systematic, Transfer]
    float StudyConsistency,       // 0-1: how regularly they practice (affects decay)
    float BloomCeiling,           // Highest Bloom level they typically achieve
    float StreakProbability,      // P(studying on consecutive day)
    int TypicalSessionLength)     // Attempts per session
{
    /// <summary>
    /// 6 research-based student archetypes covering the full performance spectrum.
    /// Distributions are calibrated for realistic Israeli high-school math.
    /// </summary>
    public static readonly IReadOnlyList<StudentArchetype> All = new[]
    {
        // 1. HIGH ACHIEVER (top 15%): Fast, accurate, few errors, strong retention
        new StudentArchetype(
            "HighAchiever", "Consistently strong student; fast recall, few errors, long retention",
            AccuracyMean: 0.90f, AccuracyStdDev: 0.05f,
            ResponseTimeMeanMs: 8_000f, ResponseTimeStdDevMs: 3_000f,
            InitialEloTheta: 1450f, DecayResistance: 1.5f,
            ErrorTypeWeights: new[] { 0.1f, 0.05f, 0.6f, 0.05f, 0.2f }, // mostly careless slips
            StudyConsistency: 0.85f, BloomCeiling: 5.5f,
            StreakProbability: 0.80f, TypicalSessionLength: 25),

        // 2. STEADY LEARNER (middle 40%): Average pace, moderate accuracy, balanced errors
        new StudentArchetype(
            "SteadyLearner", "Reliable mid-range student; consistent effort, moderate retention",
            AccuracyMean: 0.70f, AccuracyStdDev: 0.10f,
            ResponseTimeMeanMs: 15_000f, ResponseTimeStdDevMs: 5_000f,
            InitialEloTheta: 1200f, DecayResistance: 1.0f,
            ErrorTypeWeights: new[] { 0.35f, 0.30f, 0.15f, 0.10f, 0.10f },
            StudyConsistency: 0.60f, BloomCeiling: 4.0f,
            StreakProbability: 0.55f, TypicalSessionLength: 18),

        // 3. STRUGGLING STUDENT (bottom 20%): Slow, low accuracy, many conceptual errors
        new StudentArchetype(
            "Struggling", "Significant gaps; slow processing, frequent conceptual errors",
            AccuracyMean: 0.40f, AccuracyStdDev: 0.12f,
            ResponseTimeMeanMs: 25_000f, ResponseTimeStdDevMs: 10_000f,
            InitialEloTheta: 950f, DecayResistance: 0.6f,
            ErrorTypeWeights: new[] { 0.25f, 0.45f, 0.05f, 0.20f, 0.05f }, // mostly conceptual
            StudyConsistency: 0.30f, BloomCeiling: 2.5f,
            StreakProbability: 0.25f, TypicalSessionLength: 10),

        // 4. FAST BUT CARELESS (15%): High speed, moderate accuracy, careless error dominant
        new StudentArchetype(
            "FastCareless", "Quick intuition but rushes; strong on concepts, weak on execution",
            AccuracyMean: 0.65f, AccuracyStdDev: 0.15f,
            ResponseTimeMeanMs: 6_000f, ResponseTimeStdDevMs: 2_500f,
            InitialEloTheta: 1300f, DecayResistance: 1.1f,
            ErrorTypeWeights: new[] { 0.15f, 0.10f, 0.55f, 0.05f, 0.15f }, // mostly careless
            StudyConsistency: 0.50f, BloomCeiling: 4.5f,
            StreakProbability: 0.45f, TypicalSessionLength: 22),

        // 5. SLOW BUT THOROUGH (10%): High accuracy when given time, very slow response
        new StudentArchetype(
            "SlowThorough", "Deep thinker; high accuracy but needs time; effortful mastery",
            AccuracyMean: 0.80f, AccuracyStdDev: 0.08f,
            ResponseTimeMeanMs: 30_000f, ResponseTimeStdDevMs: 12_000f,
            InitialEloTheta: 1250f, DecayResistance: 1.3f,
            ErrorTypeWeights: new[] { 0.30f, 0.10f, 0.05f, 0.15f, 0.40f }, // mostly transfer
            StudyConsistency: 0.70f, BloomCeiling: 5.0f,
            StreakProbability: 0.65f, TypicalSessionLength: 12),

        // 6. INCONSISTENT (sporadic attendance, variable performance)
        new StudentArchetype(
            "Inconsistent", "Capable but unreliable; high variance, gaps from irregular study",
            AccuracyMean: 0.60f, AccuracyStdDev: 0.20f,
            ResponseTimeMeanMs: 14_000f, ResponseTimeStdDevMs: 8_000f,
            InitialEloTheta: 1150f, DecayResistance: 0.7f,
            ErrorTypeWeights: new[] { 0.25f, 0.25f, 0.20f, 0.15f, 0.15f },
            StudyConsistency: 0.25f, BloomCeiling: 3.5f,
            StreakProbability: 0.20f, TypicalSessionLength: 15),
    };
}
