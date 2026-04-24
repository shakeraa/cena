// =============================================================================
// Cena Platform — IOverrideAwareSchedulerInputsBridge (prr-150)
//
// The scheduler-facing seam that applies active teacher overrides on top
// of whatever SchedulerInputs the session plan generator built from the
// student's own StudentPlan (prr-148) + onboarding motivation profile.
//
// Precedence (ADR-0044):
//     override > student-set plan input > scheduler default
//
// The bridge is a pure function over the aggregate state: it does NOT
// re-verify the tenant invariant, because the command handler already
// refused to persist any cross-institute event. Downstream code can
// therefore trust that every override on the stream is tenant-valid.
//
// Pin handling: the bridge does not mutate the aggregate. It reads the
// RemainingSessions counter and, if > 0, guarantees the pinned topic is
// present in the scheduler's PerTopicEstimates input with a high-priority
// sentinel estimate. Decrementing happens on a SEPARATE signal emitted by
// the scheduler runtime when a session actually consumes the pin — Phase 1
// snapshots the counter only; the decrement path is a Phase 2 follow-up.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Applies active teacher overrides on top of a base
/// <see cref="SchedulerInputs"/>. Returns the effective inputs the
/// scheduler should use for this session.
/// </summary>
public interface IOverrideAwareSchedulerInputsBridge
{
    /// <summary>
    /// Merge any active overrides for <paramref name="studentAnonId"/> on
    /// top of <paramref name="baseInputs"/>. When no overrides exist the
    /// base inputs are returned unchanged (reference-equal is not
    /// guaranteed — callers must treat <see cref="SchedulerInputs"/> as
    /// immutable).
    /// </summary>
    /// <param name="baseInputs">Inputs built from the student's own plan.</param>
    /// <param name="sessionTypeScope">Session-type the scheduler is about
    /// to run (e.g. <c>"diagnostic"</c>, <c>"drill"</c>). The bridge uses
    /// this to select the applicable <see cref="MotivationProfileOverride"/>
    /// entry — scope "all" always matches when a more specific scope is
    /// absent.</param>
    /// <param name="ct">Caller cancellation.</param>
    Task<OverrideApplicationResult> ApplyAsync(
        SchedulerInputs baseInputs,
        string sessionTypeScope,
        CancellationToken ct = default);
}

/// <summary>
/// Output of <see cref="IOverrideAwareSchedulerInputsBridge.ApplyAsync"/>.
/// Carries the effective scheduler inputs plus a provenance tag so the
/// admin API and session-plan snapshot can tell the caller "the teacher
/// overrode this".
/// </summary>
public sealed record OverrideApplicationResult(
    SchedulerInputs EffectiveInputs,
    bool BudgetOverridden,
    bool MotivationOverridden,
    IReadOnlyList<string> PinnedTopics);

/// <summary>
/// Default implementation. Reads the aggregate, applies the precedence
/// rules, returns the merged inputs.
/// </summary>
public sealed class OverrideAwareSchedulerInputsBridge : IOverrideAwareSchedulerInputsBridge
{
    /// <summary>
    /// Build a sentinel AbilityEstimate for a pinned topic when the
    /// student has no estimate of their own yet. Low θ with wide SE and
    /// a zero sample size means the scheduler knows it is working from
    /// "teacher's opinion" rather than observed evidence, while still
    /// biasing toward drilling the pin because θ sits below the +0.5
    /// mastery target.
    /// </summary>
    internal static AbilityEstimate BuildPinnedTopicEstimate(
        string studentAnonId, string topicSlug, DateTimeOffset nowUtc)
        => new(
            StudentAnonId: studentAnonId,
            TopicSlug: topicSlug,
            Theta: -0.5,
            StandardError: 0.6,
            SampleSize: 0,
            ComputedAtUtc: nowUtc,
            ObservationWindowWeeks: 0);

    private readonly ITeacherOverrideStore _store;

    public OverrideAwareSchedulerInputsBridge(ITeacherOverrideStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<OverrideApplicationResult> ApplyAsync(
        SchedulerInputs baseInputs,
        string sessionTypeScope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseInputs);
        if (string.IsNullOrWhiteSpace(sessionTypeScope))
            sessionTypeScope = TeacherOverrideCommands.ScopeAll;

        var aggregate = await _store.LoadAsync(baseInputs.StudentAnonId, ct).ConfigureAwait(false);
        var state = aggregate.State;

        var budgetOverridden = state.Budget is not null;
        var weeklyBudget = state.Budget?.WeeklyBudget ?? baseInputs.WeeklyTimeBudget;

        // Motivation: exact scope match wins, fallback to "all".
        var motivation = baseInputs.MotivationProfile;
        var motivationOverridden = false;
        if (state.MotivationByScope.TryGetValue(sessionTypeScope, out var scoped))
        {
            motivation = scoped.Profile;
            motivationOverridden = true;
        }
        else if (state.MotivationByScope.TryGetValue(TeacherOverrideCommands.ScopeAll, out var all))
        {
            motivation = all.Profile;
            motivationOverridden = true;
        }

        // Pins: ensure pinned topics appear in the per-topic estimates with
        // a sentinel so the scheduler prioritises them. Preserve any existing
        // estimate (the student has made attempts) over the sentinel.
        var activePins = state.PinsByTopic.Values
            .Where(p => p.RemainingSessions > 0)
            .Select(p => p.TopicSlug)
            .ToList();

        IReadOnlyDictionary<string, AbilityEstimate> estimates = baseInputs.PerTopicEstimates;
        if (activePins.Count > 0)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, AbilityEstimate>(StringComparer.Ordinal);
            foreach (var kv in baseInputs.PerTopicEstimates)
                builder[kv.Key] = kv.Value;
            foreach (var slug in activePins)
            {
                if (!builder.ContainsKey(slug))
                    builder[slug] = BuildPinnedTopicEstimate(
                        baseInputs.StudentAnonId, slug, baseInputs.NowUtc);
            }
            estimates = builder.ToImmutable();
        }

        var effective = baseInputs with
        {
            WeeklyTimeBudget = weeklyBudget,
            MotivationProfile = motivation,
            PerTopicEstimates = estimates,
        };

        return new OverrideApplicationResult(
            EffectiveInputs: effective,
            BudgetOverridden: budgetOverridden,
            MotivationOverridden: motivationOverridden,
            PinnedTopics: activePins);
    }
}
