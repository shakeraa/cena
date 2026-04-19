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
    public DateTimeOffset SessionStart    { get; set; }
    /// <summary>How many more questions remain in the current confusion episode (0 = not confused).</summary>
    public int     ConfusionQuestionsLeft { get; set; }
    /// <summary>True once the student has abandoned the session; prevents further event publishing.</summary>
    public bool    DroppedOut             { get; set; }
}

/// <summary>
/// Holds emulation-wide counters. Supports both single-threaded (property set)
/// and multi-threaded (Interlocked on the backing ref fields) access patterns.
/// </summary>
public sealed class EmulationStats
{
    // Backing fields exposed for Interlocked operations in concurrent contexts
    internal int TotalAttemptsRef;
    internal int TotalSessionsRef;
    internal int TotalAnnotationsRef;
    internal int TotalMethodologySwitchesRef;
    internal int TotalFocusEventsRef;
    internal int TotalErrorsRef;

    public int TotalAttempts           { get => TotalAttemptsRef;           set => TotalAttemptsRef = value; }
    public int TotalSessions           { get => TotalSessionsRef;           set => TotalSessionsRef = value; }
    public int TotalAnnotations        { get => TotalAnnotationsRef;        set => TotalAnnotationsRef = value; }
    public int TotalMethodologySwitches{ get => TotalMethodologySwitchesRef;set => TotalMethodologySwitchesRef = value; }
    public int TotalFocusEvents        { get => TotalFocusEventsRef;        set => TotalFocusEventsRef = value; }
    public int TotalErrors             { get => TotalErrorsRef;             set => TotalErrorsRef = value; }
    public int TotalDropouts          { get; set; }
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

    // ── Confusion threshold: consecutive wrong answers before confusion fires ─

    private static int ConfusionThreshold(string archetype) => archetype switch
    {
        "Genius"           => 5,
        "HighAchiever"     => 4,
        "Struggling"       => 2,
        "VeryLowCognitive" => 2,
        _                  => 3
    };

    // ── Dropout profile: (abandonRate, minQuestionsBeforeEligible) ───────────

    private static (double Rate, int MinQuestion) DropoutProfile(string archetype) => archetype switch
    {
        "Struggling"       => (0.15, 5),
        "VeryLowCognitive" => (0.25, 3),
        "Genius"           => (0.02, 8),  // boredom dropout
        "Inconsistent"     => (0.10, 4),
        _                  => (0.0,  int.MaxValue)
    };

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

