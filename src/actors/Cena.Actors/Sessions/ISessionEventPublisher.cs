// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Session Event Publisher Interface
// Layer: Domain Interface | Runtime: .NET 9
// Publishes session and tutoring events to per-student NATS subjects
// for real-time SignalR bridge consumption.
// ═══════════════════════════════════════════════════════════════════════

using Cena.Actors.Events;
using Cena.Actors.Tutoring;

namespace Cena.Actors.Sessions;

/// <summary>
/// Publishes session/tutoring domain events to per-student NATS subjects.
/// Enables the SignalR bridge (SES-001) to push real-time updates to browsers.
/// Implementations should NOT throw on failure — Marten is the source of truth.
/// </summary>
public interface ISessionEventPublisher
{
    Task PublishSessionStartedAsync(string studentId, SessionStarted_V1 evt);
    Task PublishSessionEndedAsync(string studentId, SessionEnded_V1 evt);
    Task PublishConceptAttemptedAsync(string studentId, ConceptAttempted_V1 evt);
    Task PublishMasteryUpdatedAsync(string studentId, ConceptMastered_V1 evt);
    Task PublishHintDeliveredAsync(string studentId, HintRequested_V1 evt);
    Task PublishXpAwardedAsync(string studentId, XpAwarded_V1 evt);
    Task PublishStreakUpdatedAsync(string studentId, StreakUpdated_V1 evt);
    Task PublishStagnationDetectedAsync(string studentId, StagnationDetected_V1 evt);
    Task PublishMethodologySwitchedAsync(string studentId, MethodologySwitched_V1 evt);
    Task PublishTutoringStartedAsync(string studentId, TutoringSessionStarted_V1 evt);
    Task PublishTutoringMessageAsync(string studentId, TutoringMessageSent_V1 evt);
    Task PublishTutoringEndedAsync(string studentId, TutoringSessionEnded_V1 evt);
}
