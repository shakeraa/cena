// =============================================================================
// Cena Platform -- Session Lifecycle Manager (EMU-002.3)
// Manages the per-student session lifecycle:
//   Arrive → Study → Focus degrade → Break or leave → Depart
//
// Session duration is drawn from the archetype's study habit profile with
// Gaussian noise. Students may return later in the same day (1-3 sessions/day)
// after a cooldown period of 30-120 minutes.
// =============================================================================

using Cena.Emulator.Population;

namespace Cena.Emulator.Scheduler;

/// <summary>
/// The current lifecycle phase of a student session.
/// </summary>
public enum SessionPhase
{
    /// <summary>Student has arrived and is actively studying.</summary>
    Active,
    /// <summary>Student is on a short microbreak.</summary>
    OnMicrobreak,
    /// <summary>Student focus has degraded; approaching end of session.</summary>
    FocusDegraded,
    /// <summary>Session has ended; student is in cooldown before next session.</summary>
    Cooldown,
    /// <summary>Student is done for the day — no more sessions today.</summary>
    DoneForDay,
}

/// <summary>
/// Represents a single simulated student session with full lifecycle metadata.
/// </summary>
public sealed class StudentSession
{
    public string       StudentId       { get; init; } = string.Empty;
    public string       Archetype       { get; init; } = string.Empty;
    public SessionPhase Phase           { get; set; }  = SessionPhase.Active;
    public TimeSpan     StartOffset     { get; init; }
    public TimeSpan     PlannedDuration { get; init; }
    public TimeSpan     EndOffset       => StartOffset + PlannedDuration;
    public bool         TookMicrobreak  { get; set; }

    /// <summary>Offset at which this student's current cooldown ends (eligible for next session).</summary>
    public TimeSpan CooldownEndsAt { get; set; }

    /// <summary>Number of sessions completed today by this student.</summary>
    public int SessionsToday { get; set; }
}

/// <summary>
/// Manages session lifecycle decisions: duration sampling, focus degradation,
/// microbreak probability, and cooldown scheduling.
/// </summary>
public sealed class SessionLifecycleManager
{
    // Microbreak probability: 20% chance per session when focus is degraded
    private const double MicrobreakProbability = 0.20;

    // Cooldown between sessions: 30–120 minutes simulated time
    private static readonly TimeSpan CooldownMin = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CooldownMax = TimeSpan.FromMinutes(120);

