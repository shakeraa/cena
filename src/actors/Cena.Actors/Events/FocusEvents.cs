// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus Analytics Domain Events
// Layer: Domain Events | Runtime: .NET 9
// Events for focus degradation, mind wandering, and microbreak tracking.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

/// <summary>
/// Emitted after each question to track the student's focus score over time.
/// FocusLevel: Flow (>=0.8), Engaged (>=0.6), Drifting (>=0.4), Fatigued (>=0.2), Disengaged (&lt;0.2).
/// </summary>
public sealed record FocusScoreUpdated_V1(
    string StudentId,
    string SessionId,
    int QuestionNumber,
    double FocusScore,
    string FocusLevel,
    DateTimeOffset Timestamp) : IDelegatedEvent;

/// <summary>
/// Emitted when mind wandering is detected based on response time patterns.
/// DriftType: AwareDrift, UnawareDrift, Ambiguous.
/// </summary>
public sealed record MindWanderingDetected_V1(
    string StudentId,
    string SessionId,
    string DriftType,
    double Confidence,
    string Context,
    DateTimeOffset Timestamp) : IDelegatedEvent;

/// <summary>
/// Emitted when the system suggests a microbreak to the student.
/// Activity: StretchBreak, BreathingExercise, LookAway, WaterBreak, MiniWalk.
/// </summary>
public sealed record MicrobreakSuggested_V1(
    string StudentId,
    string SessionId,
    int QuestionsSinceBreak,
    double ElapsedMinutes,
    string Activity,
    int DurationSeconds,
    string Reason,
    DateTimeOffset Timestamp) : IDelegatedEvent;

/// <summary>
/// Emitted when a student takes a suggested microbreak.
/// </summary>
public sealed record MicrobreakTaken_V1(
    string StudentId,
    string SessionId,
    string Activity,
    int DurationSeconds,
    DateTimeOffset Timestamp) : IDelegatedEvent;

/// <summary>
/// Emitted when a student skips a suggested microbreak.
/// Reason: too_busy, not_needed, in_flow.
/// </summary>
public sealed record MicrobreakSkipped_V1(
    string StudentId,
    string SessionId,
    string Activity,
    string Reason,
    DateTimeOffset Timestamp) : IDelegatedEvent;
