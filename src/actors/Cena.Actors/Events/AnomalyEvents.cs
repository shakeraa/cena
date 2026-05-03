// =============================================================================
// Cena Platform — Anomaly Detection Events (SEC-ASSESS-003)
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// SEC-ASSESS-003: Emitted when a behavioral anomaly is detected.
/// Informational only — no automated penalties.
/// </summary>
public record AnomalyFlagRaised_V1(
    string StudentId,
    string SessionId,
    string? QuestionId,
    string AnomalyType,
    string Severity,
    string Description,
    IReadOnlyDictionary<string, object> Evidence,
    DateTimeOffset DetectedAt
) : IDelegatedEvent;
