// =============================================================================
// Cena Platform — TeacherOverrideAggregate tests (prr-150)
//
// Covers:
//   1. Event-apply fold for pins, budget, motivation (incl. scope keys).
//   2. ReplayFrom ordering across mixed event types.
//   3. StreamKey construction + validation.
//   4. InMemoryTeacherOverrideStore append/load + empty-stream behavior.
//   5. TeacherOverrideCommands end-to-end:
//        - Happy path pins, budget, motivation.
//        - Cross-tenant pin / budget / motivation attempts throw
//          CrossTenantOverrideDeniedException.
//        - Invalid inputs throw the expected ArgumentException.
//   6. OverrideAwareSchedulerInputsBridge precedence (override > student).
//
// Pure unit tests — no Marten, no HTTP, no DI container.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Actors.Teacher.ScheduleOverride;
using Cena.Actors.Teacher.ScheduleOverride.Events;

namespace Cena.Actors.Tests.Teacher;

public sealed class TeacherOverrideAggregateTests
{
    private const string StudentId = "stu-override-1";
    private const string TeacherId = "teacher-42";
    private const string InstituteA = "inst-a";
    private const string InstituteB = "inst-b";

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddDays(1);

    // ---- Stream key -------------------------------------------------------

    [Fact]
    public void StreamKey_builds_teacheroverride_prefix()
    {
        Assert.Equal("teacheroverride-" + StudentId, TeacherOverrideAggregate.StreamKey(StudentId));
    }

    [Fact]
    public void StreamKey_rejects_empty_id()
    {
        Assert.Throws<ArgumentException>(() => TeacherOverrideAggregate.StreamKey(""));
        Assert.Throws<ArgumentException>(() => TeacherOverrideAggregate.StreamKey("   "));
    }

    // ---- Event fold -------------------------------------------------------

    [Fact]
    public void Apply_PinTopic_records_active_pin_with_countdown()
    {
        var agg = new TeacherOverrideAggregate();
        agg.Apply(new PinTopicRequested_V1(
            StudentId, "algebra-logs", 5, TeacherId, InstituteA, "drill log laws", T0));

        Assert.True(agg.State.PinsByTopic.TryGetValue("algebra-logs", out var pin));
        Assert.Equal(5, pin!.RemainingSessions);
        Assert.Equal(TeacherId, pin.TeacherActorId);
        Assert.Equal(InstituteA, pin.InstituteId);
        Assert.Equal(T0, agg.State.UpdatedAt);
        Assert.Null(agg.State.Budget);
        Assert.Empty(agg.State.MotivationByScope);
    }

    [Fact]
    public void Apply_PinTopic_replaces_prior_pin_on_same_slug()
    {
        var agg = new TeacherOverrideAggregate();
        agg.Apply(new PinTopicRequested_V1(StudentId, "algebra-logs", 3, TeacherId, InstituteA, "v1", T0));
        agg.Apply(new PinTopicRequested_V1(StudentId, "algebra-logs", 7, TeacherId, InstituteA, "v2-reset", T1));

        Assert.Equal(7, agg.State.PinsByTopic["algebra-logs"].RemainingSessions);
        Assert.Equal("v2-reset", agg.State.PinsByTopic["algebra-logs"].Rationale);
        Assert.Equal(T1, agg.State.UpdatedAt);
    }

    [Fact]
    public void Apply_PinTopic_distinct_slugs_coexist()
    {
        var agg = new TeacherOverrideAggregate();
        agg.Apply(new PinTopicRequested_V1(StudentId, "algebra-logs", 3, TeacherId, InstituteA, "r1", T0));
        agg.Apply(new PinTopicRequested_V1(StudentId, "trig-identities", 2, TeacherId, InstituteA, "r2", T1));

        Assert.Equal(2, agg.State.PinsByTopic.Count);
        Assert.Equal(3, agg.State.PinsByTopic["algebra-logs"].RemainingSessions);
        Assert.Equal(2, agg.State.PinsByTopic["trig-identities"].RemainingSessions);
    }

    [Fact]
    public void Apply_BudgetAdjusted_last_write_wins()
    {
        var agg = new TeacherOverrideAggregate();
        agg.Apply(new BudgetAdjusted_V1(
            StudentId, TimeSpan.FromHours(6), TeacherId, InstituteA, "ramp", T0));
        agg.Apply(new BudgetAdjusted_V1(
            StudentId, TimeSpan.FromHours(12), TeacherId, InstituteA, "push", T1));

        Assert.NotNull(agg.State.Budget);
        Assert.Equal(TimeSpan.FromHours(12), agg.State.Budget!.WeeklyBudget);
        Assert.Equal("push", agg.State.Budget.Rationale);
        Assert.Equal(T1, agg.State.UpdatedAt);
    }

