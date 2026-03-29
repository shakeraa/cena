// =============================================================================
// Cena Platform -- Student Emulator Service
// Simulates 100 students interacting with the actor system via NATS.
//
// Publishes: session start/end, concept attempts, annotations, methodology switches.
// Reflects SAI layer features: confusion annotations, question annotations,
// hint usage patterns, and methodology switches on stagnation.
//
// Usage: dotnet run -- [--students 100] [--speed 10] [--nats nats://localhost:4222]
// =============================================================================

using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Simulation;
using NATS.Client.Core;
using Cena.Emulator;

// ── Configuration ──

var studentCount = args.FirstOrDefault(a => a.StartsWith("--students"))?.Split('=').LastOrDefault() is string sc ? int.Parse(sc) : 300;
var speedMultiplier = args.FirstOrDefault(a => a.StartsWith("--speed"))?.Split('=').LastOrDefault() is string sp ? float.Parse(sp) : 10f;
var natsUrl = args.FirstOrDefault(a => a.StartsWith("--nats"))?.Split('=').LastOrDefault() ?? "nats://localhost:4222";

Log.Information("╔══════════════════════════════════════════════════╗");
Log.Information("║  Cena Student Emulator                          ║");
Log.Information("║  Students: {Students}, Speed: {Speed}x, NATS: {Nats}", studentCount, speedMultiplier, natsUrl);
Log.Information("╚══════════════════════════════════════════════════╝");

// ── Connect to NATS ──

// REV-002: NATS authentication — emulator user with subject ACLs
var natsUser = Environment.GetEnvironmentVariable("NATS_EMU_USER") ?? "emulator";
var natsPass = Environment.GetEnvironmentVariable("NATS_EMU_PASSWORD") ?? "dev_emu_pass";

var natsOpts = new NatsOpts
{
    Url = natsUrl,
    Name = "cena-emulator",
    AuthOpts = new NatsAuthOpts { Username = natsUser, Password = natsPass },
};
await using var nats = new NatsConnection(natsOpts);
await nats.ConnectAsync();
Log.Information("Connected to NATS at {Url} as {User}", natsUrl, natsUser);

// ── Generate student cohort using simulation engine ──

var cohort = MasterySimulator.GenerateRealisticCohort(studentCount, simulationDays: 60, seed: 42);
Log.Information("Generated {Count} simulated students across {Archetypes} archetypes",
    cohort.Count, cohort.Select(s => s.ArchetypeName).Distinct().Count());

// ── Curriculum data for concept selection ──

var (concepts, edges) = CurriculumSeedData.BuildBagrutMathCurriculum();
var conceptIds = concepts.Select(c => c.Id).ToList();
var conceptNames = concepts.ToDictionary(c => c.Id, c => c.Name);

// ── Emulation state ──

var activeSessionIds = new Dictionary<string, string>(); // studentId → sessionId
var studentMethodology = new Dictionary<string, string>(); // studentId → current methodology
var consecutiveWrong = new Dictionary<string, int>(); // studentId → consecutive wrong count
var studentQuestionIndex = new Dictionary<string, int>(); // studentId → questions answered this session
var studentSessionStart = new Dictionary<string, DateTimeOffset>(); // studentId → session start time
var rng = new Random(42);
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Methodology pool (matches StudentMessages.Methodology enum) ──
var methodologies = new[] { "Socratic", "SpacedRepetition", "Feynman", "WorkedExample", "DrillAndPractice", "BloomsProgression", "RetrievalPractice" };

// ── Confusion annotation templates (Hebrew + English) ──
var confusionTexts = new[]
{
    "I don't understand this step",
    "Why does the formula work this way?",
    "אני לא מבין למה זה עובד ככה",
    "מה ההבדל בין זה לנוסחה הקודמת?",
    "Can someone explain the connection to the previous topic?",
    "Why do we need to use this approach?",
    "אני מבולבל מהשלב הזה"
};

var questionTexts = new[]
{
    "Is there an easier way to solve this?",
    "Can you show a worked example?",
    "יש דרך יותר פשוטה?",
    "אפשר דוגמה נוספת?",
    "How is this related to the next topic?",
    "What happens if the sign is negative?"
};

// ── Subscribe to events (to log what comes back) ──

