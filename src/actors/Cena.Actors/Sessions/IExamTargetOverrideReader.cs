// =============================================================================
// Cena Platform — IExamTargetOverrideReader (prr-226, ADR-0050 §10)
//
// Small seam the scheduler's ActiveExamTargetPolicy consults to find out
// whether the student (or another authorised actor) has explicitly asked
// THIS session to run against a non-default target — the event side of
// that request is ExamTargetOverrideApplied_V1 from prr-218, which is
// appended to the `studentplan-{studentAnonId}` stream with the sessionId
// carried on the event.
//
// The default in-memory implementation always returns null (no override).
// Production hosts wire a reader that queries the most recent
// ExamTargetOverrideApplied_V1 for (studentAnonId, sessionId) from the
// event store. Keeping the reader behind an interface means the scheduler
// does not depend on Marten / the aggregate store; it stays replaceable
// and test-friendly.
//
// The interface is intentionally pull-based per session, not push-based
// per event — the scheduler runs once at session start and needs a single
// answer, not a stream subscription.
// =============================================================================

using Cena.Actors.StudentPlan;

namespace Cena.Actors.Sessions;

/// <summary>
/// Resolves the student's explicit target override for a single session.
/// </summary>
public interface IExamTargetOverrideReader
{
    /// <summary>
    /// Return the explicitly-chosen target for this session, or null when
    /// no override has been applied. Implementations MUST be idempotent —
    /// calling twice for the same (student, session) returns the same
    /// result without side effects.
    /// </summary>
    Task<ExamTargetId?> GetOverrideAsync(
        string studentAnonId,
        string sessionId,
        CancellationToken ct = default);
}

/// <summary>
/// Default no-op reader. Every lookup returns null (no override). Safe to
/// register by default; the override path is opt-in, so absence of a real
/// reader preserves the deadline-proximity behaviour.
/// </summary>
public sealed class NullExamTargetOverrideReader : IExamTargetOverrideReader
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullExamTargetOverrideReader Instance = new();

    private NullExamTargetOverrideReader() { }

    /// <inheritdoc />
    public Task<ExamTargetId?> GetOverrideAsync(
        string studentAnonId,
        string sessionId,
        CancellationToken ct = default)
        => Task.FromResult<ExamTargetId?>(null);
}