    [Fact]
    public void Apply_MotivationOverride_keyed_by_scope()
    {
        var agg = new TeacherOverrideAggregate();
        agg.Apply(new MotivationProfileOverridden_V1(
            StudentId, "all", MotivationProfile.Confident, TeacherId, InstituteA, "r1", T0));
        agg.Apply(new MotivationProfileOverridden_V1(
            StudentId, "diagnostic", MotivationProfile.Anxious, TeacherId, InstituteA, "r2", T1));

        Assert.Equal(MotivationProfile.Confident, agg.State.MotivationByScope["all"].Profile);
        Assert.Equal(MotivationProfile.Anxious, agg.State.MotivationByScope["diagnostic"].Profile);
    }

    [Fact]
    public void ReplayFrom_mixed_events_folds_all_three_dimensions()
    {
        var events = new object[]
        {
            new PinTopicRequested_V1(StudentId, "algebra-logs", 3, TeacherId, InstituteA, "r1", T0),
            new BudgetAdjusted_V1(StudentId, TimeSpan.FromHours(10), TeacherId, InstituteA, "r2", T0.AddMinutes(5)),
            new MotivationProfileOverridden_V1(StudentId, "all", MotivationProfile.Confident, TeacherId, InstituteA, "r3", T1),
            new { unknown = true }, // forward-migration tolerance
        };

        var agg = TeacherOverrideAggregate.ReplayFrom(events);

        Assert.Equal(3, agg.State.PinsByTopic["algebra-logs"].RemainingSessions);
        Assert.Equal(TimeSpan.FromHours(10), agg.State.Budget!.WeeklyBudget);
        Assert.Equal(MotivationProfile.Confident, agg.State.MotivationByScope["all"].Profile);
        Assert.Equal(T1, agg.State.UpdatedAt);
    }

    // ---- Store ------------------------------------------------------------

    [Fact]
    public async Task Store_returns_empty_aggregate_for_unknown_student()
    {
        var store = new InMemoryTeacherOverrideStore();
        var agg = await store.LoadAsync("unseen");
        Assert.Empty(agg.State.PinsByTopic);
        Assert.Null(agg.State.Budget);
        Assert.Null(agg.State.UpdatedAt);
    }

    [Fact]
    public async Task Store_appends_and_replays_ordered()
    {
        var store = new InMemoryTeacherOverrideStore();
        await store.AppendAsync(StudentId,
            new PinTopicRequested_V1(StudentId, "t1", 2, TeacherId, InstituteA, "r", T0));
        await store.AppendAsync(StudentId,
            new BudgetAdjusted_V1(StudentId, TimeSpan.FromHours(8), TeacherId, InstituteA, "r", T1));

        var agg = await store.LoadAsync(StudentId);

        Assert.Equal(2, agg.State.PinsByTopic["t1"].RemainingSessions);
        Assert.Equal(TimeSpan.FromHours(8), agg.State.Budget!.WeeklyBudget);
    }

