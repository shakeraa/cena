// =============================================================================
// Cena Platform -- NATS Bus Message Envelopes
// Serializable message wrappers for NATS pub/sub transport.
// =============================================================================

using System.Text.Json.Serialization;
using Cena.Infrastructure.Compliance;

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
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SubjectId,
    string? ConceptId,
    string DeviceType,
    string AppVersion,
    DateTimeOffset ClientTimestamp,
    string? SchoolId = null); // REV-014: tenant context

public sealed record BusEndSession(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId,
    string Reason);  // "completed", "timeout", "user_exit"

public sealed record BusConceptAttempt(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
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
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId,
    string FromMethodology,
    string ToMethodology,
    string Reason);

public sealed record BusAddAnnotation(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId,
    string ConceptId,
    string Text,
    string Kind);   // "note", "question", "confusion", "insight"

public sealed record BusResumeSession(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId);

// ── Account Lifecycle (LCM-001) ──

public sealed record BusAccountStatusChanged(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string NewStatus,      // "suspended", "active", "locked", "frozen", "pending_delete", "expired", "grace"
    string? Reason,
    string ChangedBy,      // UID of admin/parent/system who triggered the change
    DateTimeOffset ChangedAt);

// ── Event payloads (published on cena.events.*) ──

public sealed record BusConceptAttemptedEvent(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId,
    string ConceptId,
    bool IsCorrect,
    float PriorMastery,
    float PosteriorMastery,
    int ResponseTimeMs,
    string? ErrorType,
    DateTimeOffset Timestamp);

public sealed record BusConceptMasteredEvent(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string ConceptId,
    float MasteryLevel,
    DateTimeOffset Timestamp);

public sealed record BusSessionStartedEvent(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId,
    string SubjectId,
    string StartingConceptId,
    string ActiveMethodology,
    DateTimeOffset Timestamp);

public sealed record BusSessionEndedEvent(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
    string SessionId,
    string Reason,
    int ConceptsAttempted,
    int ConceptsMastered,
    TimeSpan Duration,
    DateTimeOffset Timestamp);

public sealed record BusStagnationDetectedEvent(
    [property: Pii(PiiLevel.Low, "identity")] string StudentId,
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
