// =============================================================================
// Cena Platform — StudentPlanState (prr-218, supersedes prr-148)
//
// Event-sourced in-memory fold for the multi-target StudentPlan aggregate.
// Re-built on every aggregate load by replaying the entire stream
// `studentplan-{studentAnonId}`; fold is deterministic and order-preserving.
//
// Responsibilities (what this file does):
//   - Apply each event type to evolve the aggregate state.
//   - Maintain the full target list (active + archived) for invariant
//     checks + audit replay.
//   - Track the initialization timestamp per ADR-0050 §1.
//
// Non-responsibilities (what belongs elsewhere):
//   - Invariant validation: StudentPlanCommandHandler.Add/Update/Archive
//     enforce §5 invariants BEFORE emitting events. The state fold
//     accepts any well-formed event.
//   - DI / persistence: IStudentPlanAggregateStore owns I/O.
//   - Legacy single-target projection: StudentPlanInputsService projects
//     this state down to the prr-148 StudentPlanConfig VO for the
//     Sessions bridge (prr-149).
//
// Legacy events (ExamDateSet_V1 / WeeklyTimeBudgetSet_V1) from prr-148
// are still applied for backward compatibility — they update the legacy
// scalar fields so the InputsService projection continues to work during
// the prr-219 migration. Once the migration is complete, those events
// remain on the stream but the aggregate reads only the multi-target
// events for its canonical state.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Folded state of a single StudentPlan stream. Re-built on every load.
/// </summary>
public sealed class StudentPlanState
{
    // Multi-target world (prr-218 / ADR-0050).
    private readonly List<ExamTarget> _targets = new();

    /// <summary>All targets (active + archived) in insertion order.</summary>
    public IReadOnlyList<ExamTarget> Targets => _targets;

    /// <summary>Only active (non-archived) targets.</summary>
    public IReadOnlyList<ExamTarget> ActiveTargets
        => _targets.Where(t => t.IsActive).ToList();

    /// <summary>When the stream was first written to (null before any event).</summary>
    public DateTimeOffset? InitializedAt { get; private set; }

    /// <summary>Wall-clock of the most recent state-mutating event.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    // Legacy (prr-148) scalar fields — preserved for the InputsService
    // projection so the Sessions scheduler bridge keeps working during
    // the prr-219 migration rollout. Populated ONLY by the legacy events;
    // new writes route through the ExamTarget events and the projection
    // derives these values from the first active target.

    /// <summary>Legacy-event-sourced deadline (prr-148). Null when no
    /// legacy event has landed.</summary>
    public DateTimeOffset? LegacyDeadlineUtc { get; private set; }

    /// <summary>Legacy-event-sourced weekly budget (prr-148). Null when
    /// no legacy event has landed.</summary>
    public TimeSpan? LegacyWeeklyBudget { get; private set; }

    // ── Back-compat shims (prr-148) ──────────────────────────────────────
    // Renamed from (DeadlineUtc, WeeklyBudget) → (LegacyDeadlineUtc,
    // LegacyWeeklyBudget). Old callers that still reference the shorter
    // names go through these thin proxies. New code should read from
    // <see cref="ActiveTargets"/> + catalog canonical dates (see
    // <see cref="StudentPlanInputsService"/>).

    /// <summary>DEPRECATED (prr-148): use ActiveTargets + catalog.</summary>
    [Obsolete("Use ActiveTargets + catalog canonical dates. Retained only for legacy scheduler bridge.")]
    public DateTimeOffset? DeadlineUtc => LegacyDeadlineUtc;

    /// <summary>DEPRECATED (prr-148): use ActiveTargets + catalog.</summary>
    [Obsolete("Use ActiveTargets.Sum(WeeklyHours). Retained only for legacy scheduler bridge.")]
    public TimeSpan? WeeklyBudget => LegacyWeeklyBudget;

    // ── Event fold (multi-target, prr-218) ───────────────────────────────

    /// <summary>Apply the stream-initialization event.</summary>
    public void Apply(StudentPlanInitialized_V1 e)
    {
        InitializedAt ??= e.InitializedAt;
        UpdatedAt = Later(UpdatedAt, e.InitializedAt);
    }