var eventCount = 0;
var eventSub = Task.Run(async () =>
{
    try
    {
        await foreach (var msg in nats.SubscribeAsync<string>(NatsSubjects.AllEvents, cancellationToken: cts.Token))
        {
            Interlocked.Increment(ref eventCount);
            if (eventCount % 50 == 0)
                Log.Information("  ← Received {Count} events from actor host", eventCount);
        }
    }
    catch (OperationCanceledException) { }
});

// ── Main emulation loop ──

Log.Information("Starting emulation... (Ctrl+C to stop)");
Log.Information("Simulating {Days} days of student activity at {Speed}x speed", 60, speedMultiplier);

var totalAttempts = 0;
var totalSessions = 0;
var totalAnnotations = 0;
var totalMethodologySwitches = 0;
var totalFocusEvents = 0;
var startTime = DateTimeOffset.UtcNow;

try
{
    // Process each student's attempt history chronologically
    var allAttempts = cohort
        .SelectMany(s => s.AttemptHistory.Select(a => (Student: s, Attempt: a)))
        .OrderBy(x => x.Attempt.Timestamp)
        .ToList();

    Log.Information("Replaying {Total} concept attempts across {Students} students...", allAttempts.Count, cohort.Count);

    DateTimeOffset? lastTimestamp = null;

    foreach (var (student, attempt) in allAttempts)
    {
        if (cts.Token.IsCancellationRequested) break;

        // Simulate time gaps (compressed by speed multiplier)
        if (lastTimestamp.HasValue)
        {
            var gap = attempt.Timestamp - lastTimestamp.Value;
            var sleepMs = (int)(gap.TotalMilliseconds / speedMultiplier);
            if (sleepMs > 0 && sleepMs < 5000) // cap at 5s real time
                await Task.Delay(Math.Min(sleepMs, 100), cts.Token);
        }
        lastTimestamp = attempt.Timestamp;

        // Start session if needed
        if (!activeSessionIds.ContainsKey(student.StudentId))
        {
            var sessionId = $"sess-{Guid.NewGuid():N}"[..16];
            activeSessionIds[student.StudentId] = sessionId;
            consecutiveWrong[student.StudentId] = 0;
            studentQuestionIndex[student.StudentId] = 0;
            studentSessionStart[student.StudentId] = DateTimeOffset.UtcNow;

            // Assign initial methodology based on archetype
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
            studentMethodology[student.StudentId] = methodology;

            var startMsg = BusEnvelope<BusStartSession>.Create(
                NatsSubjects.SessionStart,
                new BusStartSession(
                    student.StudentId,
                    "math",
                    attempt.ConceptId,
                    rng.NextDouble() > 0.3 ? "mobile" : "desktop",
                    "1.0.0",
                    attempt.Timestamp),
                "emulator");

            await nats.PublishAsync(NatsSubjects.SessionStart,
                JsonSerializer.Serialize(startMsg, jsonOpts), cancellationToken: cts.Token);
            totalSessions++;
        }

        // Publish concept attempt
        var sessionForStudent = activeSessionIds[student.StudentId];

        // Simulate hint usage: struggling/VeryLowCognitive students use hints more often
        var hintCountUsed = student.ArchetypeName switch
        {
            "Struggling" => attempt.IsCorrect ? rng.Next(0, 2) : rng.Next(1, 3),
            "VeryLowCognitive" => rng.Next(1, 3),
            "SlowThorough" => rng.Next(0, 2),
            _ => rng.NextDouble() < 0.1 ? 1 : 0
        };

        var attemptMsg = BusEnvelope<BusConceptAttempt>.Create(
            NatsSubjects.ConceptAttempt,
            new BusConceptAttempt(
                student.StudentId,
                sessionForStudent,
                attempt.ConceptId,
                $"q-{rng.Next(1, 1500):D4}",
                "multiple_choice",
                attempt.IsCorrect ? "correct" : "wrong",
                attempt.ResponseTimeMs,
                hintCountUsed,
                false,
                rng.Next(0, 10),
                rng.Next(0, 3)),
            "emulator");

        await nats.PublishAsync(NatsSubjects.ConceptAttempt,
            JsonSerializer.Serialize(attemptMsg, jsonOpts), cancellationToken: cts.Token);
        totalAttempts++;

        // ── Focus Analytics: Compute and publish focus score ──
        var qIdx = studentQuestionIndex.GetValueOrDefault(student.StudentId);
        var sessStart = studentSessionStart.GetValueOrDefault(student.StudentId, DateTimeOffset.UtcNow);
        var minutesActive = (DateTimeOffset.UtcNow - sessStart).TotalMinutes;
        var baseFocus = Math.Max(0.3, 0.85 - (minutesActive / 120.0) * 0.3);
        var focusScore = Math.Clamp(baseFocus + (rng.NextDouble() - 0.5) * 0.1, 0.0, 1.0);
        var focusLevel = focusScore >= 0.8 ? "Flow" : focusScore >= 0.6 ? "Engaged" : focusScore >= 0.4 ? "Drifting" : "Fatigued";

        await nats.PublishAsync(NatsSubjects.EventFocusUpdated,
            JsonSerializer.Serialize(new
            {
                studentId = student.StudentId,
                sessionId = sessionForStudent,
                questionNumber = qIdx,
                focusScore = Math.Round(focusScore, 3),
                focusLevel,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOpts), cancellationToken: cts.Token);
        totalFocusEvents++;

        // Mind wandering detection every 7 questions
        if (qIdx % 7 == 0 && focusScore < 0.55 && rng.NextDouble() < 0.6)
        {
            var driftType = focusScore < 0.35 ? "UnawareDrift" : "AwareDrift";
            await nats.PublishAsync(NatsSubjects.EventMindWandering,
                JsonSerializer.Serialize(new
                {
                    studentId = student.StudentId,
                    sessionId = sessionForStudent,
                    driftType,
                    confidence = Math.Round(0.6 + rng.NextDouble() * 0.3, 3),
                    context = focusScore < 0.4 ? "sustained_slow_rt" : "erratic_pattern",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOpts), cancellationToken: cts.Token);
            totalFocusEvents++;
        }

        // Microbreak suggestion every 8 questions
        if (qIdx > 0 && qIdx % 8 == 0)
        {
            var activities = new[] { "StretchBreak", "BreathingExercise", "LookAway", "WaterBreak", "MiniWalk" };
            var activity = activities[rng.Next(activities.Length)];
            var duration = focusScore < 0.5 ? 90 : 60;

            await nats.PublishAsync(NatsSubjects.EventMicrobreakSuggested,
                JsonSerializer.Serialize(new
                {
                    studentId = student.StudentId,
                    sessionId = sessionForStudent,
                    questionsSinceBreak = 8,
                    elapsedMinutes = Math.Round(minutesActive, 1),
                    activity,
                    durationSeconds = duration,
                    reason = $"Reached {qIdx} questions",
                    taken = rng.NextDouble() < 0.68,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOpts), cancellationToken: cts.Token);
            totalFocusEvents++;
        }

        studentQuestionIndex[student.StudentId] = qIdx + 1;

        // ── SAI: Track consecutive wrong answers for confusion/stagnation ──
        if (!attempt.IsCorrect)
        {
            consecutiveWrong[student.StudentId] = consecutiveWrong.GetValueOrDefault(student.StudentId) + 1;
        }
        else
        {
            consecutiveWrong[student.StudentId] = 0;
        }

        // ── SAI: Publish confusion annotation after 3+ consecutive wrong answers ──
        var wrongStreak = consecutiveWrong.GetValueOrDefault(student.StudentId);
        if (wrongStreak >= 3 && rng.NextDouble() < 0.4)
        {
            var confusionText = confusionTexts[rng.Next(confusionTexts.Length)];
            var annotationMsg = BusEnvelope<BusAddAnnotation>.Create(
                NatsSubjects.Annotation,
                new BusAddAnnotation(
                    student.StudentId,
                    sessionForStudent,
                    attempt.ConceptId,
                    confusionText,
                    "confusion"),
                "emulator");

            await nats.PublishAsync(NatsSubjects.Annotation,
                JsonSerializer.Serialize(annotationMsg, jsonOpts), cancellationToken: cts.Token);
            totalAnnotations++;
        }

        // ── SAI: Publish question annotation occasionally (curious students) ──
        if (attempt.IsCorrect && rng.NextDouble() < 0.03 &&
            student.ArchetypeName is "Genius" or "HighAchiever" or "SteadyLearner")
        {
            var questionText = questionTexts[rng.Next(questionTexts.Length)];
            var annotationMsg = BusEnvelope<BusAddAnnotation>.Create(
                NatsSubjects.Annotation,
                new BusAddAnnotation(
                    student.StudentId,
                    sessionForStudent,
                    attempt.ConceptId,
                    questionText,
                    "question"),
                "emulator");

            await nats.PublishAsync(NatsSubjects.Annotation,
                JsonSerializer.Serialize(annotationMsg, jsonOpts), cancellationToken: cts.Token);
            totalAnnotations++;
        }

        // ── SAI: Methodology switch after sustained stagnation (5+ wrong in a row) ──
        if (wrongStreak >= 5 && rng.NextDouble() < 0.5)
        {
            var currentMethodology = studentMethodology.GetValueOrDefault(student.StudentId, "Socratic");
            var newMethodology = methodologies.Where(m => m != currentMethodology).ElementAt(rng.Next(methodologies.Length - 1));
            studentMethodology[student.StudentId] = newMethodology;

            var switchMsg = BusEnvelope<BusMethodologySwitch>.Create(
                NatsSubjects.MethodologySwitch,
                new BusMethodologySwitch(
                    student.StudentId,
                    sessionForStudent,
                    currentMethodology,
                    newMethodology,
                    "stagnation_auto_switch"),
                "emulator");

            await nats.PublishAsync(NatsSubjects.MethodologySwitch,
                JsonSerializer.Serialize(switchMsg, jsonOpts), cancellationToken: cts.Token);
            totalMethodologySwitches++;
            consecutiveWrong[student.StudentId] = 0; // reset after switch
        }

        // End session randomly (after ~15 attempts per session)
        if (rng.NextDouble() < 0.07 && activeSessionIds.ContainsKey(student.StudentId))
        {
            var endMsg = BusEnvelope<BusEndSession>.Create(
                NatsSubjects.SessionEnd,
                new BusEndSession(
                    student.StudentId,
                    activeSessionIds[student.StudentId],
                    "completed"),
                "emulator");

            await nats.PublishAsync(NatsSubjects.SessionEnd,
                JsonSerializer.Serialize(endMsg, jsonOpts), cancellationToken: cts.Token);
            activeSessionIds.Remove(student.StudentId);
            studentMethodology.Remove(student.StudentId);
            consecutiveWrong.Remove(student.StudentId);
            studentQuestionIndex.Remove(student.StudentId);
            studentSessionStart.Remove(student.StudentId);
        }

        // Progress logging
        if (totalAttempts % 500 == 0)
        {
            var elapsed = DateTimeOffset.UtcNow - startTime;
            Log.Information("  → Published {Attempts} attempts, {Sessions} sessions, {Annotations} annotations, {Switches} switches, {Focus} focus ({Rate}/sec, {Events} events received)",
                totalAttempts, totalSessions, totalAnnotations, totalMethodologySwitches, totalFocusEvents,
                (int)(totalAttempts / elapsed.TotalSeconds),
                eventCount);
        }
    }

    // End all remaining sessions
    foreach (var (studentId, sessionId) in activeSessionIds)
    {
        var endMsg = BusEnvelope<BusEndSession>.Create(
            NatsSubjects.SessionEnd,
            new BusEndSession(studentId, sessionId, "emulator_shutdown"),
            "emulator");

        await nats.PublishAsync(NatsSubjects.SessionEnd,
            JsonSerializer.Serialize(endMsg, jsonOpts), cancellationToken: cts.Token);
    }
}
catch (OperationCanceledException)
{
    Log.Information("Emulation cancelled by user.");
}

var totalElapsed = DateTimeOffset.UtcNow - startTime;
Log.Information("╔══════════════════════════════════════════════════╗");
Log.Information("║  Emulation Complete                             ║");
Log.Information("║  Attempts: {Attempts}, Sessions: {Sessions}", totalAttempts, totalSessions);
Log.Information("║  Annotations: {Annotations}, Methodology switches: {Switches}", totalAnnotations, totalMethodologySwitches);
Log.Information("║  Focus events: {Focus}", totalFocusEvents);
Log.Information("║  Events received: {Events}", eventCount);
Log.Information("║  Duration: {Duration:F1}s, Rate: {Rate}/sec", totalElapsed.TotalSeconds, (int)(totalAttempts / Math.Max(1, totalElapsed.TotalSeconds)));
Log.Information("╚══════════════════════════════════════════════════╝");

cts.Cancel();
await Task.WhenAny(eventSub, Task.Delay(2000));
