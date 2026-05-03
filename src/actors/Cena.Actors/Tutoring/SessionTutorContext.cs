// =============================================================================
// Cena Platform — Session Tutor Context (prr-204)
//
// Strictly session-scoped context record that the Sidekick drawer + hint
// ladder consumers read from GET /api/v1/sessions/{sid}/tutor-context.
//
// ADR-0003 (misconception session scope): LastMisconceptionTag is session-
// scoped ONLY. It is never persisted to a student profile, never exported
// to GDPR Art. 20 dumps, never joined onto long-term analytics. The whole
// record lives in Redis keyed on the session id with session-TTL and
// rebuilt from event-sourced projections on cache miss.
//
// ADR-0001 (tenant isolation): InstituteId is authoritative — the endpoint
// rejects cross-tenant reads with 403 before the service is even called.
//
// No dark-pattern fields (streaks, loss-aversion counts) — per ship-gate.
// =============================================================================

namespace Cena.Actors.Tutoring;

/// <summary>
/// Session-scoped tutor context. Never persisted outside the session stream
/// or the Redis session-TTL cache. See ADR-0003 for scope boundary.
/// </summary>
/// <param name="SessionId">
/// Stream key for the learning session. The Sidekick drawer and the hint
/// ladder both key their own state on this id.
/// </param>
/// <param name="StudentId">
/// Owner of the session. The endpoint has already authenticated and
/// verified ownership before the service sees this id.
/// </param>
/// <param name="InstituteId">
/// ADR-0001 tenant scope. Null pre-tenancy or for anonymous sessions.
/// The endpoint enforces cross-tenant rejection before delegating to the
/// service; this field is carried for observability and for the pre-seed
/// to include in its key set.
/// </param>
/// <param name="CurrentQuestionId">
/// The question the student is currently looking at, or null when the
/// queue is empty between questions. Drives the Sidekick "explain this
/// step" path.
/// </param>
/// <param name="AnsweredCount">
/// Questions the student has already answered in this session. Session-
/// scoped; resets to zero on the next session.
/// </param>
/// <param name="CorrectCount">
/// Questions answered correctly in this session. Used for pacing hints
/// only. Never surfaced as a loss-aversion counter or a peer-ranked
/// leaderboard number per the ship-gate dark-pattern rules.
/// </param>
/// <param name="CurrentRung">
/// The highest hint-ladder rung (0..3) the student has reached for the
/// current question. Mirrors
/// <c>LearningSessionQueueProjection.LadderRungByQuestion</c> for the
/// active question; resets when the question changes.
/// </param>
/// <param name="LastMisconceptionTag">
/// ADR-0003 session-scoped misconception tag — the most recent buggy-rule
/// id (e.g. "DIST-EXP-SUM") detected in this session, or null when no
/// misconception has been observed. NEVER read from or written to a
/// long-term student store.
/// </param>
/// <param name="AttemptPhase">
/// Pedagogical phase marker for the current question. One of
/// <see cref="SessionTutorContextAttemptPhase"/>.
/// </param>
/// <param name="ElapsedMinutes">
/// Whole minutes since the session started, at the time the context was
/// last rebuilt. Not a timer for the student; used to decide pacing
/// nudges ("you've been here 8 minutes, want to take a breath?").
/// </param>
/// <param name="DailyMinutesRemaining">
/// Minutes left in today's daily tutor-time budget
/// (<c>DailyTutorTimeBudget</c>). Zero or negative means the cap has
/// been reached and the Sidekick should render the take-a-break copy
/// instead of starting a new tutor turn.
/// </param>
/// <param name="BktMasteryBucket">
/// Coarse BKT mastery bucket snapshot at session start ("low" | "mid" |
/// "high" | "unknown"). Coarse on purpose — ADR-0003 allows aggregate
/// learning signals, not fine-grained per-student tracking. Carried here
/// so the pre-seed does not need to re-query Marten on every tutor turn.
/// </param>
/// <param name="AccommodationFlags">
/// Snapshot of accommodation flags relevant to the tutor/hint copy path
/// (LD-anxious friendly template, extended-time multiplier, etc.). Copied
/// at session-start from <c>AccommodationProfile</c> so the tutor does
/// not have to round-trip the accommodations bounded context mid-turn.
/// </param>
/// <param name="BuiltAtUtc">
/// When this snapshot was assembled. Used by the cache-invalidation logic
/// and exposed to observers so a stale pre-seed is visible in metrics.
/// </param>
public sealed record SessionTutorContext(
    string SessionId,
    string StudentId,
    string? InstituteId,
    string? CurrentQuestionId,
    int AnsweredCount,
    int CorrectCount,
    int CurrentRung,
    string? LastMisconceptionTag,
    SessionTutorContextAttemptPhase AttemptPhase,
    int ElapsedMinutes,
    int DailyMinutesRemaining,
    string BktMasteryBucket,
    SessionTutorAccommodationFlags AccommodationFlags,
    DateTimeOffset BuiltAtUtc);

/// <summary>
/// Pedagogical phase marker. The three values correspond to the three
/// Socratic-drawer copy branches — first_try vs retry vs post_solution —
/// so the client does not have to derive the phase from counts.
/// </summary>
public enum SessionTutorContextAttemptPhase
{
    /// <summary>Student has not yet submitted an answer to the current question.</summary>
    FirstTry = 0,

    /// <summary>Student got the current question wrong and is retrying.</summary>
    Retry = 1,

    /// <summary>Student has already submitted a correct answer; Sidekick is now in "reflect" mode.</summary>
    PostSolution = 2,
}

/// <summary>
/// Minimal accommodation-flag bundle mirrored onto the session context so
/// the Sidekick + hint consumers do not need a second round-trip. Defaults
/// match <see cref="Accommodations.AccommodationProfile.Default"/> (no
/// accommodations enabled).
/// </summary>
/// <param name="LdAnxiousFriendly">
/// LD-anxious hint governor opt-in (Cena-native Phase 1B). When true, the
/// Sidekick prefers concrete worked-step copy over terse nudges.
/// </param>
/// <param name="ExtendedTimeMultiplier">
/// Ministry hatama-1 extended-time multiplier (1.0 = no accommodation,
/// 1.5 = 50% extension). Carried so the Sidekick copy can acknowledge
/// "take your time" without re-querying the accommodations service.
/// </param>
/// <param name="DistractionReducedLayout">
/// Ministry hatama-3 distraction-reduced layout opt-in. Drives the Sidekick's
/// "hide peripheral widgets" render choice.
/// </param>
/// <param name="TtsForProblemStatements">
/// Ministry hatama-2 text-to-speech opt-in for problem statements. The
/// Sidekick uses this to decide whether to offer a "read this aloud"
/// affordance.
/// </param>
public sealed record SessionTutorAccommodationFlags(
    bool LdAnxiousFriendly,
    double ExtendedTimeMultiplier,
    bool DistractionReducedLayout,
    bool TtsForProblemStatements)
{
    /// <summary>"No accommodations" baseline used when the profile lookup fails fail-open.</summary>
    public static SessionTutorAccommodationFlags None { get; } =
        new(LdAnxiousFriendly: false,
            ExtendedTimeMultiplier: 1.0,
            DistractionReducedLayout: false,
            TtsForProblemStatements: false);
}
