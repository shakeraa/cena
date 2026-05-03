// =============================================================================
// Cena Platform -- Mastery Simulation Engine
// Generates statistically diverse student learning histories using:
// - Beta distributions for accuracy (concept-specific, student-specific)
// - Log-normal distributions for response time (realistic right-skew)
// - Weighted categorical distribution for error types
// - BKT + HLR pipeline for realistic mastery progression
// - Temporal patterns (study gaps, streaks, decay)
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Simulation;

/// <summary>
/// Result of simulating one student's learning history.
/// </summary>
public sealed record SimulatedStudent(
    string StudentId,
    string ArchetypeName,
    IReadOnlyDictionary<string, ConceptMasteryState> MasteryOverlay,
    IReadOnlyList<SimulatedAttempt> AttemptHistory,
    ResponseTimeBaseline ResponseBaseline,
    float EloTheta,
    int TotalSessions,
    int StudyStreakDays);

/// <summary>
/// A single simulated concept attempt.
/// </summary>
public sealed record SimulatedAttempt(
    string StudentId,
    string ConceptId,
    string SessionId,
    bool IsCorrect,
    int ResponseTimeMs,
    ErrorType? ClassifiedError,
    float PriorMastery,
    float PosteriorMastery,
    float EffectiveMastery,
    MasteryQuality QualityQuadrant,
    DateTimeOffset Timestamp);

/// <summary>
/// Generates statistically diverse simulation data using the full mastery pipeline.
/// All randomness is seeded for reproducibility.
/// </summary>
public static class MasterySimulator
{
    /// <summary>
    /// Generate a cohort of simulated students with diverse learning patterns.
    /// Default: 5 per archetype, each with 60 days of history.
    /// </summary>
    public static IReadOnlyList<SimulatedStudent> GenerateCohort(
        int studentsPerArchetype = 5,
        int simulationDays = 60,
        int seed = 42)
    {
        var graphCache = CurriculumSeedData.BuildGraphCache();
        var results = new List<SimulatedStudent>();
        int studentIndex = 0;

        foreach (var archetype in StudentArchetype.All)
        {
            for (int i = 0; i < studentsPerArchetype; i++)
            {
                studentIndex++;
                var studentSeed = seed + studentIndex * 1000;
                var student = SimulateStudent(
                    $"sim-{archetype.Name.ToLower()}-{i + 1:D2}",
                    archetype, graphCache, simulationDays, studentSeed);
                results.Add(student);
            }
        }

        return results;
    }

    /// <summary>
    /// Generate a realistically distributed cohort: 5% Genius, 10% VeryLowCognitive,
    /// 85% distributed across middle archetypes. 2 months (60 days) of history.
    ///
    /// Distribution (per 100 students):
    ///   Genius:           5  (5%)
    ///   HighAchiever:    10  (10%)
    ///   SteadyLearner:   30  (30%)
    ///   Struggling:      15  (15%)
    ///   FastCareless:    10  (10%)
    ///   SlowThorough:    10  (10%)
    ///   Inconsistent:    10  (10%)
    ///   VeryLowCognitive:10  (10%)
    /// </summary>
    public static IReadOnlyList<SimulatedStudent> GenerateRealisticCohort(
        int totalStudents = 100,
        int simulationDays = 60,
        int seed = 42)
    {
        var graphCache = CurriculumSeedData.BuildGraphCache();
        var results = new List<SimulatedStudent>();

        // Distribution table: archetype name → percentage of total
        var distribution = new (string ArchetypeName, double Percentage)[]
        {
            ("Genius",           0.05),
            ("HighAchiever",     0.10),
            ("SteadyLearner",    0.30),
            ("Struggling",       0.15),
            ("FastCareless",     0.10),
            ("SlowThorough",     0.10),
            ("Inconsistent",     0.10),
            ("VeryLowCognitive", 0.10),
        };

        var archetypeMap = StudentArchetype.All.ToDictionary(a => a.Name);
        int studentIndex = 0;

        foreach (var (archetypeName, percentage) in distribution)
        {
            int count = (int)Math.Round(totalStudents * percentage);
            var archetype = archetypeMap[archetypeName];

            for (int i = 0; i < count; i++)
            {
                studentIndex++;
                var studentSeed = seed + studentIndex * 1000;
                var student = SimulateStudent(
                    $"sim-{archetype.Name.ToLower()}-{studentIndex:D3}",
                    archetype, graphCache, simulationDays, studentSeed);
                results.Add(student);
            }
        }

        return results;
    }

