// =============================================================================
// Cena Platform -- Session Simulator
// Runs individual student sessions: start → attempts → annotations → end.
// Publishes all events via NATS. Extracted from the monolithic Program.cs.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Emulator.Population;
using NATS.Client.Core;

namespace Cena.Emulator.Simulation;

/// <summary>
/// Per-student mutable session state tracked during emulation.
/// </summary>
internal sealed class StudentSessionState
{
    public string? SessionId           { get; set; }
    public string  Methodology         { get; set; } = "Socratic";
    public int     ConsecutiveWrong    { get; set; }
    public int     QuestionIndex       { get; set; }
    public DateTimeOffset SessionStart { get; set; }
}

/// <summary>
/// Holds emulation-wide counters updated from the session loop.
/// All fields are accessed from a single thread — no locking needed.
/// </summary>
public sealed class EmulationStats
{
    public int TotalAttempts          { get; set; }
    public int TotalSessions          { get; set; }
    public int TotalAnnotations       { get; set; }
    public int TotalMethodologySwitches { get; set; }
    public int TotalFocusEvents       { get; set; }
    public int TotalErrors            { get; set; }
}

/// <summary>
/// Replays a sorted stream of (student, attempt) pairs over NATS,
/// generating all related events (focus, confusion, methodology switches).
/// </summary>
public sealed class SessionSimulator
{
    // ── Annotation templates (Hebrew + English) ──────────────────────────────

    private static readonly string[] ConfusionTexts =
    {
        "I don't understand this step",
        "Why does the formula work this way?",
        "אני לא מבין למה זה עובד ככה",
        "מה ההבדל בין זה לנוסחה הקודמת?",
        "Can someone explain the connection to the previous topic?",
        "Why do we need to use this approach?",
        "אני מבולבל מהשלב הזה"
    };

    private static readonly string[] QuestionTexts =
    {
        "Is there an easier way to solve this?",
        "Can you show a worked example?",
        "יש דרך יותר פשוטה?",
        "אפשר דוגמה נוספת?",
        "How is this related to the next topic?",
        "What happens if the sign is negative?"
    };

    private static readonly string[] Methodologies =
    {
        "Socratic", "SpacedRepetition", "Feynman",
        "WorkedExample", "DrillAndPractice",
        "BloomsProgression", "RetrievalPractice"
    };

    // ── Initial methodology per archetype ────────────────────────────────────

    private static string InitialMethodology(string archetype) => archetype switch
    {
        "Genius"           => "Socratic",
        "HighAchiever"     => "BloomsProgression",
        "SteadyLearner"    => "Socratic",
        "Struggling"       => "WorkedExample",
        "FastCareless"     => "DrillAndPractice",
        "SlowThorough"     => "Feynman",
        "Inconsistent"     => "SpacedRepetition",
        "VeryLowCognitive" => "WorkedExample",
        _                  => "Socratic"
    };

    // ── Focus degradation: use profile rate if available ─────────────────────

    private static float FocusDegradationRate(string archetype)
        => StudyHabitProfile.All.TryGetValue(archetype, out var p)
            ? p.FocusDegradationRate
            : 0.005f;

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly NatsConnection _nats;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly Random _rng;
    private readonly float _speedMultiplier;

    private readonly Dictionary<string, StudentSessionState> _sessions = new();

    // ── Constructor ──────────────────────────────────────────────────────────

