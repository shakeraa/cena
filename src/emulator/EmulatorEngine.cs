// =============================================================================
// Cena Platform -- Emulator Engine
// Orchestrates the full simulation lifecycle:
//   1. Parse CLI args
//   2. Connect to NATS
//   3. Generate cohort (demographic profiles + mastery histories)
//   4. Run session simulator
//   5. Report statistics + round-trip validation results
//
// Usage:
//   dotnet run -- [--students=1000] [--speed=10] [--duration=60]
//                 [--schools=5] [--nats=nats://localhost:4222] [--seed=42]
// =============================================================================

using Cena.Actors.Bus;
using Cena.Emulator.Population;
using Cena.Emulator.Scheduler;
using Cena.Emulator.Simulation;
using NATS.Client.Core;

namespace Cena.Emulator;

/// <summary>
/// Parsed configuration for a single emulator run.
/// </summary>
public sealed record EmulatorConfig(
    int StudentCount,
    float SpeedMultiplier,
    int SimulationDays,
    int Seed,
    string NatsUrl,
    TimeSpan RoundTripTimeout);

/// <summary>
/// Orchestrates the emulator: NATS connection, cohort generation, simulation, reporting.
/// </summary>
public static class EmulatorEngine
{
    // Round-trip validation: count received events and correlate to commands
    private static int _eventsReceived;
    private static int _roundTripMatches;

    // ── Public entry point ────────────────────────────────────────────────────