    /// <summary>
    /// Simulate a single student's learning journey over N days.
    /// </summary>
    public static SimulatedStudent SimulateStudent(
        string studentId,
        StudentArchetype archetype,
        InMemoryGraphCache graphCache,
        int days,
        int seed)
    {
        var rng = new Random(seed);
        var overlay = new Dictionary<string, ConceptMasteryState>();
        var attempts = new List<SimulatedAttempt>();
        var baseline = ResponseTimeBaseline.Initial;
        float eloTheta = archetype.InitialEloTheta;
        int totalSessions = 0;
        int streakDays = 0;
        int consecutiveStudyDays = 0;

        var conceptIds = graphCache.Concepts.Keys.OrderBy(c => graphCache.GetDepth(c)).ToList();
        var startDate = DateTimeOffset.Parse("2025-09-01T08:00:00+03:00"); // Israeli school year

        for (int day = 0; day < days; day++)
        {
            var dayStart = startDate.AddDays(day);

            // Skip Saturdays (Shabbat) and Fridays after noon
            if (dayStart.DayOfWeek == DayOfWeek.Saturday)
                continue;

            // Decide if student studies today (based on consistency + streak)
            bool studiestoday = rng.NextDouble() < archetype.StudyConsistency;

            // Streak logic
            if (studiestoday)
            {
                consecutiveStudyDays++;
                streakDays = Math.Max(streakDays, consecutiveStudyDays);
            }
            else
            {
                consecutiveStudyDays = 0;
                continue;
            }

            totalSessions++;
            var sessionId = $"{studentId}-session-{totalSessions:D3}";

            // Pick concepts for this session (frontier + review mix)
            var sessionConcepts = SelectSessionConcepts(
                overlay, graphCache, conceptIds, archetype, rng);

            int attemptCount = Math.Max(5,
                (int)(archetype.TypicalSessionLength * (0.7 + rng.NextDouble() * 0.6)));

            for (int a = 0; a < attemptCount && sessionConcepts.Count > 0; a++)
            {
                // Pick concept (round-robin through session selection)
                var conceptId = sessionConcepts[a % sessionConcepts.Count];
                var concept = graphCache.Concepts[conceptId];

                // Get or create mastery state
                if (!overlay.TryGetValue(conceptId, out var state))
                    state = new ConceptMasteryState();

                // Generate accuracy for this concept (beta-distributed, concept difficulty modulated)
                float conceptAccuracy = SampleBetaAccuracy(
                    archetype.AccuracyMean, archetype.AccuracyStdDev,
                    concept.IntrinsicLoad, rng);

                bool isCorrect = rng.NextDouble() < conceptAccuracy;

                // Generate response time (log-normal, difficulty modulated)
                int responseTimeMs = SampleLogNormalResponseTime(
                    archetype.ResponseTimeMeanMs, archetype.ResponseTimeStdDevMs,
                    concept.IntrinsicLoad, rng);

                // Classify error type if incorrect
                ErrorType? errorType = null;
                if (!isCorrect)
                    errorType = SampleErrorType(archetype.ErrorTypeWeights, rng);

                var timestamp = dayStart.AddMinutes(a * 2 + rng.NextDouble() * 1.5);
                float priorMastery = state.MasteryProbability;

                // Run BKT update
                var bktParams = Mastery.BktParameters.Default;
                state = state.WithAttempt(isCorrect, timestamp);
                float newBkt = BktTracer.Update(priorMastery, isCorrect, bktParams);
                state = state.WithBktUpdate(newBkt);

                // Run HLR update
                var hlrFeatures = new HlrFeatures(
                    state.AttemptCount, state.CorrectCount,
                    concept.IntrinsicLoad,
                    concept.DepthLevel,
                    Math.Min(state.BloomLevel, concept.BloomMax),
                    state.FirstEncounter != default
                        ? (float)(timestamp - state.FirstEncounter).TotalDays : 0f);
                float halfLife = HlrCalculator.ComputeHalfLife(hlrFeatures, HlrWeights.Default);
                halfLife *= archetype.DecayResistance; // archetype-specific retention
                state = state.WithHalfLifeUpdate(Math.Clamp(halfLife, 1f, 8760f));

                // Track errors
                if (errorType.HasValue)
                    state = state.WithRecentError(errorType.Value);

                // Classify quality
                var quality = MasteryQualityClassifier.Classify(
                    isCorrect, responseTimeMs, baseline.MedianResponseTimeMs);
                state = state with { QualityQuadrant = quality };
                baseline = baseline.Update(responseTimeMs);

                // Progress Bloom level (stochastic, bounded by concept max)
                if (isCorrect && state.CurrentStreak >= 3 && rng.NextDouble() < 0.3)
                {
                    int newBloom = Math.Min(state.BloomLevel + 1, concept.BloomMax);
                    state = state.WithBloomLevel(newBloom);
                }

                // Compute effective mastery
                float prereq = PrerequisiteCalculator.ComputeSupport(conceptId, overlay, graphCache);
                float effective = EffectiveMasteryCalculator.Compute(state, prereq, timestamp);

                // Update Elo
                float expectedCorrect = EloScoring.ExpectedCorrectness(eloTheta, concept.IntrinsicLoad * 2000);
                var (newTheta, _) = EloScoring.UpdateRatings(
                    eloTheta, concept.IntrinsicLoad * 2000,
                    isCorrect, EloScoring.StudentKFactor(attempts.Count), 10f);
                eloTheta = newTheta;

                overlay[conceptId] = state;

                attempts.Add(new SimulatedAttempt(
                    studentId, conceptId, sessionId, isCorrect, responseTimeMs,
                    errorType, priorMastery, newBkt, effective, quality, timestamp));
            }
        }

        return new SimulatedStudent(studentId, archetype.Name, overlay, attempts,
            baseline, eloTheta, totalSessions, streakDays);
    }

