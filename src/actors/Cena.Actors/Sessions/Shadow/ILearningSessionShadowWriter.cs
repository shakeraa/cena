// =============================================================================
// Cena Platform — LearningSession shadow-write contract (Phase 1)
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Phase 1 migration strategy per ADR-0012:
//   1. Keep existing `student-{id}` stream as primary (legacy SessionStarted_V1)
//   2. On every V1 append, shadow-write a V2 event to `session-{sessionId}`
//   3. Read path unchanged in Phase 1 — only writes are dual
//
// This interface decouples the StudentActor from the shadow-write mechanism so
// that Phase 2 can swap the implementation (to a real-write, not a shadow)
// without touching the call site.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Sessions.Shadow;

/// <summary>
/// Shadow-writes LearningSession-bounded-context events to their own Marten
/// stream whenever the legacy <c>StudentActor</c> appends the corresponding
/// legacy event to the student stream.
/// <para>
/// Phase 1 contract: MUST NOT throw on failure — the student-stream write is
/// the source of truth, and a shadow failure must never break the primary
/// write path. Implementations log + swallow so they observe but do not
/// disrupt.
/// </para>
/// <para>
/// Phase 1 contract: MUST be controllable via
/// <see cref="LearningSessionShadowWriteFeatureFlag.IsEnabled"/>. When the
/// flag is off, the writer SHOULD be a no-op so rollback is instant.
/// </para>
/// </summary>
public interface ILearningSessionShadowWriter
{
    /// <summary>
    /// Appends a <see cref="Cena.Actors.Sessions.Events.SessionStarted_V2"/>
    /// to the new LearningSession stream keyed by
    /// <c>session-{v1Event.SessionId}</c> — only if the feature flag is on.
    /// Must not throw; must not block the caller's command-reply.
    /// </summary>
    /// <param name="v1Event">The legacy <see cref="SessionStarted_V1"/> that
    /// was just appended to the student stream. The V2 payload is derived
    /// from this record.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    Task AppendSessionStartedAsync(
        SessionStarted_V1 v1Event,
        CancellationToken cancellationToken = default);
}
