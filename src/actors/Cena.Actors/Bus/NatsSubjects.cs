// =============================================================================
// Cena Platform -- NATS Subject Definitions
// Centralized subject naming for all NATS pub/sub and request/reply patterns.
// =============================================================================

namespace Cena.Actors.Bus;

/// <summary>
/// NATS subject hierarchy for the Cena platform.
/// Pattern: cena.{domain}.{action}.{entityId}
/// </summary>
public static class NatsSubjects
{
    // ── Commands (Emulator/Client → Actor Host) ──
    public const string SessionStart  = "cena.session.start";     // → StartSession
    public const string SessionEnd    = "cena.session.end";       // → EndSession
    public const string ConceptAttempt = "cena.mastery.attempt";  // → AttemptConcept
    public const string MethodologySwitch = "cena.mastery.switch"; // → SwitchMethodology
    public const string Annotation       = "cena.session.annotate"; // → AddAnnotation

    // ── Events (Actor Host → Subscribers) ──
    public const string EventConceptAttempted  = "cena.events.concept.attempted";
    public const string EventConceptMastered   = "cena.events.concept.mastered";
    public const string EventSessionStarted    = "cena.events.session.started";
    public const string EventSessionEnded      = "cena.events.session.ended";
    public const string EventStagnationDetected = "cena.events.stagnation.detected";
    public const string EventMethodologySwitched = "cena.events.methodology.switched";
    public const string EventFocusAlert        = "cena.events.focus.alert";
    public const string EventFocusUpdated      = "cena.events.focus.updated";
    public const string EventMindWandering     = "cena.events.focus.mind_wandering";
    public const string EventMicrobreakSuggested = "cena.events.focus.microbreak_suggested";

    // ── Wildcard subscriptions ──
    public const string AllEvents   = "cena.events.>";
    public const string AllCommands = "cena.session.>"; // + cena.mastery.>

    // ── Request/Reply (Admin API → Actor Host) ──
    public const string RequestStudentProfile = "cena.request.student.profile";
    public const string RequestStudentMastery = "cena.request.student.mastery";
    public const string RequestClusterHealth  = "cena.request.cluster.health";
    public const string RequestActorStats     = "cena.request.actor.stats";

    /// <summary>
    /// Get per-student subject for targeted events.
    /// </summary>
    public static string StudentEvent(string studentId, string eventType)
        => $"cena.events.student.{studentId}.{eventType}";
}
