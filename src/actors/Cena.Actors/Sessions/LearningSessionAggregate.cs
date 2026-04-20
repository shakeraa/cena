// =============================================================================
// Cena Platform — LearningSession aggregate root (Phase 1 scaffold)
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Aggregate root for the LearningSession bounded context. Event-stream-backed
// via Marten, keyed by `session-{SessionId}`. Phase 1 scope is scaffolding
// only — the command surface is empty, and the single known event type is
// SessionStarted_V2 (shadow-written alongside the legacy V1 by StudentActor).
//
// Sprint 2+ work will:
//   - Migrate HintRequested, QuestionSkipped, ConceptAttempted, SessionEnded,
//     ExercisePresented, StepAttempted/Verified, MisconceptionDetected/
//     Remediated, SessionMisconceptionsScrubbed, MentorChatMessageSent/Read,
//     ExamSimulationStarted/Submitted, AnnotationAdded into this stream
//     (see ADR-0012 "LearningSession (session lifecycle)" table)
//   - Cut over the write path so StudentActor stops appending V1
//   - Retire SessionStarted_V1 from the StudentActor.Queries.cs switch
// =============================================================================

using Cena.Actors.Sessions.Events;

namespace Cena.Actors.Sessions;

/// <summary>
/// Aggregate root for a single learning session. Stream key:
/// <c>session-{SessionId}</c>. Phase 1 is a minimal scaffold with one event
/// type (<see cref="SessionStarted_V2"/>); Sprint 2+ expands the surface.
/// <para>
/// This type does not hold the Proto.Actor virtual-actor plumbing — that
/// lives in <c>LearningSessionActor</c>, which will eventually consume this
/// aggregate as its state carrier. For now the aggregate is independent
/// state + event application, exercised by the Marten projection path only.
/// </para>
/// </summary>
public sealed class LearningSessionAggregate
{
    /// <summary>Conventional stream-key prefix for this aggregate.</summary>
    public const string StreamKeyPrefix = "session-";

    /// <summary>
    /// Builds the Marten stream key for a session id. Stream identity is
    /// <c>AsString</c> (see <c>MartenConfiguration</c>), so we return a
    /// string-keyed identity.
    /// </summary>
    public static string StreamKey(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException(
                "Session id must be non-empty for stream-key construction.",
                nameof(sessionId));
        return StreamKeyPrefix + sessionId;
    }

    /// <summary>Backing state carried by this aggregate instance.</summary>
    public LearningSessionState State { get; } = new();

    /// <summary>
    /// Applies an inbound domain event. Phase 1 only recognises the stream-
    /// start event; unknown events are silently ignored so the aggregate
    /// tolerates forward migration (Sprint 2+ events will arrive before their
    /// handlers are wired).
    /// </summary>
    public void Apply(object @event)
    {
        switch (@event)
        {
            case SessionStarted_V2 e:
                State.Apply(e);
                break;
            case SessionPlanComputed_V1 p:
                // prr-149 — scheduler plan. Session-scoped only; applying the
                // event refreshes State.CurrentPlan so stream replay rebuilds
                // the same plan an in-flight actor is already holding.
                State.Apply(p);
                break;
            // Phase 2+ handlers land here as each V1 event migrates in.
        }
    }
}
