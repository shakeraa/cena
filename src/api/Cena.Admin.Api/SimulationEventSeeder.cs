// =============================================================================
// Cena Platform -- Simulation Event Seeder
// Persists MasterySimulator output as real Marten event streams.
// ~25,000+ events across 100 students, 60 days of learning history.
// Includes SAI-layer events: HintRequested, AnnotationAdded, TutoringEpisodeCompleted.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Simulation;
using Cena.Actors.Tutoring;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

/// <summary>
/// Converts simulated student data into real event-sourced streams in Marten.
/// Each student gets a stream with SessionStarted, ConceptAttempted, SessionEnded,
/// XpAwarded, StreakUpdated, HintRequested, AnnotationAdded, and TutoringEpisodeCompleted
/// events — the same events the live system produces.
/// </summary>
public static class SimulationEventSeeder
{
    /// <summary>
    /// Generate a realistic cohort and persist all events to Marten.
    /// Idempotent: skips if streams already exist.
    /// </summary>
    public static async Task SeedSimulationEventsAsync(
        IDocumentStore store,
        ILogger logger,
        int totalStudents = 100,
        int simulationDays = 60,
        int seed = 42)
    {
        // Check if already seeded by looking for a known stream
        await using var checkSession = store.QuerySession();
        var existingStream = await checkSession.Events.FetchStreamStateAsync("sim-genius-001");
        if (existingStream != null)
        {
            logger.LogInformation(
                "Simulation events already seeded (stream sim-genius-001 exists with {Version} events). Skipping.",
                existingStream.Version);
            return;
        }

        logger.LogInformation(
            "Generating {Students}-student simulation cohort ({Days} days)...",
            totalStudents, simulationDays);

        var cohort = MasterySimulator.GenerateRealisticCohort(totalStudents, simulationDays, seed);

        logger.LogInformation(
            "Simulation generated: {Students} students, {TotalAttempts} total attempts. Persisting to Marten...",
            cohort.Count, cohort.Sum(s => s.AttemptHistory.Count));

        int totalEvents = 0;
        int batchSize = 10; // Students per batch to avoid huge transactions

        for (int i = 0; i < cohort.Count; i += batchSize)
        {
            await using var session = store.LightweightSession();
            var batch = cohort.Skip(i).Take(batchSize);

            foreach (var student in batch)
            {
                var events = BuildEventStream(student);
                if (events.Count == 0) continue;

                session.Events.StartStream<object>(student.StudentId, events.ToArray());
                totalEvents += events.Count;
            }

            await session.SaveChangesAsync();
        }

        logger.LogInformation(
            "Simulation events seeded: {Events} events across {Students} student streams",
            totalEvents, cohort.Count);
    }

