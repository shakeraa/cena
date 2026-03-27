// =============================================================================
// Cena Platform -- Student Emulator Service
// Simulates 100 students interacting with the actor system via NATS.
//
// Phase 1: Publishes commands on NATS (session start, concept attempts, session end)
// Phase 2: Actor Host subscribes, processes, and publishes events back
//
// Usage: dotnet run -- [--students 100] [--speed 10] [--nats nats://localhost:4222]
// =============================================================================

using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Simulation;
using NATS.Client.Core;
using Cena.Emulator;

// ── Configuration ──

var studentCount = args.FirstOrDefault(a => a.StartsWith("--students"))?.Split('=').LastOrDefault() is string sc ? int.Parse(sc) : 100;
var speedMultiplier = args.FirstOrDefault(a => a.StartsWith("--speed"))?.Split('=').LastOrDefault() is string sp ? float.Parse(sp) : 10f;
var natsUrl = args.FirstOrDefault(a => a.StartsWith("--nats"))?.Split('=').LastOrDefault() ?? "nats://localhost:4222";

Log.Information("╔══════════════════════════════════════════════════╗");
Log.Information("║  Cena Student Emulator                          ║");
Log.Information("║  Students: {Students}, Speed: {Speed}x, NATS: {Nats}", studentCount, speedMultiplier, natsUrl);
Log.Information("╚══════════════════════════════════════════════════╝");

// ── Connect to NATS ──

var natsOpts = new NatsOpts { Url = natsUrl, Name = "cena-emulator" };
await using var nats = new NatsConnection(natsOpts);
await nats.ConnectAsync();
Log.Information("Connected to NATS at {Url}", natsUrl);

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
var rng = new Random(42);
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

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
                rng.Next(0, 2),
                false,
                rng.Next(0, 10),
                rng.Next(0, 3)),
            "emulator");

        await nats.PublishAsync(NatsSubjects.ConceptAttempt,
            JsonSerializer.Serialize(attemptMsg, jsonOpts), cancellationToken: cts.Token);
        totalAttempts++;

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
        }

        // Progress logging
        if (totalAttempts % 500 == 0)
        {
            var elapsed = DateTimeOffset.UtcNow - startTime;
            Log.Information("  → Published {Attempts} attempts, {Sessions} sessions ({Rate}/sec, {Events} events received)",
                totalAttempts, totalSessions,
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
Log.Information("║  Events received: {Events}", eventCount);
Log.Information("║  Duration: {Duration:F1}s, Rate: {Rate}/sec", totalElapsed.TotalSeconds, (int)(totalAttempts / Math.Max(1, totalElapsed.TotalSeconds)));
Log.Information("╚══════════════════════════════════════════════════╝");

cts.Cancel();
await Task.WhenAny(eventSub, Task.Delay(2000));
