// =============================================================================
// Cena Platform -- GetSessionSnapshot Messages (PWA-BE-001)
// Actor query for SignalR reconnect session snapshot.
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Query a student's active session for a complete snapshot.
/// Handled by StudentActor and forwarded to LearningSessionActor.
/// </summary>
public sealed record GetSessionSnapshot(string StudentId, string SessionId);

/// <summary>
/// Response from StudentActor containing the session snapshot.
/// </summary>
public sealed record SessionSnapshotResponse(
    string SessionId,
    int CurrentStepNumber,
    string? CurrentQuestionId,
    Dictionary<string, SkillMasteryDto> BktSnapshot,
    string ScaffoldingLevel,
    List<StepResultDto> CompletedSteps,
    DateTimeOffset SessionStartedAt,
    int SessionDurationSeconds,
    string? Error = null);

/// <summary>
/// BKT mastery snapshot for a single skill/concept.
/// </summary>
public sealed record SkillMasteryDto(
    string ConceptId,
    double MasteryProbability,
    int AttemptCount,
    int CorrectCount);

/// <summary>
/// A single completed step in the session.
/// </summary>
public sealed record StepResultDto(
    string StepNumber,
    string QuestionId,
    string ConceptId,
    bool IsCorrect,
    int ResponseTimeMs,
    DateTimeOffset CompletedAt);