    [Fact]
    public async Task Store_rejects_empty_student_id()
    {
        var store = new InMemoryTeacherOverrideStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendAsync("", new PinTopicRequested_V1("x", "t", 1, TeacherId, InstituteA, "r", T0)));
    }

    // ---- Commands: happy path --------------------------------------------

    private static (TeacherOverrideCommands cmds, InMemoryTeacherOverrideStore store,
            InMemoryStudentInstituteLookup lookup)
        BuildCommands(params (string student, string institute)[] enrollment)
    {
        var store = new InMemoryTeacherOverrideStore();
        var lookup = new InMemoryStudentInstituteLookup();
        foreach (var (s, i) in enrollment) lookup.Set(s, i);
        return (new TeacherOverrideCommands(store, lookup), store, lookup);
    }

    [Fact]
    public async Task PinTopic_appends_when_tenants_match()
    {
        var (cmds, store, _) = BuildCommands((StudentId, InstituteA));
        await cmds.PinTopicAsync(new PinTopicCommand(
            StudentId, "algebra-logs", 3, TeacherId, InstituteA, "drill", T0));

        var agg = await store.LoadAsync(StudentId);
        Assert.Equal(3, agg.State.PinsByTopic["algebra-logs"].RemainingSessions);
    }

    [Fact]
    public async Task AdjustBudget_appends_when_tenants_match()
    {
        var (cmds, store, _) = BuildCommands((StudentId, InstituteA));
        await cmds.AdjustBudgetAsync(new AdjustBudgetCommand(
            StudentId, TimeSpan.FromHours(10), TeacherId, InstituteA, "push", T0));

        var agg = await store.LoadAsync(StudentId);
        Assert.Equal(TimeSpan.FromHours(10), agg.State.Budget!.WeeklyBudget);
    }

    [Fact]
    public async Task OverrideMotivation_appends_when_tenants_match()
    {
        var (cmds, store, _) = BuildCommands((StudentId, InstituteA));
        await cmds.OverrideMotivationAsync(new OverrideMotivationCommand(
            StudentId, "all", MotivationProfile.Confident, TeacherId, InstituteA, "pep", T0));

        var agg = await store.LoadAsync(StudentId);
        Assert.Equal(MotivationProfile.Confident, agg.State.MotivationByScope["all"].Profile);
    }

    // ---- Commands: cross-tenant denial -----------------------------------

    [Fact]
    public async Task PinTopic_throws_cross_tenant_when_student_enrolled_elsewhere()
    {
        var (cmds, store, _) = BuildCommands((StudentId, InstituteB)); // student at B
        var cmd = new PinTopicCommand(
            StudentId, "algebra-logs", 3, TeacherId, InstituteA, "r", T0); // teacher claims A

        var ex = await Assert.ThrowsAsync<CrossTenantOverrideDeniedException>(() =>
            cmds.PinTopicAsync(cmd));
        Assert.Equal(InstituteA, ex.TeacherInstituteId);
        Assert.Equal(InstituteB, ex.StudentInstituteId);

        // No event should have been appended.
        var agg = await store.LoadAsync(StudentId);
        Assert.Empty(agg.State.PinsByTopic);
    }

    [Fact]
    public async Task AdjustBudget_throws_cross_tenant_and_does_not_persist()
    {
        var (cmds, store, _) = BuildCommands((StudentId, InstituteB));
        await Assert.ThrowsAsync<CrossTenantOverrideDeniedException>(() =>
            cmds.AdjustBudgetAsync(new AdjustBudgetCommand(
                StudentId, TimeSpan.FromHours(10), TeacherId, InstituteA, "r", T0)));

        var agg = await store.LoadAsync(StudentId);
        Assert.Null(agg.State.Budget);
    }

    [Fact]
    public async Task OverrideMotivation_throws_cross_tenant_and_does_not_persist()
    {
        var (cmds, store, _) = BuildCommands((StudentId, InstituteB));
        await Assert.ThrowsAsync<CrossTenantOverrideDeniedException>(() =>
            cmds.OverrideMotivationAsync(new OverrideMotivationCommand(
                StudentId, "all", MotivationProfile.Confident, TeacherId, InstituteA, "r", T0)));

        var agg = await store.LoadAsync(StudentId);
        Assert.Empty(agg.State.MotivationByScope);
    }

    [Fact]
    public async Task Unknown_student_also_denies_to_avoid_existence_leak()
    {
        // No enrollment recorded => lookup returns null. This MUST surface
        // the same exception, so a caller cannot use the override endpoint
        // to probe which student ids exist in another tenant.
        var (cmds, _, _) = BuildCommands(/* no enrollment */);
        await Assert.ThrowsAsync<CrossTenantOverrideDeniedException>(() =>
            cmds.PinTopicAsync(new PinTopicCommand(
                StudentId, "t", 1, TeacherId, InstituteA, "r", T0)));
    }

    // ---- Commands: input validation --------------------------------------

    [Fact]
    public async Task PinTopic_rejects_out_of_range_session_count()
    {
        var (cmds, _, _) = BuildCommands((StudentId, InstituteA));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cmds.PinTopicAsync(new PinTopicCommand(
                StudentId, "t", 0, TeacherId, InstituteA, "r", T0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cmds.PinTopicAsync(new PinTopicCommand(
                StudentId, "t", 999, TeacherId, InstituteA, "r", T0)));
    }

    [Fact]
    public async Task AdjustBudget_rejects_out_of_range_budget()
    {
        var (cmds, _, _) = BuildCommands((StudentId, InstituteA));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cmds.AdjustBudgetAsync(new AdjustBudgetCommand(
                StudentId, TimeSpan.FromMinutes(30), TeacherId, InstituteA, "r", T0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cmds.AdjustBudgetAsync(new AdjustBudgetCommand(
                StudentId, TimeSpan.FromHours(80), TeacherId, InstituteA, "r", T0)));
    }

    // ---- Bridge precedence -----------------------------------------------

    [Fact]
    public async Task Bridge_leaves_inputs_unchanged_when_no_overrides()
    {
        var store = new InMemoryTeacherOverrideStore();
        var bridge = new OverrideAwareSchedulerInputsBridge(store);
        var baseInputs = MakeBaseInputs();

        var result = await bridge.ApplyAsync(baseInputs, "all");

        Assert.Equal(baseInputs.WeeklyTimeBudget, result.EffectiveInputs.WeeklyTimeBudget);
        Assert.Equal(baseInputs.MotivationProfile, result.EffectiveInputs.MotivationProfile);
        Assert.False(result.BudgetOverridden);
        Assert.False(result.MotivationOverridden);
        Assert.Empty(result.PinnedTopics);
    }

    [Fact]
    public async Task Bridge_overrides_budget_and_motivation_when_active()
    {
        var store = new InMemoryTeacherOverrideStore();
        await store.AppendAsync(StudentId, new BudgetAdjusted_V1(
            StudentId, TimeSpan.FromHours(15), TeacherId, InstituteA, "push", T0));
        await store.AppendAsync(StudentId, new MotivationProfileOverridden_V1(
            StudentId, "all", MotivationProfile.Confident, TeacherId, InstituteA, "pep", T0));
        var bridge = new OverrideAwareSchedulerInputsBridge(store);

        var result = await bridge.ApplyAsync(MakeBaseInputs(), "all");

        Assert.Equal(TimeSpan.FromHours(15), result.EffectiveInputs.WeeklyTimeBudget);
        Assert.Equal(MotivationProfile.Confident, result.EffectiveInputs.MotivationProfile);
        Assert.True(result.BudgetOverridden);
        Assert.True(result.MotivationOverridden);
    }

    [Fact]
    public async Task Bridge_adds_pinned_topic_to_estimates()
    {
        var store = new InMemoryTeacherOverrideStore();
        await store.AppendAsync(StudentId, new PinTopicRequested_V1(
            StudentId, "pinned-topic", 3, TeacherId, InstituteA, "r", T0));
        var bridge = new OverrideAwareSchedulerInputsBridge(store);

        var result = await bridge.ApplyAsync(MakeBaseInputs(), "all");

        Assert.Contains("pinned-topic", result.EffectiveInputs.PerTopicEstimates.Keys);
        Assert.Single(result.PinnedTopics);
        Assert.Equal("pinned-topic", result.PinnedTopics[0]);
    }

    [Fact]
    public async Task Bridge_scoped_motivation_beats_all_scope()
    {
        var store = new InMemoryTeacherOverrideStore();
        await store.AppendAsync(StudentId, new MotivationProfileOverridden_V1(
            StudentId, "all", MotivationProfile.Neutral, TeacherId, InstituteA, "r1", T0));
        await store.AppendAsync(StudentId, new MotivationProfileOverridden_V1(
            StudentId, "diagnostic", MotivationProfile.Anxious, TeacherId, InstituteA, "r2", T0));
        var bridge = new OverrideAwareSchedulerInputsBridge(store);

        var diagnosticResult = await bridge.ApplyAsync(MakeBaseInputs(), "diagnostic");
        var drillResult = await bridge.ApplyAsync(MakeBaseInputs(), "drill");

        Assert.Equal(MotivationProfile.Anxious, diagnosticResult.EffectiveInputs.MotivationProfile);
        Assert.Equal(MotivationProfile.Neutral, drillResult.EffectiveInputs.MotivationProfile);
    }

    private static SchedulerInputs MakeBaseInputs() => new(
        StudentAnonId: StudentId,
        PerTopicEstimates: ImmutableDictionary<string, AbilityEstimate>.Empty,
        DeadlineUtc: T2,
        WeeklyTimeBudget: TimeSpan.FromHours(5),
        MotivationProfile: MotivationProfile.Neutral,
        NowUtc: T0,
        PrerequisiteGraph: null);
}
