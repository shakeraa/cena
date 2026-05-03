// =============================================================================
// Cena Platform — SessionPlanDocument (prr-149, read model)
//
// Marten read document for the current session plan. One document per
// session, keyed by `session-plan-{SessionId}`. Purely a cache of the
// latest SessionPlanComputed_V1 event — the event stream is the source
// of truth.
//
// SESSION-SCOPED. NEVER projected from a student-keyed stream. Archived
// together with the session.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Sessions.Events;

namespace Cena.Actors.Sessions;

/// <summary>
/// Flat, JSON-friendly read doc for the session plan. Marten's default
/// identity is <see cref="Id"/>; we compose it as
/// <c>session-plan-{SessionId}</c> so a session cannot collide with
/// another session's plan even if ids share a prefix by accident.
/// </summary>
public sealed class SessionPlanDocument
{
    /// <summary>Document id: <c>session-plan-{SessionId}</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Session stream id (without the prefix).</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Anon student id this plan was generated for.</summary>
    public string StudentAnonId { get; set; } = "";

    /// <summary>Wall-clock the scheduler ran.</summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>Motivation profile used — drives rendering.</summary>
    public MotivationProfile MotivationProfile { get; set; }

    /// <summary>Deadline the scheduler planned against, if supplied.</summary>
    public DateTimeOffset? DeadlineUtc { get; set; }

    /// <summary>Weekly minute budget used as input.</summary>
    public int WeeklyBudgetMinutes { get; set; }

    /// <summary>
    /// "student-plan-config" (prr-148 active) or "default-fallback"
    /// (scheduler ran against defaults).
    /// </summary>
    public string InputsSource { get; set; } = "";

    /// <summary>Priority-ordered topics with rationale.</summary>
    public List<SessionPlanTopicEntry_V1> Topics { get; set; } = new();

    /// <summary>
    /// Compose the Marten doc id for a given session. Separate from
    /// the LearningSessionAggregate stream key so the two surfaces do
    /// not collide in Marten's document/event namespace.
    /// </summary>
    public static string DocumentId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException(
                "Session id must be non-empty.", nameof(sessionId));
        return "session-plan-" + sessionId;
    }

    /// <summary>
    /// Build a read doc from the authoritative event. Keeps the doc +
    /// event in sync — any field added to one must be added to the other.
    /// </summary>
    public static SessionPlanDocument FromEvent(SessionPlanComputed_V1 e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new SessionPlanDocument
        {
            Id = DocumentId(e.SessionId),
            SessionId = e.SessionId,
            StudentAnonId = e.StudentAnonId,
            GeneratedAtUtc = e.GeneratedAtUtc,
            MotivationProfile = e.MotivationProfile,
            DeadlineUtc = e.DeadlineUtc,
            WeeklyBudgetMinutes = e.WeeklyBudgetMinutes,
            InputsSource = e.InputsSource,
            Topics = e.Topics.ToList(),
        };
    }
}
