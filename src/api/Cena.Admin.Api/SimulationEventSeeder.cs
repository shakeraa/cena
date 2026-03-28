// =============================================================================
// Cena Platform -- Simulation Event Seeder
// Persists MasterySimulator output as real Marten event streams.
// ~75,000+ events across 300 students, 60 days of learning history.
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
        int totalStudents = 300,
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

        // Seed tutoring session documents
        await SeedTutoringSessionDocumentsAsync(store, logger, cohort, simulationDays, seed);
    }

    /// <summary>
    /// Seed TutoringSessionDocument records — one per student on average (300 total),
    /// with normally-distributed turn counts and durations.
    /// Idempotent: skips if documents already exist.
    /// </summary>
    private static async Task SeedTutoringSessionDocumentsAsync(
        IDocumentStore store,
        ILogger logger,
        IReadOnlyList<SimulatedStudent> cohort,
        int simulationDays,
        int seed)
    {
        await using var checkSession = store.QuerySession();
        var existingCount = await checkSession.Query<TutoringSessionDocument>().CountAsync();
        if (existingCount > 0)
        {
            logger.LogInformation(
                "Tutoring session documents already seeded ({Count} exist). Skipping.",
                existingCount);
            return;
        }

        var rng = new Random(seed + 7); // offset seed to avoid correlation with event seeder
        var startDate = DateTimeOffset.Parse("2025-09-01T08:00:00+03:00");

        var conceptIds = new[]
        {
            "ALG-001", "ALG-002", "ALG-003", "ALG-004", "ALG-005", "ALG-006", "ALG-007", "ALG-008",
            "FUN-001", "FUN-002", "FUN-003", "FUN-004", "FUN-005", "FUN-006", "FUN-007",
            "GEO-001", "GEO-002", "GEO-003", "GEO-004", "GEO-005", "GEO-006", "GEO-007",
            "TRG-001", "TRG-002", "TRG-003", "TRG-004",
            "CAL-001", "CAL-002", "CAL-003", "CAL-004", "CAL-005", "CAL-006",
            "PRB-001", "PRB-002", "PRB-003", "PRB-004", "PRB-005", "PRB-006",
            "VEC-001", "VEC-002", "VEC-003", "VEC-004"
        };

        var methodologies = new[] { "Socratic", "SpacedRepetition", "Feynman", "WorkedExample", "DrillAndPractice" };

        int totalDocs = 0;
        int batchSize = 20;

        // Create ~300 tutoring sessions distributed across students
        // Use a round-robin with some randomness so each student gets ~1 session on average
        var sessionAssignments = new List<(SimulatedStudent Student, int Index)>();
        for (int i = 0; i < cohort.Count; i++)
        {
            sessionAssignments.Add((cohort[i], 0));
        }

        for (int i = 0; i < sessionAssignments.Count; i += batchSize)
        {
            await using var session = store.LightweightSession();
            var batch = sessionAssignments.Skip(i).Take(batchSize);

            foreach (var (student, index) in batch)
            {
                var conceptId = conceptIds[rng.Next(conceptIds.Length)];
                var methodology = methodologies[rng.Next(methodologies.Length)];

                // Normally distributed turn count: mean=6, stddev=2, clamped to 3-12
                var turnCount = Math.Clamp((int)Math.Round(NormalSample(rng, 6.0, 2.0)), 3, 12);

                // Normally distributed start time within the simulation window
                var dayOffset = Math.Clamp(
                    (int)Math.Round(NormalSample(rng, simulationDays / 2.0, simulationDays / 4.0)),
                    0, simulationDays - 1);
                var hourOffset = 8 + rng.Next(0, 10); // school hours 8:00-18:00
                var minuteOffset = rng.Next(0, 60);
                var startedAt = startDate.AddDays(dayOffset).AddHours(hourOffset).AddMinutes(minuteOffset);

                // Normally distributed duration: mean=12min, stddev=5min, clamped to 5-30
                var durationMinutes = Math.Clamp(NormalSample(rng, 12.0, 5.0), 5.0, 30.0);
                var endedAt = startedAt.AddMinutes(durationMinutes);

                // Find a matching session ID from the student's attempt history
                var sessionId = student.AttemptHistory.Count > 0
                    ? student.AttemptHistory[rng.Next(student.AttemptHistory.Count)].SessionId
                    : $"{student.StudentId}-session-001";

                // Build conversation turns
                var turns = new List<ConversationTurn>();
                var turnTime = startedAt;
                var turnInterval = TimeSpan.FromMinutes(durationMinutes / turnCount);

                for (int t = 0; t < turnCount; t++)
                {
                    var role = t % 2 == 0 ? "student" : "tutor";
                    var content = role == "student"
                        ? $"[simulated student message #{t + 1} about {conceptId}]"
                        : $"[simulated tutor response #{t + 1} for {conceptId} using {methodology}]";
                    turns.Add(new ConversationTurn(role, content, turnTime));
                    turnTime = turnTime.Add(turnInterval);
                }

                var doc = new TutoringSessionDocument
                {
                    Id = $"tutor-sim-{student.StudentId}-{index}",
                    StudentId = student.StudentId,
                    SessionId = sessionId,
                    ConceptId = conceptId,
                    Subject = "math",
                    Methodology = methodology,
                    Turns = turns,
                    TotalTurns = turnCount,
                    StartedAt = startedAt,
                    EndedAt = endedAt
                };

                session.Store(doc);
                totalDocs++;
            }

            await session.SaveChangesAsync();
        }

        logger.LogInformation(
            "Tutoring session documents seeded: {Count} documents across {Students} students",
            totalDocs, cohort.Count);
    }

    /// <summary>
    /// Box-Muller transform for normally-distributed random values.
    /// </summary>
    private static double NormalSample(Random rng, double mean, double stddev)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stddev * z;
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
