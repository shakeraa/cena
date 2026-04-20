// =============================================================================
// Cena Platform — LearningSession aggregate state (Phase 1 scaffold)
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Minimal state type for the LearningSession aggregate. In Phase 1 the only
// event applied here is SessionStarted_V2, matching the single event type
// shadow-written in this phase. Additional event handlers land in Sprint 2+
// as each V1 type migrates in.
//
// DISCIPLINE: this file stays tiny on purpose. The whole point of the
// aggregate decomposition is to keep successor-aggregate state types well
// under the 500-LOC rule, so each Sprint 2+ event migration adds at most
// a handful of lines here.
// =============================================================================

using Cena.Actors.Sessions.Events;

namespace Cena.Actors.Sessions;

/// <summary>
/// In-memory state for one LearningSession aggregate instance. Stream key
/// is <c>session-{SessionId}</c>. Event application is intentionally minimal
/// in Phase 1 — only <see cref="SessionStarted_V2"/> is recognised. The
/// <c>StudentActor</c> retains the authoritative session-state shape until
/// Phase 2 (cutover); this state is used only to prove the stream and
/// projection wiring work end-to-end.
/// </summary>
public sealed class LearningSessionState
{
    /// <summary>Session stream key.</summary>
    public string SessionId { get; private set; } = "";

    /// <summary>Student that owns the session.</summary>
    public string StudentId { get; private set; } = "";

    /// <summary>Wall-clock start time as reported by the client.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Starting methodology (Socratic, Direct, etc.).</summary>
    public string Methodology { get; private set; } = "";

    /// <summary>Tenant scope (REV-014). Null pre-tenancy.</summary>
    public string? SchoolId { get; private set; }

    /// <summary>
    /// True once the aggregate has observed a <see cref="SessionStarted_V2"/>.
    /// Used as the "stream initialised" marker — future command handlers
    /// guard against operating on an uninitialised stream.
    /// </summary>
    public bool IsStarted { get; private set; }

    /// <summary>Applies the stream-start event to state.</summary>
    public void Apply(SessionStarted_V2 e)
    {
        SessionId = e.SessionId;
        StudentId = e.StudentId;
        StartedAt = e.ClientTimestamp;
        Methodology = e.Methodology;
        SchoolId = e.SchoolId;
        IsStarted = true;
    }

    // ── prr-149: session-scoped scheduler plan ──
    //
    // These fields are session-local and die when the session stream is
    // archived. They must NEVER be copied into StudentState or
    // StudentProfileSnapshot — the SessionScopedSnapshotTest archtest
    // enforces this contract.

    /// <summary>
    /// The most-recent scheduler plan computed for this session. Null
    /// until <see cref="SessionPlanComputed_V1"/> is applied.
    /// Rebuilt on stream replay so a restarted actor sees the same plan.
    /// </summary>
    public SessionPlanComputed_V1? CurrentPlan { get; private set; }

    /// <summary>Applies a scheduler plan event to the session state.</summary>
    public void Apply(SessionPlanComputed_V1 e)
    {
        CurrentPlan = e;
    }
}