    public static async Task RunAsync(string[] args)
    {
        var config = ParseArgs(args);
        PrintBanner(config);

        // ── NATS connection ───────────────────────────────────────────────────
        var natsUser = Environment.GetEnvironmentVariable("NATS_EMU_USER")     ?? "emulator";
        var natsPass = Environment.GetEnvironmentVariable("NATS_EMU_PASSWORD") ?? "dev_emu_pass";

        var natsOpts = new NatsOpts
        {
            Url      = config.NatsUrl,
            Name     = "cena-emulator",
            AuthOpts = new NatsAuthOpts { Username = natsUser, Password = natsPass },
        };

        await using var nats = new NatsConnection(natsOpts);
        await nats.ConnectAsync();
        Log.Information("Connected to NATS at {Url} as {User}", config.NatsUrl, natsUser);

        // ── Round-trip validation: subscribe to all events ────────────────────
        using var cts           = new CancellationTokenSource();
        var       eventSubTask  = StartEventSubscriberAsync(nats, cts.Token);

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // ── Generate cohort ───────────────────────────────────────────────────
        Log.Information("Generating cohort: {Count} students, {Days} simulation days, seed={Seed}...",
            config.StudentCount, config.SimulationDays, config.Seed);

        var cohort = CohortGenerator.Generate(
            config.StudentCount,
            config.SimulationDays,
            config.Seed);

        Log.Information("Cohort ready: {Count} students across {Archetypes} archetypes",
            cohort.Count,
            cohort.Select(m => m.Profile.Archetype).Distinct().Count());

        CohortGenerator.LogDistribution(cohort);

        // ── Run simulation (arrival-scheduled dispatch) ──────────────────────
        Log.Information("Starting emulation at {Speed}x speed with arrival scheduler... (Ctrl+C to stop)",
            config.SpeedMultiplier);
        var startTime = DateTimeOffset.UtcNow;

        // Hard cap: 30% of total students may be active concurrently
        var maxConcurrent = Math.Max(1, (int)(config.StudentCount * 0.30));
        using var limiter  = new ConcurrencyLimiter(maxConcurrent, backpressureThreshold: 50);
        var scheduleModel  = new DailyScheduleModel();
        var simulator      = new SessionSimulator(nats, config.SpeedMultiplier, config.Seed);
        EmulationStats stats;

        try
        {
            stats = await RunScheduledAsync(
                cohort, config, simulator, limiter, scheduleModel, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Emulation cancelled by user.");
            stats = new EmulationStats();
        }

        // ── Wait briefly for trailing events ─────────────────────────────────
        await Task.Delay(Math.Min((int)config.RoundTripTimeout.TotalMilliseconds, 3000));
        cts.Cancel();
        await Task.WhenAny(eventSubTask, Task.Delay(2000));

        // ── Report ────────────────────────────────────────────────────────────
        var elapsed = DateTimeOffset.UtcNow - startTime;
        PrintSummary(stats, elapsed);
        PrintRoundTripReport(stats.TotalAttempts + stats.TotalSessions);
    }

    // ── Arrival-scheduled simulation dispatch ─────────────────────────────────

    /// <summary>
    /// Dispatches student sessions according to a realistic arrival schedule.
    /// Concurrency is gated by <paramref name="limiter"/> (max 30% active).
    /// Each student's attempts are replayed via <paramref name="simulator"/> once
    /// their arrival slot is reached in time-compressed wall-clock terms.
    /// </summary>
    private static async Task<EmulationStats> RunScheduledAsync(
        IReadOnlyList<CohortMember>  cohort,
        EmulatorConfig               config,
        SessionSimulator             simulator,
        ConcurrencyLimiter           limiter,
        DailyScheduleModel           scheduleModel,
        CancellationToken            cancellationToken)
    {
        var stats      = new EmulationStats();
        var rng        = new Random(config.Seed + 9999);
        var simStart   = DateOnly.FromDateTime(DateTime.UtcNow);
        var msPerSimDay = (24.0 * 60.0 * 60.0 * 1000.0) / config.SpeedMultiplier;

        // Build a per-student lookup of their attempt history
        var attemptsByStudent = cohort.ToDictionary(
            m => m.Profile.StudentId,
            m => m.Simulation.AttemptHistory
                    .OrderBy(a => a.Timestamp)
                    .ToList());

        // Work through each simulated day
        for (int day = 0; day < config.SimulationDays; day++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var dayDate   = simStart.AddDays(day);
            var dayKind   = DailyScheduleModel.KindFor(dayDate);

            // Generate arrival offsets (in minutes from start of day) using the schedule model
            var dayArrivals = scheduleModel.GenerateDayArrivals(
                day, simStart, config.StudentCount, rng);

            if (dayArrivals.Count == 0) continue;

            Log.Information("  Day {Day} ({Kind}): {Arrivals} scheduled arrivals, concurrency cap={Cap}",
                day + 1, dayKind, dayArrivals.Count, limiter.MaxConcurrency);

            // Assign arrivals round-robin to students (multiple per student is fine — they queue)
            // Each arrival index maps to a student via modulo so distribution is uniform
            var arrivalTasks = new List<Task>();

            for (int arrIdx = 0; arrIdx < dayArrivals.Count; arrIdx++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var arrivalMinute = dayArrivals[arrIdx];
                var studentIdx    = arrIdx % cohort.Count;
                var member        = cohort[studentIdx];

                // Backpressure: when queue > threshold, slow arrival dispatch
                if (limiter.BackpressureActive)
                    await Task.Delay(200, cancellationToken);

                var capturedMember  = member;
                var capturedMinute  = arrivalMinute;
                var capturedDay     = day;

                var t = Task.Run(async () =>
                {
                    // Time-compress: wait until this arrival's simulated minute
                    var delayMs = (int)(capturedMinute * 60_000.0 / config.SpeedMultiplier);
                    if (delayMs > 0)
                        await Task.Delay(Math.Min(delayMs, 5_000), cancellationToken);

                    await limiter.AcquireAsync(cancellationToken);
                    var sessionStart = DateTimeOffset.UtcNow;
                    try
                    {
                        var studentId = capturedMember.Profile.StudentId;
                        if (!attemptsByStudent.TryGetValue(studentId, out var attempts)
                            || attempts.Count == 0)
                            return;

                        // Slice attempts for this simulated day
                        var dayAttempts = attempts
                            .Skip(capturedDay * (attempts.Count / Math.Max(1, config.SimulationDays)))
                            .Take(Math.Max(1, attempts.Count / Math.Max(1, config.SimulationDays)))
                            .ToList();

                        if (dayAttempts.Count == 0)
                            dayAttempts = attempts.Take(3).ToList();

                        var singleStudentStats = await simulator.RunSingleStudentAsync(
                            capturedMember, dayAttempts, cancellationToken);

                        // Accumulate stats (interlocked for thread safety)
                        Interlocked.Add(ref stats.TotalAttemptsRef,   singleStudentStats.TotalAttempts);
                        Interlocked.Add(ref stats.TotalSessionsRef,   singleStudentStats.TotalSessions);
                        Interlocked.Add(ref stats.TotalAnnotationsRef, singleStudentStats.TotalAnnotations);
                        Interlocked.Add(ref stats.TotalMethodologySwitchesRef, singleStudentStats.TotalMethodologySwitches);
                        Interlocked.Add(ref stats.TotalFocusEventsRef, singleStudentStats.TotalFocusEvents);
                        Interlocked.Add(ref stats.TotalErrorsRef,     singleStudentStats.TotalErrors);
                    }
                    finally
                    {
                        var durationMs = (long)(DateTimeOffset.UtcNow - sessionStart).TotalMilliseconds;
                        limiter.Release(durationMs);
                    }
                }, cancellationToken);

                arrivalTasks.Add(t);

                // Log concurrency metrics every 100 dispatches
                if (arrIdx % 100 == 0)
                {
                    var metrics = limiter.GetMetrics();
                    Log.Information("  Concurrency — active: {Active}/{Cap}  queue: {Q}  peak: {Peak}  backpressure: {BP}",
                        metrics.CurrentActive, limiter.MaxConcurrency,
                        metrics.QueueDepth, metrics.PeakConcurrency,
                        metrics.BackpressureActive);
                }
            }

            // Wait for all of this day's sessions to complete before advancing
            await Task.WhenAll(arrivalTasks);

            // Brief real-time pause between simulated days
            if (day < config.SimulationDays - 1)
                await Task.Delay(50, cancellationToken);
        }

        var finalMetrics = limiter.GetMetrics();
        Log.Information("  Peak concurrency observed: {Peak}/{Cap}  avg session: {Avg:F0}ms",
            finalMetrics.PeakConcurrency, limiter.MaxConcurrency,
            finalMetrics.AvgSessionDurationMs);

        return stats;
    }

    // ── Round-trip event subscriber ───────────────────────────────────────────

    private static async Task StartEventSubscriberAsync(
        NatsConnection nats,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var msg in nats.SubscribeAsync<string>(
                NatsSubjects.AllEvents,
                cancellationToken: cancellationToken))
            {
                Interlocked.Increment(ref _eventsReceived);
                Interlocked.Increment(ref _roundTripMatches);

                if (_eventsReceived % 50 == 0)
                    Log.Information("  <- Received {Count} events from actor host (matches: {Matches})",
                        _eventsReceived, _roundTripMatches);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── CLI argument parsing ──────────────────────────────────────────────────

    private static EmulatorConfig ParseArgs(string[] args)
    {
        static string? GetArg(string[] a, string key)
            => a.FirstOrDefault(x => x.StartsWith($"--{key}"))
                ?.Split('=').LastOrDefault();

        var students  = GetArg(args, "students")  is string s   ? int.Parse(s)    : 1000;
        var speed     = GetArg(args, "speed")     is string sp  ? float.Parse(sp) : 10f;
        var days      = GetArg(args, "duration")  is string d   ? int.Parse(d)    : 60;
        var seed      = GetArg(args, "seed")      is string sd  ? int.Parse(sd)   : 42;
        var natsUrl   = GetArg(args, "nats")                                       ?? "nats://localhost:4222";
        var rtTimeout = GetArg(args, "rt-timeout") is string rt ? int.Parse(rt)   : 5000;

        return new EmulatorConfig(
            StudentCount:      students,
            SpeedMultiplier:   speed,
            SimulationDays:    days,
            Seed:              seed,
            NatsUrl:           natsUrl,
            RoundTripTimeout:  TimeSpan.FromMilliseconds(rtTimeout));
    }

    // ── Console output ────────────────────────────────────────────────────────

    private static void PrintBanner(EmulatorConfig config)
    {
        Log.Information("=====================================================");
        Log.Information("  Cena Student Emulator");
        Log.Information("  Students: {Students} | Speed: {Speed}x | Days: {Days} | Seed: {Seed}",
            config.StudentCount, config.SpeedMultiplier, config.SimulationDays, config.Seed);
        Log.Information("  NATS: {Nats}", config.NatsUrl);
        Log.Information("=====================================================");
    }

    private static void PrintSummary(EmulationStats stats, TimeSpan elapsed)
    {
        var rate = elapsed.TotalSeconds > 0
            ? (int)(stats.TotalAttempts / elapsed.TotalSeconds)
            : 0;

        Log.Information("=====================================================");
        Log.Information("  Emulation Complete");
        Log.Information("  Attempts:  {Attempts}  |  Sessions: {Sessions}",
            stats.TotalAttempts, stats.TotalSessions);
        Log.Information("  Annotations: {A}  |  Switches: {S}  |  Focus: {F}",
            stats.TotalAnnotations, stats.TotalMethodologySwitches, stats.TotalFocusEvents);
        Log.Information("  Errors: {Errors}",  stats.TotalErrors);
        Log.Information("  Duration: {Dur:F1}s  |  Rate: {Rate}/sec",
            elapsed.TotalSeconds, rate);
        Log.Information("=====================================================");
    }

    private static void PrintRoundTripReport(int commandsPublished)
    {
        var matchRate = commandsPublished > 0
            ? _roundTripMatches * 100.0 / commandsPublished
            : 0.0;

        Log.Information("--- Round-Trip Validation ---");
        Log.Information("  Commands published : {Commands}", commandsPublished);
        Log.Information("  Events received    : {Events}",   _eventsReceived);
        Log.Information("  RT matches         : {Matches} ({Rate:F1}%)",
            _roundTripMatches, matchRate);

        if (matchRate < 80.0 && commandsPublished > 0)
            Log.Warning("  WARN: Round-trip match rate below 80% — check actor host connectivity.");
    }
}
