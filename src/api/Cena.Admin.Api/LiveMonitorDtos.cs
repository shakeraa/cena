// =============================================================================
// Cena Platform -- Live Monitor DTOs
// ADM-026: SSE event types and active session snapshot for live monitor page
// =============================================================================

namespace Cena.Admin.Api;

// ── SSE Event Envelope ──

/// <summary>
/// Envelope sent over the SSE stream. The <c>event</c> field maps to the SSE "event:" line.
/// </summary>
public sealed record LiveSessionEvent(
    string Id,          // monotonic counter as string, becomes SSE "id:"
    string Event,       // SSE "event:" type, e.g. "question.attempted"
    string StudentId,
    DateTimeOffset Timestamp,
    string PayloadJson  // raw JSON specific to the event type
);

// ── Active Session Snapshot (initial state on connect) ──

/// <summary>
/// Full snapshot of an active session sent as the first event on SSE connect
/// (event type: "session.snapshot").
/// </summary>
public sealed record ActiveSessionSnapshot(
    string SessionId,
    string StudentId,
    string StudentName,
    string Subject,
    string ConceptId,
    string Methodology,
    int QuestionCount,
    int CorrectCount,
    double FatigueScore,   // 0–1; drives card border colour
    int DurationSeconds,
    DateTimeOffset StartedAt
);

// ── Convenience response for REST snapshot endpoint ──

public sealed record ActiveSessionsResponse(
    IReadOnlyList<ActiveSessionSnapshot> Sessions,
    int TotalActive
);
