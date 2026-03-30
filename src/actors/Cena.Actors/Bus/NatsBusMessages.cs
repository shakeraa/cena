// =============================================================================
// Cena Platform -- NATS Bus Message Envelopes
// Serializable message wrappers for NATS pub/sub transport.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Actors.Bus;

/// <summary>
/// Envelope wrapping all NATS messages with metadata.
/// </summary>
public sealed record BusEnvelope<T>(
    string MessageId,
    string Subject,
    DateTimeOffset Timestamp,
    string Source,   // "emulator", "actor-host", "admin-api"
    string? SchoolId, // REV-014: tenant context -- school the message originates from
    T Payload)
{
    public static BusEnvelope<T> Create(string subject, T payload, string source, string? schoolId = null)
        => new(Guid.NewGuid().ToString("N"), subject, DateTimeOffset.UtcNow, source, schoolId, payload);
}

// ── Command payloads (sent on cena.session.* and cena.mastery.*) ──

public sealed record BusStartSession(
    string StudentId,
    string SubjectId,
    string? ConceptId,
    string DeviceType,
    string AppVersion,
    DateTimeOffset ClientTimestamp,
    string? SchoolId = null); // REV-014: tenant context

public sealed record BusEndSession(
    string StudentId,
    string SessionId,
    string Reason);  // "completed", "timeout", "user_exit"

public sealed record BusConceptAttempt(
    string StudentId,
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

public sealed record BusMethodologySwitch(
    string StudentId,
    string SessionId,
    string FromMethodology,
    string ToMethodology,
    string Reason);

public sealed record BusAddAnnotation(
    string StudentId,
    string SessionId,
    string ConceptId,
    string Text,
    string Kind);   // "note", "question", "confusion", "insight"

public sealed record BusResumeSession(
    string StudentId,
    string SessionId);

// ── Account Lifecycle (LCM-001) ──

public sealed record BusAccountStatusChanged(
    string StudentId,
    string NewStatus,      // "suspended", "active", "locked", "frozen", "pending_delete", "expired", "grace"
    string? Reason,
    string ChangedBy,      // UID of admin/parent/system who triggered the change
    DateTimeOffset ChangedAt);

// ── Event payloads (published on cena.events.*) ──

public sealed record BusConceptAttemptedEvent(
    string StudentId,
    string SessionId,
    string ConceptId,
    bool IsCorrect,
    float PriorMastery,
    float PosteriorMastery,
    int ResponseTimeMs,
    string? ErrorType,
    DateTimeOffset Timestamp);

public sealed record BusConceptMasteredEvent(
    string StudentId,
    string ConceptId,
    float MasteryLevel,
    DateTimeOffset Timestamp);

public sealed record BusSessionStartedEvent(
    string StudentId,
    string SessionId,
    string SubjectId,
    string StartingConceptId,
    string ActiveMethodology,
    DateTimeOffset Timestamp);

public sealed record BusSessionEndedEvent(
    string StudentId,
    string SessionId,
    string Reason,
    int ConceptsAttempted,
    int ConceptsMastered,
    TimeSpan Duration,
    DateTimeOffset Timestamp);

public sealed record BusStagnationDetectedEvent(
    string StudentId,
    string ConceptId,
    int AttemptCount,
    float CompositeScore,
    DateTimeOffset Timestamp);

public sealed record BusActorStatsResponse(
    int ActiveStudentActors,
    int TotalMessagesProcessed,
    float AvgProcessingTimeMs,
    IReadOnlyList<BusActorKindStats> ByKind,
    DateTimeOffset Timestamp);

public sealed record BusActorKindStats(
    string Kind,
    int Count,
    string Status);
