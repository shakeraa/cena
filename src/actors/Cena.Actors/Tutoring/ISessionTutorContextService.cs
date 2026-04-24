// =============================================================================
// Cena Platform — Session Tutor Context Service (prr-204)
//
// Seam for the Sidekick drawer + hint-ladder consumers to fetch a session-
// scoped tutor context. Two-layer lookup:
//
//   1. Redis session-TTL cache (pre-seeded at session start, invalidated on
//      session end)
//   2. Live Marten fallback — fold the session projections into a context
//      record without touching the student profile
//
// ADR-0003: the service NEVER writes the misconception tag to a non-
// session store. The NoTutorContextPersistenceTest architecture ratchet
// asserts this at test-time.
// =============================================================================

namespace Cena.Actors.Tutoring;

/// <summary>
/// Reads the current tutor context for a session. Session-scoped per ADR-0003 —
/// every lookup is keyed on <c>sessionId</c> and the result is never merged
/// back into a long-lived student profile.
/// </summary>
public interface ISessionTutorContextService
{
    /// <summary>
    /// Returns the current tutor context for the given session. Fast path is
    /// the Redis session-TTL cache (pre-seeded by
    /// <c>SessionTutorContextPreSeedService</c>); miss path folds the
    /// Marten projections into a fresh snapshot. On Redis outage the service
    /// falls back to the Marten path — callers still get a usable context,
    /// just with a round-trip to Postgres instead of a cache hit.
    /// </summary>
    /// <param name="sessionId">The session stream key.</param>
    /// <param name="studentId">
    /// The caller's authenticated student id. MUST match the session's owner
    /// — the endpoint enforces this before calling, and the service re-asserts
    /// it defensively so a bug in the endpoint does not become a cross-student
    /// leak.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The context snapshot, or null when the session does not exist / has
    /// already ended. Null is a 404 at the endpoint layer.
    /// </returns>
    Task<SessionTutorContext?> GetAsync(
        string sessionId,
        string studentId,
        CancellationToken ct = default);

    /// <summary>
    /// Pre-seeds the cache with a freshly-built context. Called on session
    /// start (<c>SessionStarted_V1</c> / <c>LearningSessionStarted_V1</c>)
    /// by the pre-seed hosted service. Asynchronous and best-effort — a
    /// Redis outage during pre-seed does not block session start; the first
    /// <see cref="GetAsync"/> will rebuild from Marten.
    /// </summary>
    Task PreSeedAsync(
        string sessionId,
        string studentId,
        string? instituteId,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cached context for a session. Called on
    /// <c>LearningSessionEnded_V1</c> / <c>SessionCompleted_V2</c> so the
    /// session-scope promise in ADR-0003 is honoured — the tag, the
    /// misconception state, and the counts do not outlive the session.
    /// </summary>
    Task InvalidateAsync(
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// prr-152 — bulk invalidation for the erasure cascade. Removes every
    /// cached tutor-context entry whose embedded <c>StudentId</c> matches
    /// <paramref name="studentId"/>, regardless of session id.
    ///
    /// Because cache entries expire on the session TTL (default 6h), this
    /// is almost always a no-op by the time a 30-day-cooled erasure runs —
    /// but we still invalidate explicitly so a second erasure run after
    /// the TTL still succeeds and so the manifest audit trail records the
    /// intent. Returns the number of entries invalidated (best-effort on
    /// Redis outage — logs a warning and returns 0).
    /// </summary>
    Task<int> InvalidateAllForStudentAsync(
        string studentId,
        CancellationToken ct = default);
}
