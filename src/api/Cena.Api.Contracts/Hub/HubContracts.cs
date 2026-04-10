// =============================================================================
// Cena Platform -- SignalR Hub Contracts (DB-05)
// Typed interfaces and DTOs for CenaHub client/server communication.
// =============================================================================

namespace Cena.Api.Contracts.Hub;

// ── Server → Client (ICenaClient) ──────────────────────────────────────────

/// <summary>
/// Defines all methods the server can invoke on connected SignalR clients.
/// Each method corresponds to a NATS per-student event type.
/// </summary>
public interface ICenaClient
{
    // Session lifecycle
    Task SessionStarted(SessionStartedEvent evt);
    Task SessionEnded(SessionEndedEvent evt);

    // Learning events
    Task AnswerEvaluated(AnswerEvaluatedEvent evt);
    Task MasteryUpdated(MasteryUpdatedEvent evt);
    Task HintDelivered(HintDeliveredEvent evt);
    Task MethodologySwitched(MethodologySwitchedEvent evt);
    Task StagnationDetected(StagnationDetectedEvent evt);

    // Gamification events
    Task XpAwarded(XpAwardedEvent evt);
    Task StreakUpdated(StreakUpdatedEvent evt);

    // Tutoring events
    Task TutoringStarted(TutoringStartedEvent evt);
    Task TutorMessage(TutorMessageEvent evt);
    Task TutoringEnded(TutoringEndedEvent evt);

    // System
    Task Error(HubErrorEvent evt);
    Task CommandAck(CommandAckEvent evt);
}

// ── Client → Server Commands ───────────────────────────────────────────────

public sealed record StartSessionCommand(
    string SubjectId,
    string? ConceptId,
    DeviceInfo Device);

public sealed record SubmitAnswerCommand(
    string SessionId,
    string ConceptId,
    string QuestionId,
    string QuestionType,
    string Answer,
    int ResponseTimeMs,
    int HintCountUsed,
    bool WasSkipped,
    int BackspaceCount,
    int AnswerChangeCount);

public sealed record EndSessionCommand(
    string SessionId,
    string Reason);

public sealed record RequestHintCommand(
    string SessionId,
    string ConceptId,
    string QuestionId);

public sealed record SkipQuestionCommand(
    string SessionId,
    string ConceptId,
    string QuestionId);

public sealed record AddAnnotationCommand(
    string SessionId,
    string ConceptId,
    string Text,
    string Kind);

public sealed record SwitchApproachCommand(
    string SessionId,
    string FromMethodology,
    string ToMethodology,
    string Reason);

public sealed record RequestNextConceptCommand(
    string SessionId);

public sealed record DeviceInfo(
    string DeviceType,
    string AppVersion);

// ── Server → Client Events ─────────────────────────────────────────────────

public sealed record SessionStartedEvent(
    string SessionId,
    string StudentId,
    string SubjectId,
    string StartingConceptId,
    string ActiveMethodology,
    DateTimeOffset Timestamp);

public sealed record SessionEndedEvent(
    string SessionId,
    string StudentId,
    string Reason,
    int ConceptsAttempted,
    int ConceptsMastered,
    string Duration,
    DateTimeOffset Timestamp);

public sealed record AnswerEvaluatedEvent(
    string SessionId,
    string StudentId,
    string ConceptId,
    string QuestionId,
    bool IsCorrect,
    float PriorMastery,
    float PosteriorMastery,
    int ResponseTimeMs,
    string? ErrorType,
    DateTimeOffset Timestamp);

public sealed record MasteryUpdatedEvent(
    string StudentId,
    string ConceptId,
    float MasteryLevel,
    DateTimeOffset Timestamp);

public sealed record HintDeliveredEvent(
    string SessionId,
    string StudentId,
    string ConceptId,
    string QuestionId,
    string HintText,
    int HintIndex,
    DateTimeOffset Timestamp);

public sealed record MethodologySwitchedEvent(
    string SessionId,
    string StudentId,
    string FromMethodology,
    string ToMethodology,
    string Reason,
    DateTimeOffset Timestamp);

public sealed record StagnationDetectedEvent(
    string StudentId,
    string ConceptId,
    int AttemptCount,
    float CompositeScore,
    DateTimeOffset Timestamp);

public sealed record XpAwardedEvent(
    string StudentId,
    int Amount,
    string Reason,
    int TotalXp,
    DateTimeOffset Timestamp);

public sealed record StreakUpdatedEvent(
    string StudentId,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset Timestamp);

public sealed record TutoringStartedEvent(
    string SessionId,
    string StudentId,
    string ConceptId,
    DateTimeOffset Timestamp);

public sealed record TutorMessageEvent(
    string SessionId,
    string StudentId,
    string Role,
    string Content,
    DateTimeOffset Timestamp);

public sealed record TutoringEndedEvent(
    string SessionId,
    string StudentId,
    string Summary,
    DateTimeOffset Timestamp);

public sealed record HubErrorEvent(
    string CorrelationId,
    string Code,
    string Message,
    DateTimeOffset Timestamp);

public sealed record CommandAckEvent(
    string CorrelationId,
    string CommandType,
    DateTimeOffset Timestamp);

// ── MessageEnvelope for correlation ────────────────────────────────────────

public sealed record MessageEnvelope<T>(
    string CorrelationId,
    string Type,
    DateTimeOffset Timestamp,
    T Payload)
{
    public static MessageEnvelope<T> Create(T payload, string type)
        => new(Guid.NewGuid().ToString("N"), type, DateTimeOffset.UtcNow, payload);
}