    // =========================================================================
    // STATISTICAL SAMPLING FUNCTIONS
    // =========================================================================

    /// <summary>
    /// Sample accuracy from a Beta distribution modulated by concept difficulty.
    /// Higher difficulty = lower accuracy. Uses Box-Muller approximation.
    /// </summary>
    private static float SampleBetaAccuracy(
        float mean, float stdDev, float conceptDifficulty, Random rng)
    {
        // Shift mean down for harder concepts
        float adjustedMean = mean - (conceptDifficulty * 0.15f);
        adjustedMean = Math.Clamp(adjustedMean, 0.10f, 0.98f);

        // Sample from normal, clamp to [0.05, 0.99]
        float sample = SampleNormal(adjustedMean, stdDev, rng);
        return Math.Clamp(sample, 0.05f, 0.99f);
    }

    /// <summary>
    /// Sample response time from log-normal distribution.
    /// Produces realistic right-skewed distribution (most answers moderate, some very slow).
    /// </summary>
    private static int SampleLogNormalResponseTime(
        float meanMs, float stdDevMs, float conceptDifficulty, Random rng)
    {
        // Harder concepts take longer
        float adjustedMean = meanMs * (1f + conceptDifficulty * 0.5f);

        // Log-normal: exp(Normal(mu, sigma))
        float mu = MathF.Log(adjustedMean) - 0.5f * MathF.Log(1f + (stdDevMs * stdDevMs) / (adjustedMean * adjustedMean));
        float sigma = MathF.Sqrt(MathF.Log(1f + (stdDevMs * stdDevMs) / (adjustedMean * adjustedMean)));

        float logSample = SampleNormal(mu, sigma, rng);
        int ms = (int)MathF.Exp(logSample);
        return Math.Clamp(ms, 1_000, 300_000); // 1s - 5min
    }

    /// <summary>
    /// Sample error type from weighted categorical distribution.
    /// </summary>
    private static ErrorType SampleErrorType(float[] weights, Random rng)
    {
        float total = weights.Sum();
        float roll = (float)rng.NextDouble() * total;
        float cumulative = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
                return (ErrorType)i;
        }

        return ErrorType.Procedural;
    }

    /// <summary>
    /// Box-Muller transform for normal distribution sampling.
    /// </summary>
    private static float SampleNormal(float mean, float stdDev, Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * (float)z;
    }

    /// <summary>
    /// Select concepts for a session: mix of frontier (new learning) and review (decayed).
    /// </summary>
    private static List<string> SelectSessionConcepts(
        Dictionary<string, ConceptMasteryState> overlay,
        InMemoryGraphCache graphCache,
        List<string> allConceptIds,
        StudentArchetype archetype,
        Random rng)
    {
        var selected = new List<string>();

        // 60% new frontier concepts, 40% review
        var frontier = LearningFrontierCalculator.ComputeFrontier(
            overlay, graphCache, DateTimeOffset.UtcNow, maxResults: 8);

        foreach (var f in frontier.Take(5))
            selected.Add(f.ConceptId);

        // Add review concepts (decayed)
        var decayed = overlay
            .Where(kv => kv.Value.MasteryProbability >= 0.70f &&
                         kv.Value.HalfLifeHours > 0 &&
                         kv.Value.LastInteraction != default)
            .OrderBy(kv => kv.Value.RecallProbability(DateTimeOffset.UtcNow))
            .Take(3)
            .Select(kv => kv.Key);
        selected.AddRange(decayed);

        // If nothing selected yet (brand new student), start with depth-1 concepts
        if (selected.Count == 0)
        {
            selected.AddRange(allConceptIds
                .Where(c => graphCache.GetDepth(c) == 1)
                .OrderBy(_ => rng.Next())
                .Take(4));
        }

        return selected.Distinct().ToList();
    }
}
