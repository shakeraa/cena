// =============================================================================
// Cena Platform — TeacherOverrideAggregate state (prr-150)
//
// Folds TeacherOverride events into a small last-write-wins projection the
// scheduler bridge consumes. Mirrors the "pure fold in the aggregate, policy
// in the command handler" split established by prr-148's StudentPlanState.
//
// Fold semantics:
//   - PinTopicRequested_V1  — keyed by TopicSlug. Latest active pin for a
//     given slug replaces the prior one (re-pinning resets the counter).
//   - BudgetAdjusted_V1     — single-valued, last-write-wins.
//   - MotivationProfileOverridden_V1 — keyed by SessionTypeScope so future
//     scoped overrides (e.g. "only for diagnostic sessions") coexist.
//
// Pins do NOT decrement on event application. The aggregate does not know
// when a session was consumed — that is the bridge's responsibility (the
// scheduler emits a session-consumed signal downstream which the bridge
// translates into a PinConsumed event). Phase-1 ships the counter field on
// the read model so the bridge can snapshot it without a separate query.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Teacher.ScheduleOverride.Events;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Folded state of a single <see cref="TeacherOverrideAggregate"/> instance.
/// Exposes read accessors only; mutation happens through the Apply methods
/// (internal to the aggregate fold).
/// </summary>
public sealed class TeacherOverrideState
{
    private readonly Dictionary<string, ActivePin> _pinsByTopic =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MotivationProfileOverride> _motivationByScope =
        new(StringComparer.Ordinal);

    /// <summary>Active topic pins keyed by <c>TopicSlug</c>.</summary>
    public IReadOnlyDictionary<string, ActivePin> PinsByTopic => _pinsByTopic;

    /// <summary>Active motivation overrides keyed by <c>SessionTypeScope</c>.</summary>
    public IReadOnlyDictionary<string, MotivationProfileOverride> MotivationByScope => _motivationByScope;

    /// <summary>Latest budget override, if any.</summary>
    public BudgetOverride? Budget { get; private set; }

    /// <summary>Wall-clock of the most recent event applied, if any.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Apply a <see cref="PinTopicRequested_V1"/> event.</summary>
    public void Apply(PinTopicRequested_V1 e)
    {
        _pinsByTopic[e.TopicSlug] = new ActivePin(
            TopicSlug: e.TopicSlug,
            RemainingSessions: e.PinnedSessionCount,
            TeacherActorId: e.TeacherActorId,
            InstituteId: e.InstituteId,
            Rationale: e.Rationale,
            SetAt: e.SetAt);
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a <see cref="BudgetAdjusted_V1"/> event.</summary>
    public void Apply(BudgetAdjusted_V1 e)
    {
        Budget = new BudgetOverride(
            WeeklyBudget: e.NewWeeklyBudget,
            TeacherActorId: e.TeacherActorId,
            InstituteId: e.InstituteId,
            Rationale: e.Rationale,
            SetAt: e.SetAt);
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    /// <summary>Apply a <see cref="MotivationProfileOverridden_V1"/> event.</summary>
    public void Apply(MotivationProfileOverridden_V1 e)
    {
        _motivationByScope[e.SessionTypeScope] = new MotivationProfileOverride(
            SessionTypeScope: e.SessionTypeScope,
            Profile: e.OverrideProfile,
            TeacherActorId: e.TeacherActorId,
            InstituteId: e.InstituteId,
            Rationale: e.Rationale,
            SetAt: e.SetAt);
        UpdatedAt = Later(UpdatedAt, e.SetAt);
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset b)
        => a is null || b > a.Value ? b : a;
}

/// <summary>Active teacher pin carrying the remaining session countdown.</summary>
public sealed record ActivePin(
    string TopicSlug,
    int RemainingSessions,
    string TeacherActorId,
    string InstituteId,
    string Rationale,
    DateTimeOffset SetAt);

/// <summary>Active budget override (last-write-wins).</summary>
public sealed record BudgetOverride(
    TimeSpan WeeklyBudget,
    string TeacherActorId,
    string InstituteId,
    string Rationale,
    DateTimeOffset SetAt);

/// <summary>Active motivation-profile override scoped to a session-type.</summary>
public sealed record MotivationProfileOverride(
    string SessionTypeScope,
    MotivationProfile Profile,
    string TeacherActorId,
    string InstituteId,
    string Rationale,
    DateTimeOffset SetAt);
