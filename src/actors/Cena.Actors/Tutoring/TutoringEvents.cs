// =============================================================================
// Cena Platform -- Tutoring Domain Events (SAI-009)
// Append-only versioned events for tutoring conversation lifecycle.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Tutoring;

/// <summary>
/// Emitted when a tutoring conversation begins for a concept.
/// </summary>
public sealed record TutoringSessionStarted_V1(
    string StudentId,
    string SessionId,
    string TutoringSessionId,
    string ConceptId,
    string Subject,
    string Methodology,
    string Language,
    double ConceptMastery,
    int BloomsLevel,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

/// <summary>
/// Emitted for each student/tutor exchange in the conversation.
/// </summary>
public sealed record TutoringMessageSent_V1(
    string StudentId,
    string SessionId,
    string TutoringSessionId,
    int TurnNumber,
    string Role,           // "student" or "tutor"
    string MessagePreview, // First 200 chars, no PII
    int SourceCount,       // Number of RAG sources used
    DateTimeOffset Timestamp
) : IDelegatedEvent;

/// <summary>
/// Emitted when a tutoring conversation ends (max turns, student request, or inactivity timeout).
/// </summary>
public sealed record TutoringSessionEnded_V1(
    string StudentId,
    string SessionId,
    string TutoringSessionId,
    string EndReason,      // "max_turns", "student_request", "inactivity_timeout"
    int TotalTurns,
    int DurationSeconds,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

/// <summary>
/// Summary event emitted when a tutoring episode completes. Contains metadata only --
/// conversation text is NOT persisted (ephemeral). Used for analytics and A/B experiments.
/// </summary>
public sealed record TutoringEpisodeCompleted_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string TriggerType,        // confusion_annotation, question_annotation, confusion_stuck, post_wrong_answer
    string Methodology,
    int TurnCount,
    TimeSpan Duration,
    string? ResolutionStatus,  // resolved, unresolved, student_ended, turn_limit, timeout
    DateTimeOffset Timestamp
) : IDelegatedEvent;