    // Focus degradation threshold: session ends when focus < 30% of original
    private const double FocusDegradationThreshold = 0.30;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sample a concrete session duration for a student, applying Gaussian
    /// noise (±30%) around the archetype's mean session length.
    /// Clamps to the archetype's [min * 0.5, max * 1.5] range.
    /// </summary>
    public static TimeSpan GenerateSessionDuration(StudyHabitProfile profile, Random rng)
    {
        var mean    = (profile.MinSessionMinutes + profile.MaxSessionMinutes) / 2.0;
        // Box-Muller Gaussian sample
        var u1      = 1.0 - rng.NextDouble();
        var u2      = 1.0 - rng.NextDouble();
        var normal  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        var minutes = mean + normal * mean * 0.30;
        minutes     = Math.Clamp(
            minutes,
            profile.MinSessionMinutes * 0.5,
            profile.MaxSessionMinutes * 1.5);

        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// Determine whether a student should take a microbreak or end their
    /// session when focus has degraded below the threshold.
    /// </summary>
    /// <returns>True if the student takes a microbreak and continues; false if they leave.</returns>
    public static bool ShouldTakeMicrobreak(Random rng)
        => rng.NextDouble() < MicrobreakProbability;

    /// <summary>
    /// Compute the focus score at a given elapsed time within a session.
    /// Applies the archetype's focus degradation rate.
    /// Returns a value in [0, 1] where 1 = fully focused.
    /// </summary>
    public static double ComputeFocusScore(
        StudyHabitProfile profile,
        TimeSpan           elapsed,
        float              varianceFactor,
        Random             rng)
    {
        var minutesActive = elapsed.TotalMinutes;
        var base_ = Math.Max(0.0, 1.0 - minutesActive * profile.FocusDegradationRate * 60.0);

        // Add per-student variance jitter (±5%)
        var jitter = (rng.NextDouble() - 0.5) * 0.10 * varianceFactor;
        return Math.Clamp(base_ + jitter, 0.0, 1.0);
    }

    /// <summary>
    /// Returns true when the focus score has fallen below the degradation threshold,
    /// meaning the student is approaching the end of their productive session.
    /// </summary>
    public static bool IsFocusDegraded(double focusScore)
        => focusScore < FocusDegradationThreshold;

    /// <summary>
    /// Schedule the next session for a student who has just completed a session,
    /// if they have not yet reached their maximum sessions for the day.
    /// Returns null if the student is done for the day.
    /// </summary>
    /// <param name="student">The student profile.</param>
    /// <param name="sessionsCompletedToday">Sessions already completed today.</param>
    /// <param name="currentOffset">The simulation offset at which the current session ends.</param>
    /// <param name="dayEndOffset">The offset at which the current day ends.</param>
    /// <param name="rng">Random source.</param>
    /// <returns>The start offset for the next session, or null if done for the day.</returns>
    public static TimeSpan? ScheduleNextSession(
        StudentProfile student,
        int            sessionsCompletedToday,
        TimeSpan       currentOffset,
        TimeSpan       dayEndOffset,
        Random         rng)
    {
        var profile = student.HabitProfile;
        var maxSessionsToday = rng.Next(
            profile.MinSessionsPerDay,
            profile.MaxSessionsPerDay + 1);

        if (sessionsCompletedToday >= maxSessionsToday)
            return null;

        // Cooldown: 30–120 minutes between sessions
        var cooldownMinutes = CooldownMin.TotalMinutes
            + rng.NextDouble() * (CooldownMax - CooldownMin).TotalMinutes;

        var nextStart = currentOffset + TimeSpan.FromMinutes(cooldownMinutes);

        // Ensure the next session fits before the day ends
        if (nextStart >= dayEndOffset)
            return null;

        return nextStart;
    }

    /// <summary>
    /// Build the full day schedule for a single student: start offsets for each
    /// session they will have today, based on their archetype profile.
    /// </summary>
    /// <param name="student">The student profile.</param>
    /// <param name="dayStartOffset">Simulation offset when today begins.</param>
    /// <param name="dayArrivalsMinutes">
    ///     Pre-generated minute offsets from the daily schedule model for this student's
    ///     first arrival today (from the global arrival curve).
    /// </param>
    /// <param name="rng">Random source.</param>
    /// <returns>Ordered list of (startOffset, plannedDuration) pairs for the day.</returns>
    public static IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> BuildDaySchedule(
        StudentProfile student,
        TimeSpan       dayStartOffset,
        double         firstArrivalMinute,
        Random         rng)
    {
        var profile  = student.HabitProfile;
        var schedule = new List<(TimeSpan, TimeSpan)>();

        var maxSessions  = rng.Next(profile.MinSessionsPerDay, profile.MaxSessionsPerDay + 1);
        if (maxSessions == 0) return schedule;

        // First session: driven by the global arrival curve
        var firstStart   = dayStartOffset + TimeSpan.FromMinutes(firstArrivalMinute);
        var firstDuration = GenerateSessionDuration(profile, rng);
        schedule.Add((firstStart, firstDuration));

        var dayEnd = dayStartOffset + TimeSpan.FromHours(24);

        // Subsequent sessions: after a cooldown
        for (int s = 1; s < maxSessions; s++)
        {
            var prev = schedule[s - 1];
            var prevEnd = prev.Item1 + prev.Item2;

            var next = ScheduleNextSession(student, s, prevEnd, dayEnd, rng);
            if (next is null) break;

            // Add small peak-hour bias for subsequent sessions
            var peakHour  = profile.PeakHours[rng.Next(profile.PeakHours.Length)];
            var peakBias  = TimeSpan.FromMinutes((rng.NextDouble() - 0.5) * 60.0);
            var biasedStart = dayStartOffset + TimeSpan.FromHours(peakHour) + peakBias;

            // Choose whichever is later: cooldown end or peak-hour bias
            var sessionStart  = next.Value > biasedStart ? next.Value : biasedStart;
            if (sessionStart >= dayEnd) break;

            var duration = GenerateSessionDuration(profile, rng);
            schedule.Add((sessionStart, duration));
        }

        return schedule;
    }
}