    public SessionSimulator(
        NatsConnection nats,
        float speedMultiplier,
        int seed = 42)
    {
        _nats            = nats;
        _speedMultiplier = speedMultiplier;
        _rng             = new Random(seed);
        _jsonOpts        = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Replay all student attempts in chronological order across the full cohort.
    /// </summary>
    public async Task<EmulationStats> RunAsync(
        IReadOnlyList<CohortMember> cohort,
        CancellationToken cancellationToken)
    {
        var stats = new EmulationStats();

        var allAttempts = cohort
            .SelectMany(m => m.Simulation.AttemptHistory
                .Select(a => (Member: m, Attempt: a)))
            .OrderBy(x => x.Attempt.Timestamp)
            .ToList();

        Log.Information("Replaying {Total} concept attempts across {Students} students...",
            allAttempts.Count, cohort.Count);

        DateTimeOffset? lastTimestamp = null;

        foreach (var (member, attempt) in allAttempts)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Time-compressed gap simulation
            if (lastTimestamp.HasValue)
            {
                var gap     = attempt.Timestamp - lastTimestamp.Value;
                var sleepMs = (int)(gap.TotalMilliseconds / _speedMultiplier);
                if (sleepMs > 0)
                    await Task.Delay(Math.Min(sleepMs, 100), cancellationToken);
            }
            lastTimestamp = attempt.Timestamp;

            try
            {
                await ProcessAttemptAsync(member, attempt, stats, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                stats.TotalErrors++;
                Log.Warning("Error processing attempt for {StudentId}: {Error}",
                    member.Profile.StudentId, ex.Message);
            }
        }

        // End all remaining open sessions
        await CloseAllSessionsAsync(cancellationToken);

        return stats;
    }

    // ── Private: per-attempt logic ────────────────────────────────────────────

    private async Task ProcessAttemptAsync(
        CohortMember member,
        Cena.Actors.Simulation.SimulatedAttempt attempt,
        EmulationStats stats,
        CancellationToken cancellationToken)
    {
        var studentId = member.Profile.StudentId;
        var archetype = member.Profile.Archetype;
        var schoolId  = member.Profile.SchoolId;

        // ── Open session if not active ────────────────────────────────────────
        if (!_sessions.TryGetValue(studentId, out var state))
        {
            state = new StudentSessionState
            {
                SessionId   = $"sess-{Guid.NewGuid():N}"[..16],
                Methodology = InitialMethodology(archetype),
                SessionStart = DateTimeOffset.UtcNow,
            };
            _sessions[studentId] = state;

            var startMsg = BusEnvelope<BusStartSession>.Create(
                NatsSubjects.SessionStart,
                new BusStartSession(
                    studentId,
                    "math",
                    attempt.ConceptId,
                    _rng.NextDouble() > 0.3 ? "mobile" : "desktop",
                    "1.0.0",
                    attempt.Timestamp,
                    SchoolId: schoolId),
                "emulator",
                schoolId: schoolId);

            await PublishAsync(NatsSubjects.SessionStart, startMsg, cancellationToken);
            stats.TotalSessions++;

            // Stagger session starts: 30-70ms jitter to avoid thundering herd
            await Task.Delay(_rng.Next(30, 70), cancellationToken);
        }

        var sessionId = state.SessionId!;

        // ── Concept attempt ───────────────────────────────────────────────────
        var hintCount = SimulateHintUsage(archetype, attempt.IsCorrect);

        var attemptMsg = BusEnvelope<BusConceptAttempt>.Create(
            NatsSubjects.ConceptAttempt,
            new BusConceptAttempt(
                studentId,
                sessionId,
                attempt.ConceptId,
                $"q-{_rng.Next(1, 1500):D4}",
                "multiple_choice",
                attempt.IsCorrect ? "correct" : "wrong",
                attempt.ResponseTimeMs,
                hintCount,
                false,
                _rng.Next(0, 10),
                _rng.Next(0, 3)),
            "emulator");

        await PublishAsync(NatsSubjects.ConceptAttempt, attemptMsg, cancellationToken);
        stats.TotalAttempts++;

        // ── Focus analytics ───────────────────────────────────────────────────
        await PublishFocusEventsAsync(
            member, state, sessionId, stats, cancellationToken);

        state.QuestionIndex++;

        // ── Consecutive wrong tracking ────────────────────────────────────────
        if (!attempt.IsCorrect)
            state.ConsecutiveWrong++;
        else
            state.ConsecutiveWrong = 0;

        // ── SAI: Confusion annotation (3+ wrong in a row) ────────────────────
        if (state.ConsecutiveWrong >= 3 && _rng.NextDouble() < 0.4)
        {
            var text = ConfusionTexts[_rng.Next(ConfusionTexts.Length)];
            await PublishAnnotationAsync(
                studentId, sessionId, attempt.ConceptId, text, "confusion", cancellationToken);
            stats.TotalAnnotations++;
        }

        // ── SAI: Question annotation (curious archetypes, on correct answers) ─
        if (attempt.IsCorrect && _rng.NextDouble() < 0.03 &&
            archetype is "Genius" or "HighAchiever" or "SteadyLearner")
        {
            var text = QuestionTexts[_rng.Next(QuestionTexts.Length)];
            await PublishAnnotationAsync(
                studentId, sessionId, attempt.ConceptId, text, "question", cancellationToken);
            stats.TotalAnnotations++;
        }

        // ── SAI: Methodology switch (5+ wrong in a row) ───────────────────────
        if (state.ConsecutiveWrong >= 5 && _rng.NextDouble() < 0.5)
        {
            var next = Methodologies
                .Where(m => m != state.Methodology)
                .ElementAt(_rng.Next(Methodologies.Length - 1));

            var switchMsg = BusEnvelope<BusMethodologySwitch>.Create(
                NatsSubjects.MethodologySwitch,
                new BusMethodologySwitch(
                    studentId, sessionId,
                    state.Methodology, next,
                    "stagnation_auto_switch"),
                "emulator");

            await PublishAsync(NatsSubjects.MethodologySwitch, switchMsg, cancellationToken);
            stats.TotalMethodologySwitches++;
            state.Methodology     = next;
            state.ConsecutiveWrong = 0;
        }

        // ── Probabilistic session end (~7% chance per attempt ≈ 15 att/sess) ──
        if (_rng.NextDouble() < 0.07)
            await CloseSessionAsync(studentId, "completed", cancellationToken);
    }

    // ── Focus events ─────────────────────────────────────────────────────────

    private async Task PublishFocusEventsAsync(
        CohortMember member,
        StudentSessionState state,
        string sessionId,
        EmulationStats stats,
        CancellationToken cancellationToken)
    {
        var studentId      = member.Profile.StudentId;
        var archetype      = member.Profile.Archetype;
        var minutesActive  = (DateTimeOffset.UtcNow - state.SessionStart).TotalMinutes;
        var degradation    = FocusDegradationRate(archetype);
        var baseFocus      = Math.Max(0.3, 1.0 - minutesActive * degradation * 60.0);
        var focusScore     = Math.Clamp(
            baseFocus + (_rng.NextDouble() - 0.5) * 0.1, 0.0, 1.0);
        var focusLevel     = focusScore >= 0.8 ? "Flow"
                           : focusScore >= 0.6 ? "Engaged"
                           : focusScore >= 0.4 ? "Drifting"
                           : "Fatigued";

        await _nats.PublishAsync(NatsSubjects.EventFocusUpdated,
            JsonSerializer.Serialize(new
            {
                studentId,
                sessionId,
                questionNumber = state.QuestionIndex,
                focusScore     = Math.Round(focusScore, 3),
                focusLevel,
                timestamp      = DateTimeOffset.UtcNow
            }, _jsonOpts),
            cancellationToken: cancellationToken);
        stats.TotalFocusEvents++;

        // Mind wandering detection every 7 questions
        if (state.QuestionIndex % 7 == 0 && focusScore < 0.55 && _rng.NextDouble() < 0.6)
        {
            var driftType = focusScore < 0.35 ? "UnawareDrift" : "AwareDrift";
            await _nats.PublishAsync(NatsSubjects.EventMindWandering,
                JsonSerializer.Serialize(new
                {
                    studentId,
                    sessionId,
                    driftType,
                    confidence = Math.Round(0.6 + _rng.NextDouble() * 0.3, 3),
                    context    = focusScore < 0.4 ? "sustained_slow_rt" : "erratic_pattern",
                    timestamp  = DateTimeOffset.UtcNow
                }, _jsonOpts),
                cancellationToken: cancellationToken);
            stats.TotalFocusEvents++;
        }

        // Microbreak suggestion every 8 questions
        if (state.QuestionIndex > 0 && state.QuestionIndex % 8 == 0)
        {
            var activities = new[] { "StretchBreak", "BreathingExercise", "LookAway", "WaterBreak", "MiniWalk" };
            var activity   = activities[_rng.Next(activities.Length)];
            var duration   = focusScore < 0.5 ? 90 : 60;

            await _nats.PublishAsync(NatsSubjects.EventMicrobreakSuggested,
                JsonSerializer.Serialize(new
                {
                    studentId,
                    sessionId,
                    questionsSinceBreak = 8,
                    elapsedMinutes      = Math.Round(minutesActive, 1),
                    activity,
                    durationSeconds     = duration,
                    reason              = $"Reached {state.QuestionIndex} questions",
                    taken               = _rng.NextDouble() < 0.68,
                    timestamp           = DateTimeOffset.UtcNow
                }, _jsonOpts),
                cancellationToken: cancellationToken);
            stats.TotalFocusEvents++;
        }
    }

    // ── Session lifecycle helpers ─────────────────────────────────────────────

    private async Task CloseSessionAsync(
        string studentId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(studentId, out var state)) return;

        var endMsg = BusEnvelope<BusEndSession>.Create(
            NatsSubjects.SessionEnd,
            new BusEndSession(studentId, state.SessionId!, reason),
            "emulator");

        await PublishAsync(NatsSubjects.SessionEnd, endMsg, cancellationToken);
        _sessions.Remove(studentId);
    }

