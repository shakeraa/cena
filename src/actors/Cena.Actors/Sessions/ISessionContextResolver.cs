// =============================================================================
// Cena Platform — ISessionContextResolver (EPIC-PRR-I PRR-310, SLICE 1)
//
// FIRST-SLICE FRAMING
// -------------------
// The resolver is the seam. It composes IStudentEntitlementResolver at
// session start and freezes the tier + caps + features into a
// SessionContext snapshot. Downstream slices plug consumers in against
// this interface:
//
//   Slice 2: NATS envelope enrichment reads SessionContext to stamp tier
//            + caps headers on every actor-bound request.
//   Slice 3: LearningSessionActor / LLM router resolve-once at session
//            start and use the frozen snapshot for the session's life.
//
// This slice ships the interface, the default implementation, and a
// legitimate Null-object fallback (see NullSessionContextResolver).
//
// IMMUTABILITY CONTRACT
// ---------------------
// Implementations MUST return a SessionContext whose tier/caps/features
// are the ones effective AT <paramref name="startedAt"/>. They must NOT
// observe a newer entitlement and apply it retroactively. This is the
// single most important invariant of PRR-310: mid-session upgrades do
// not retroactively change caps under an active student.
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Builds an immutable <see cref="SessionContext"/> for a session at its
/// start. Consumed exactly once per session, held for the session's
/// duration.
/// </summary>
public interface ISessionContextResolver
{
    /// <summary>
    /// Resolve the entitlement-pinned session context for a student at
    /// the exact moment the session starts. Returns a non-null snapshot
    /// even when the student has no active subscription (synthesized
    /// Unsubscribed + zero caps — the hot path always has an object to
    /// enforce against; see <see cref="NullSessionContextResolver"/>).
    /// </summary>
    /// <param name="sessionId">Session identifier (non-empty).</param>
    /// <param name="studentSubjectIdEncrypted">Encrypted student id.</param>
    /// <param name="startedAt">Session start timestamp (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SessionContext> ResolveAtSessionStartAsync(
        string sessionId,
        string studentSubjectIdEncrypted,
        DateTimeOffset startedAt,
        CancellationToken ct);
}