    /// <summary>Apply a target-added event.</summary>
    public void Apply(ExamTargetAdded_V1 e)
    {
        _targets.Add(e.Target);
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a target-updated event. Silently ignored if the
    /// target does not exist or has been archived (defensive — command
    /// handler enforces, but replay of a malformed stream should not
    /// throw).</summary>
    public void Apply(ExamTargetUpdated_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        var current = _targets[index];
        _targets[index] = current with
        {
            Track = e.Track,
            Sitting = e.Sitting,
            WeeklyHours = e.WeeklyHours,
            ReasonTag = e.ReasonTag,
        };
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a target-archived event. Idempotent.</summary>
    public void Apply(ExamTargetArchived_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        _targets[index] = _targets[index] with { ArchivedAt = e.ArchivedAt };
        UpdatedAt = Later(UpdatedAt, e.ArchivedAt);
    }

    /// <summary>Apply a target-completed event. Terminates the target
    /// just like archival — completion is a specialised archive reason.</summary>
    public void Apply(ExamTargetCompleted_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        _targets[index] = _targets[index] with { ArchivedAt = e.CompletedAt };
        UpdatedAt = Later(UpdatedAt, e.CompletedAt);
    }

    /// <summary>Apply an override-applied event — telemetry only, no
    /// state change. Included so replay doesn't warn on unknown events.</summary>
    public void Apply(ExamTargetOverrideApplied_V1 _)
    {
        // Pure telemetry — no aggregate state touched. The event exists
        // on the stream so downstream projections can count overrides.
    }

    /// <summary>Apply a question-paper-added event (PRR-243). Idempotent
    /// for the "already present" case (silently no-ops so replay of a
    /// malformed stream does not fail). Archived targets do not accept
    /// new papers.</summary>
    public void Apply(QuestionPaperAdded_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        var current = _targets[index];
        if (current.QuestionPaperCodes.Contains(e.PaperCode)) return;

        var newPapers = new List<string>(current.QuestionPaperCodes) { e.PaperCode };

        IReadOnlyDictionary<string, SittingCode>? newOverride = current.PerPaperSittingOverride;
        if (e.SittingOverride is { } ov)
        {
            var map = current.PerPaperSittingOverride is null
                ? new Dictionary<string, SittingCode>(StringComparer.Ordinal)
                : new Dictionary<string, SittingCode>(current.PerPaperSittingOverride, StringComparer.Ordinal);
            map[e.PaperCode] = ov;
            newOverride = map;
        }

        _targets[index] = current with
        {
            QuestionPaperCodes = newPapers,
            PerPaperSittingOverride = newOverride,
        };
        UpdatedAt = Later(UpdatedAt, e.AddedAt);
    }

    /// <summary>Apply a question-paper-removed event (PRR-243). Clears any
    /// matching per-paper sitting override as a side effect. Silently
    /// ignored for archived or unknown targets, and for unknown paper
    /// codes.</summary>
    public void Apply(QuestionPaperRemoved_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        var current = _targets[index];
        if (!current.QuestionPaperCodes.Contains(e.PaperCode)) return;

        var newPapers = current.QuestionPaperCodes.Where(c => c != e.PaperCode).ToList();

        IReadOnlyDictionary<string, SittingCode>? newOverride = current.PerPaperSittingOverride;
        if (current.PerPaperSittingOverride is not null
            && current.PerPaperSittingOverride.ContainsKey(e.PaperCode))
        {
            var map = new Dictionary<string, SittingCode>(
                current.PerPaperSittingOverride, StringComparer.Ordinal);
            map.Remove(e.PaperCode);
            newOverride = map.Count == 0 ? null : map;
        }

        _targets[index] = current with
        {
            QuestionPaperCodes = newPapers,
            PerPaperSittingOverride = newOverride,
        };
        UpdatedAt = Later(UpdatedAt, e.RemovedAt);
    }

    /// <summary>Apply a per-paper sitting override set event (PRR-243).
    /// Overwrites any existing entry for the same paper code.</summary>
    public void Apply(PerPaperSittingOverrideSet_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        var current = _targets[index];
        if (!current.QuestionPaperCodes.Contains(e.PaperCode)) return;

        var map = current.PerPaperSittingOverride is null
            ? new Dictionary<string, SittingCode>(StringComparer.Ordinal)
            : new Dictionary<string, SittingCode>(current.PerPaperSittingOverride, StringComparer.Ordinal);
        map[e.PaperCode] = e.Sitting;

        _targets[index] = current with { PerPaperSittingOverride = map };
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a per-paper sitting override cleared event
    /// (PRR-243). Idempotent on missing keys.</summary>
    public void Apply(PerPaperSittingOverrideCleared_V1 e)
    {
        var index = _targets.FindIndex(t => t.Id == e.TargetId);
        if (index < 0 || !_targets[index].IsActive) return;

        var current = _targets[index];
        if (current.PerPaperSittingOverride is null
            || !current.PerPaperSittingOverride.ContainsKey(e.PaperCode))
        {
            return;
        }

        var map = new Dictionary<string, SittingCode>(
            current.PerPaperSittingOverride, StringComparer.Ordinal);
        map.Remove(e.PaperCode);
        IReadOnlyDictionary<string, SittingCode>? newOverride = map.Count == 0 ? null : map;

        _targets[index] = current with { PerPaperSittingOverride = newOverride };
        UpdatedAt = Later(UpdatedAt, e.ClearedAt);
    }

    /// <summary>Apply a migration failure. Pure telemetry.</summary>
    public void Apply(StudentPlanMigrationFailed_V1 _)
    {
        // Pure telemetry — the DLQ worker reads these directly; aggregate
        // invariants are unaffected.
    }

    /// <summary>Apply a migration-success event. Records the "last
    /// updated" timestamp — the preceding ExamTargetAdded_V1 already
    /// materialised the target.</summary>
    public void Apply(StudentPlanMigrated_V1 e)
    {
        UpdatedAt = Later(UpdatedAt, e.MigratedAt);
    }

    // ── Legacy fold (prr-148 events on existing streams) ──────────────────

    /// <summary>Apply a legacy single-target ExamDateSet event.</summary>
    public void Apply(ExamDateSet_V1 e)
    {
        LegacyDeadlineUtc = e.DeadlineUtc;
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a legacy single-target WeeklyTimeBudgetSet event.</summary>
    public void Apply(WeeklyTimeBudgetSet_V1 e)
    {
        LegacyWeeklyBudget = e.WeeklyBudget;
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset b)
        => a is null || b > a.Value ? b : a;
}