            // Skip attempts for students who already abandoned their session
            if (_sessions.TryGetValue(member.Profile.StudentId, out var dropState) && dropState.DroppedOut)
                continue;

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
                // Pass the exception as the first arg so Serilog emits the
                // stack trace — without it "Index was outside the bounds of
                // the array" swallowed the actual source site and masked
                // the bug for weeks.
                Log.Warning(ex, "Error processing attempt for {StudentId}",
                    member.Profile.StudentId);
            }
        }

        // End all remaining open sessions
        await CloseAllSessionsAsync(cancellationToken);

        return stats;
    }

    /// <summary>
    /// Replay a specific student's attempt slice in chronological order.
    /// Used by the arrival scheduler to process one student session at a time.
    /// Returns a stats object for this student's contribution.
    /// </summary>
    public async Task<EmulationStats> RunSingleStudentAsync(
        CohortMember  member,
        IReadOnlyList<Cena.Actors.Simulation.SimulatedAttempt> attempts,
        CancellationToken cancellationToken)
    {
        var stats = new EmulationStats();

        DateTimeOffset? lastTimestamp = null;

        foreach (var attempt in attempts)
        {
            if (cancellationToken.IsCancellationRequested) break;

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
                // Pass the exception as the first arg so Serilog emits the
                // stack trace — without it "Index was outside the bounds of
                // the array" swallowed the actual source site and masked
                // the bug for weeks.
                Log.Warning(ex, "Error processing attempt for {StudentId}",
                    member.Profile.StudentId);
            }
        }

        // Close this student's session when their attempt slice ends
        await CloseSessionAsync(member.Profile.StudentId, "session_complete", cancellationToken);

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
        // Difficulty proxy: lower prior mastery means the concept is harder for this student.
        var difficultyProxy   = Math.Clamp(1.0f - attempt.PriorMastery, 0.0f, 1.0f);
        var responseTimeMs    = SimulateResponseTimeMs(archetype, difficultyProxy, state.QuestionIndex);
        var backspaceCount    = SimulateBackspaceCount(archetype);
        var answerChangeCount = SimulateAnswerChangeCount(archetype);
        var hintCount         = SimulateHintUsage(archetype, attempt.IsCorrect);

        var attemptMsg = BusEnvelope<BusConceptAttempt>.Create(
            NatsSubjects.ConceptAttempt,
            new BusConceptAttempt(
                studentId,
                sessionId,
                attempt.ConceptId,
                $"q-{_rng.Next(1, 1500):D4}",
                "multiple_choice",
                attempt.IsCorrect ? "correct" : "wrong",
                responseTimeMs,
                hintCount,
                false,
                backspaceCount,
                answerChangeCount),
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

        // ── SAI: Confusion annotation (archetype-specific threshold + episode tracking) ─
        await ProcessConfusionAsync(
            studentId, sessionId, archetype, attempt, state, stats, cancellationToken);

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

        // ── Session dropout simulation (archetype-specific abandonment) ─────────
        if (await TryDropoutAsync(studentId, archetype, state, stats, cancellationToken))
            return;

        // ── Probabilistic session end (~7% chance per attempt ≈ 15 att/sess) ──
        if (_rng.NextDouble() < 0.07)
            await CloseSessionAsync(studentId, "completed", cancellationToken);
    }

    // ── Confusion escalation ─────────────────────────────────────────────────

    private async Task ProcessConfusionAsync(
        string studentId,
        string sessionId,
        string archetype,
        Cena.Actors.Simulation.SimulatedAttempt attempt,
        StudentSessionState state,
        EmulationStats stats,
        CancellationToken cancellationToken)
    {
        // Decrement confusion duration if still inside an episode — no new annotation
        if (state.ConfusionQuestionsLeft > 0)
        {
            state.ConfusionQuestionsLeft--;
            return;
        }

        if (attempt.IsCorrect) return;
        if (state.ConsecutiveWrong < ConfusionThreshold(archetype)) return;

        // Struggling/VeryLowCognitive emit confusion annotations more often
        var confusionProb = archetype is "Struggling" or "VeryLowCognitive" ? 0.4 : 0.25;
        if (_rng.NextDouble() >= confusionProb) return;

        var text = ConfusionTexts[_rng.Next(ConfusionTexts.Length)];
        await PublishAnnotationAsync(
            studentId, sessionId, attempt.ConceptId, text, "confusion", cancellationToken);
        stats.TotalAnnotations++;

        // 30% chance of a paired question annotation during confusion
        if (_rng.NextDouble() < 0.30)
        {
            var qtext = QuestionTexts[_rng.Next(QuestionTexts.Length)];
            await PublishAnnotationAsync(
                studentId, sessionId, attempt.ConceptId, qtext, "question", cancellationToken);
            stats.TotalAnnotations++;
        }

        // Set confusion duration: 1-3 more questions before the episode resolves
        state.ConfusionQuestionsLeft = _rng.Next(1, 4);
    }

    // ── Dropout simulation ────────────────────────────────────────────────────

    /// <summary>Returns true if the student abandoned the session this turn.</summary>
    private async Task<bool> TryDropoutAsync(
        string studentId,
        string archetype,
        StudentSessionState state,
        EmulationStats stats,
        CancellationToken cancellationToken)
    {
        var (rate, minQuestion) = DropoutProfile(archetype);
        if (rate <= 0.0 || state.QuestionIndex < minQuestion) return false;
        if (_rng.NextDouble() >= rate) return false;

        state.DroppedOut = true;
        await CloseSessionAsync(studentId, "abandoned", cancellationToken);
        stats.TotalDropouts++;
        Log.Information("Student {StudentId} ({Archetype}) abandoned session at question {Q}",
            studentId, archetype, state.QuestionIndex);
        return true;
    }

    // ── Realistic response time simulation ───────────────────────────────────

    /// <summary>
    /// Computes a realistic response time in milliseconds.
    /// difficulty is in [0.0, 1.0] where 1.0 is hardest (derived from inverse prior mastery).
    /// Base: 5s + difficulty * 20s, then modulated by archetype speed and fatigue.
    /// </summary>
    private int SimulateResponseTimeMs(string archetype, float difficulty, int questionIndex)
    {
        var baseMs = 5_000.0 + difficulty * 20_000.0;

        double speedFactor = archetype switch
        {
            "FastCareless"     => 0.5,
            "SlowThorough"     => 2.0,
            "VeryLowCognitive" => 1.8,
            "Genius"           => 0.7,
            _                  => 1.0
        };

        // Each question adds 2% to response time (fatigue accumulation)
        var fatigueFactor = 1.0 + questionIndex * 0.02;

        // ±15% random jitter (total spread: ±30%)
        var jitter = 1.0 + (_rng.NextDouble() - 0.5) * 0.30;

        return (int)Math.Clamp(baseMs * speedFactor * fatigueFactor * jitter, 500, 120_000);
    }

    // ── Backspace count simulation ────────────────────────────────────────────

    private int SimulateBackspaceCount(string archetype) => archetype switch
    {
        "FastCareless"     => _rng.Next(3, 9),   // impulsive: 3-8 backspaces
        "SlowThorough"     => _rng.Next(0, 2),   // deliberate: 0-1
        "Genius"           => _rng.Next(0, 3),
        "Struggling"       => _rng.Next(1, 6),
        "VeryLowCognitive" => _rng.Next(2, 7),
        "Inconsistent"     => _rng.Next(0, 8),   // high variance
        _                  => _rng.Next(0, 4)
    };

    // ── Answer-change count simulation ───────────────────────────────────────

    private int SimulateAnswerChangeCount(string archetype) => archetype switch
    {
        "Inconsistent" => _rng.Next(1, 4),           // 1-3 changes
        "FastCareless"  => _rng.Next(0, 3),
        "SlowThorough"  => 0,                        // commits to first answer
        "Genius"        => _rng.NextDouble() < 0.05 ? 1 : 0,
        _              => _rng.NextDouble() < 0.15 ? 1 : 0
    };

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

    /// <summary>
    /// Simulates hint requests per archetype and outcome.
    /// Struggling: 60% of wrong answers trigger at least 1 hint.
    /// HighAchiever: only 10% of wrong answers use a hint.
    /// SlowThorough: always at least 1 hint before answering wrong.
    /// </summary>
    private int SimulateHintUsage(string archetype, bool isCorrect) => archetype switch
    {
        "Struggling"       => isCorrect ? _rng.Next(0, 2)
                                        : (_rng.NextDouble() < 0.60 ? _rng.Next(1, 3) : 0),
        "VeryLowCognitive" => _rng.Next(1, 4),
        "SlowThorough"     => isCorrect ? _rng.Next(0, 2) : _rng.Next(1, 3),
        "HighAchiever"     => isCorrect ? 0 : (_rng.NextDouble() < 0.10 ? 1 : 0),
        "Genius"           => _rng.NextDouble() < 0.05 ? 1 : 0,
        _                  => _rng.NextDouble() < 0.10 ? 1 : 0
    };
}