    private async Task CloseAllSessionsAsync(CancellationToken cancellationToken)
    {
        foreach (var studentId in _sessions.Keys.ToList())
        {
            try
            {
                await CloseSessionAsync(studentId, "emulator_shutdown", cancellationToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── Low-level publish ─────────────────────────────────────────────────────

    private async Task PublishAsync<T>(
        string subject,
        BusEnvelope<T> envelope,
        CancellationToken cancellationToken)
    {
        await _nats.PublishAsync(subject,
            JsonSerializer.Serialize(envelope, _jsonOpts),
            cancellationToken: cancellationToken);
    }

    private async Task PublishAnnotationAsync(
        string studentId,
        string sessionId,
        string conceptId,
        string text,
        string kind,
        CancellationToken cancellationToken)
    {
        var msg = BusEnvelope<BusAddAnnotation>.Create(
            NatsSubjects.Annotation,
            new BusAddAnnotation(studentId, sessionId, conceptId, text, kind),
            "emulator");

        await PublishAsync(NatsSubjects.Annotation, msg, cancellationToken);
    }

    // ── Hint simulation ───────────────────────────────────────────────────────

    private int SimulateHintUsage(string archetype, bool isCorrect) => archetype switch
    {
        "Struggling"       => isCorrect ? _rng.Next(0, 2) : _rng.Next(1, 3),
        "VeryLowCognitive" => _rng.Next(1, 3),
        "SlowThorough"     => _rng.Next(0, 2),
        _                  => _rng.NextDouble() < 0.1 ? 1 : 0
    };
}
