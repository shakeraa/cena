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
    public const string SessionResume = "cena.session.resume";    // → ResumeSession
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

    // ── Per-Student Events (Actor Host → SignalR Bridge, SES-001) ──
    // Pattern: cena.events.student.{studentId}.{event_type}
    // Used by NatsSignalRBridge to push real-time updates to browser clients.
    public const string StudentSessionStarted     = "session_started";
    public const string StudentSessionEnded        = "session_ended";
    public const string StudentAnswerEvaluated     = "answer_evaluated";
    public const string StudentMasteryUpdated      = "mastery_updated";
    public const string StudentHintDelivered       = "hint_delivered";
    public const string StudentXpAwarded           = "xp_awarded";
    public const string StudentStreakUpdated        = "streak_updated";
    public const string StudentBadgeEarned         = "badge_earned";
    public const string StudentStagnationDetected  = "stagnation_detected";
    public const string StudentMethodologySwitched = "methodology_switched";
    public const string StudentTutoringStarted     = "tutoring_started";
    public const string StudentTutorMessage        = "tutor_message";
    public const string StudentTutoringEnded       = "tutoring_ended";
    /// <summary>
    /// TASK-E2E-A-01-BE-02: per-student onboarding event-type token. Combined via
    /// <see cref="StudentEvent(string, string)"/> yields
    /// <c>cena.events.student.{uid}.onboarded</c>.
    /// </summary>
    public const string StudentOnboarded           = "onboarded";

    /// <summary>
    /// TASK-E2E-A-01-BE-02: wildcard subscription matching every onboarded event
    /// across every student. Used by E2E flow tests
    /// (`tests/e2e-flow/fixtures/bus-probe.ts`) and by downstream consumers
    /// (admin analytics, parent-notification fanout) that don't know the uid in
    /// advance.
    /// </summary>
    public const string AllStudentOnboardedEvents  = "cena.events.student.*.onboarded";

    /// <summary>
    /// Wildcard subscription for all events of a specific student.
    /// </summary>
    public static string AllStudentEvents(string studentId)
        => $"cena.events.student.{studentId}.>";

    /// <summary>
    /// Wildcard subscription for all per-student events across all students.
    /// Used by admin SSE bridge (ADM-026).
    /// </summary>
    public const string AllPerStudentEvents = "cena.events.student.>";

    // ── Account Lifecycle (LCM-001: Admin API → Actor Host) ──
    public const string AccountStatusChanged = "cena.account.status_changed";

    // ── Actor Pre-warm (Admin API → Actor Host) ──
    public const string WarmUpRequest = "cena.actors.warmup";

    // ── Dead-letter (messages that exhausted retries → DEAD_LETTER JetStream stream) ──
    public const string DeadLetter = "cena.durable.dlq.commands";

    // ── Wildcard subscriptions ──
    public const string AllEvents   = "cena.events.>";
    public const string AllCommands = "cena.session.>"; // + cena.mastery.>

    // ── Request/Reply (Admin API → Actor Host) ──
    public const string RequestStudentProfile = "cena.request.student.profile";
    public const string RequestStudentMastery = "cena.request.student.mastery";
    public const string RequestClusterHealth  = "cena.request.cluster.health";
    public const string RequestActorStats     = "cena.request.actor.stats";

    // ── Session Snapshot (SignalR Hub → Actor Host) ──
    public const string SessionSnapshotRequest = "cena.request.session.snapshot";

    /// <summary>
    /// Get per-student subject for targeted events.
    /// </summary>
    public static string StudentEvent(string studentId, string eventType)
        => $"cena.events.student.{studentId}.{eventType}";

    /// <summary>
    /// Wildcard subscription for a specific event type across all students.
    /// Pattern: cena.events.student.*.{eventType}
    /// NATS <c>*</c> matches exactly one token, so this matches every student
    /// but only the named event type. Used by server-side consumers that care
    /// about one event kind (e.g. NotificationDispatcher for xp_awarded).
    /// </summary>
    public static string StudentEventTypeWildcard(string eventType)
        => $"cena.events.student.*.{eventType}";

    /// <summary>
    /// Position (0-indexed) of the student identifier token in a subject
    /// produced by <see cref="StudentEvent(string, string)"/>.
    /// Pattern: cena.events.student.{studentId}.{eventType}
    ///          0    1      2       3           4
    /// </summary>
    public const int StudentEventSubjectStudentIdIndex = 3;

    /// <summary>
    /// Position (0-indexed) of the event-type token in a subject produced by
    /// <see cref="StudentEvent(string, string)"/>.
    /// </summary>
    public const int StudentEventSubjectEventTypeIndex = 4;

    /// <summary>
    /// Extract the studentId from a per-student subject published by
    /// <see cref="StudentEvent(string, string)"/>. Returns <c>null</c> if the
    /// subject does not match the expected pattern — callers MUST treat
    /// subject as the source of truth for studentId, never the payload.
    /// </summary>
    public static string? TryParseStudentIdFromSubject(string subject)
    {
        if (string.IsNullOrEmpty(subject)) return null;
        var parts = subject.Split('.');
        if (parts.Length <= StudentEventSubjectStudentIdIndex) return null;
        if (parts[0] != "cena" || parts[1] != "events" || parts[2] != "student") return null;
        var studentId = parts[StudentEventSubjectStudentIdIndex];
        return string.IsNullOrWhiteSpace(studentId) ? null : studentId;
    }

    // ── Durable outbox subjects (JetStream, published by NatsOutboxPublisher) ──
    // Pattern: cena.durable.{category}.{EventTypeName}
    // The NATS JetStream streams subscribe to cena.durable.{category}.> wildcards,
    // so each event lands in the correct stream for replay and durability.
    // The subject for a given event type MUST match NatsOutboxPublisher.GetDurableSubject
    // character-for-character, otherwise subscribers never see the event. Centralise
    // the mapping here so subscribers and the outbox publisher cannot drift.

    /// <summary>
    /// Durable subject prefix for question-bank events emitted by the outbox.
    /// Used by <see cref="DurableCurriculumEvent(string)"/> and
    /// <see cref="AllDurableCurriculumEvents"/>.
    /// </summary>
    public const string DurableCurriculumPrefix = "cena.durable.curriculum";

    /// <summary>
    /// Wildcard subscription for all durable curriculum events.
    /// Pattern: cena.durable.curriculum.>
    /// </summary>
    public const string AllDurableCurriculumEvents = "cena.durable.curriculum.>";

    /// <summary>
    /// Get the durable outbox subject for a curriculum event (e.g. Question*_V1).
    /// This mirrors the <c>cena.durable.curriculum.*</c> branch of
    /// <c>NatsOutboxPublisher.GetDurableSubject</c>. Keep the two in sync: a drift
    /// here silently drops downstream consumers (ExplanationCacheInvalidator etc.).
    /// </summary>
    public static string DurableCurriculumEvent(string eventTypeName)
        => $"{DurableCurriculumPrefix}.{eventTypeName}";
}