    /// <summary>
    /// Convert a SimulatedStudent into a chronological list of domain events.
    /// Groups attempts into sessions, adds engagement events (XP, streaks),
    /// SAI events (hints, annotations, tutoring episodes).
    /// </summary>
    private static List<object> BuildEventStream(SimulatedStudent student)
    {
        var events = new List<object>();
        var rng = new Random(student.StudentId.GetHashCode());

        // Group attempts by session
        var sessions = student.AttemptHistory
            .GroupBy(a => a.SessionId)
            .OrderBy(g => g.First().Timestamp)
            .ToList();

        int totalXp = 0;
        int currentStreak = 0;
        int longestStreak = 0;
        DateTimeOffset? lastSessionDate = null;

        foreach (var session in sessions)
        {
            var attempts = session.OrderBy(a => a.Timestamp).ToList();
            var firstAttempt = attempts[0];
            var lastAttempt = attempts[^1];
            var sessionDate = firstAttempt.Timestamp.Date;

            // Streak tracking
            if (lastSessionDate.HasValue)
            {
                var dayGap = (sessionDate - lastSessionDate.Value.Date).Days;
                if (dayGap <= 1)
                    currentStreak++;
                else
                    currentStreak = 1;
            }
            else
            {
                currentStreak = 1;
            }
            longestStreak = Math.Max(longestStreak, currentStreak);
            lastSessionDate = firstAttempt.Timestamp;

            // SessionStarted
            var methodology = student.ArchetypeName switch
            {
                "Genius" => "Socratic",
                "HighAchiever" => "BloomsProgression",
                "SteadyLearner" => "Socratic",
                "Struggling" => "WorkedExample",
                "FastCareless" => "DrillAndPractice",
                "SlowThorough" => "Feynman",
                "Inconsistent" => "SpacedRepetition",
                "VeryLowCognitive" => "WorkedExample",
                _ => "Socratic"
            };

            events.Add(new SessionStarted_V1(
                student.StudentId,
                session.Key,
                rng.NextDouble() < 0.7 ? "mobile" : "desktop",
                "1.0.0",
                methodology,
                student.ArchetypeName switch
                {
                    "Genius" => "cohort-A",
                    "VeryLowCognitive" => "cohort-C",
                    _ => "cohort-B"
                },
                false,
                firstAttempt.Timestamp));

            // ConceptAttempted events + SAI events
            int sessionCorrect = 0;
            int consecutiveWrong = 0;
            bool tutoringTriggeredThisSession = false;

            foreach (var attempt in attempts)
            {
                if (attempt.IsCorrect)
                {
                    sessionCorrect++;
                    consecutiveWrong = 0;
                }
                else
                {
                    consecutiveWrong++;
                }

                // Determine hint usage based on archetype
                var hintCount = student.ArchetypeName switch
                {
                    "Struggling" => attempt.IsCorrect ? (rng.NextDouble() < 0.3 ? 1 : 0) : (rng.NextDouble() < 0.6 ? rng.Next(1, 3) : 0),
                    "VeryLowCognitive" => rng.NextDouble() < 0.5 ? rng.Next(1, 3) : 0,
                    "SlowThorough" => rng.NextDouble() < 0.2 ? 1 : 0,
                    _ => rng.NextDouble() < 0.1 ? 1 : 0
                };

                var questionId = $"q-{rng.Next(1, 16):0000}";

                events.Add(new ConceptAttempted_V1(
                    student.StudentId,
                    attempt.ConceptId,
                    session.Key,
                    attempt.IsCorrect,
                    attempt.ResponseTimeMs,
                    questionId,
                    "multiple_choice",
                    methodology,
                    attempt.ClassifiedError?.ToString() ?? "None",
                    attempt.PriorMastery,
                    attempt.PosteriorMastery,
                    hintCount,
                    false,
                    $"h{rng.Next(100000):x5}",
                    rng.Next(0, 5),
                    rng.Next(0, 3),
                    false,
                    attempt.Timestamp));

                // SAI: Emit HintRequested events for each hint level used
                for (int h = 1; h <= hintCount; h++)
                {
                    events.Add(new HintRequested_V1(
                        student.StudentId,
                        session.Key,
                        attempt.ConceptId,
                        questionId,
                        h));
                }

                // SAI: Confusion annotation after 3+ consecutive wrong answers
                if (consecutiveWrong >= 3 && rng.NextDouble() < 0.35)
                {
                    var annotationId = $"ann-{Guid.NewGuid():N}"[..16];
                    var sentiment = -0.2 - rng.NextDouble() * 0.5; // negative sentiment for confusion
                    events.Add(new AnnotationAdded_V1(
                        student.StudentId,
                        attempt.ConceptId,
                        annotationId,
                        $"h{rng.Next(100000):x5}",
                        sentiment,
                        "confusion"));
                }

                // SAI: Question annotation from curious students after correct answers
                if (attempt.IsCorrect && rng.NextDouble() < 0.02 &&
                    student.ArchetypeName is "Genius" or "HighAchiever" or "SteadyLearner")
                {
                    var annotationId = $"ann-{Guid.NewGuid():N}"[..16];
                    events.Add(new AnnotationAdded_V1(
                        student.StudentId,
                        attempt.ConceptId,
                        annotationId,
                        $"h{rng.Next(100000):x5}",
                        0.3 + rng.NextDouble() * 0.4, // positive/neutral sentiment for questions
                        "question"));
                }

                // XP for correct answers
                if (attempt.IsCorrect)
                {
                    int xp = 10 + (int)(attempt.PosteriorMastery * 20);
                    totalXp += xp;
                    events.Add(new XpAwarded_V1(
                        student.StudentId, xp, "exercise_correct",
                        totalXp, "recall", 1));
                }

                // ConceptMastered when crossing threshold
                if (attempt.PosteriorMastery >= 0.85f && attempt.PriorMastery < 0.85f)
                {
                    events.Add(new ConceptMastered_V1(
                        student.StudentId,
                        attempt.ConceptId,
                        session.Key,
                        attempt.PosteriorMastery,
                        attempts.Count(a => a.ConceptId == attempt.ConceptId),
                        sessions.TakeWhile(s => s.Key != session.Key).Count() + 1,
                        methodology,
                        24.0,
                        attempt.Timestamp));
                }

                // SAI: Tutoring episode after sustained struggle (5+ wrong, once per session)
                if (consecutiveWrong >= 5 && !tutoringTriggeredThisSession && rng.NextDouble() < 0.4)
                {
                    tutoringTriggeredThisSession = true;
                    var tutoringDuration = TimeSpan.FromMinutes(2 + rng.NextDouble() * 6);
                    var turnCount = 2 + rng.Next(0, 7); // 2-8 turns
                    var triggerType = rng.NextDouble() < 0.5 ? "confusion_stuck" : "post_wrong_answer";
                    var resolution = rng.NextDouble() < 0.6 ? "resolved" : "student_ended";

                    events.Add(new TutoringEpisodeCompleted_V1(
                        StudentId: student.StudentId,
                        SessionId: session.Key,
                        ConceptId: attempt.ConceptId,
                        TriggerType: triggerType,
                        Methodology: methodology,
                        TurnCount: turnCount,
                        Duration: tutoringDuration,
                        ResolutionStatus: resolution,
                        Timestamp: attempt.Timestamp.AddMinutes(1)));
                }
            }

            // SessionEnded
            var durationMinutes = (int)(lastAttempt.Timestamp - firstAttempt.Timestamp).TotalMinutes + 1;
            var avgRt = attempts.Average(a => a.ResponseTimeMs);
            var fatigueScore = Math.Min(1.0, attempts.Count / 30.0 * 0.7 + durationMinutes / 45.0 * 0.3);

            events.Add(new SessionEnded_V1(
                student.StudentId,
                session.Key,
                fatigueScore > 0.8 ? "fatigue" : "completed",
                durationMinutes,
                attempts.Count,
                sessionCorrect,
                avgRt,
                fatigueScore));

            // StreakUpdated
            events.Add(new StreakUpdated_V1(
                student.StudentId,
                currentStreak,
                longestStreak,
                firstAttempt.Timestamp));
        }

        return events;
    }
}
