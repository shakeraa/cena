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

        // ── Run simulation ────────────────────────────────────────────────────
        Log.Information("Starting emulation at {Speed}x speed... (Ctrl+C to stop)", config.SpeedMultiplier);
        var startTime = DateTimeOffset.UtcNow;

        var simulator = new SessionSimulator(nats, config.SpeedMultiplier, config.Seed);
        EmulationStats stats;

        try
        {
            stats = await simulator.RunAsync(cohort, cts.Token);
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
