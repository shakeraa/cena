// =============================================================================
// Cena Platform — StudentPlanAggregate tests (prr-148)
//
// Covers:
//   1. Event-apply fold (last-write-wins on both fields).
//   2. ReplayFrom ordering across mixed event types.
//   3. StreamKey construction + validation.
//   4. StudentPlanInputsService returns the aggregate's projected VO.
//   5. InMemoryStudentPlanAggregateStore is thread-safe and returns empty
//      aggregate for unknown streams.
//
// These are pure unit tests — no Marten, no HTTP, no DI container.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class StudentPlanAggregateTests
{
    private const string StudentId = "stu-plan-1";

    [Fact]
    public void StreamKey_builds_studentplan_prefix()
    {
        Assert.Equal("studentplan-" + StudentId, StudentPlanAggregate.StreamKey(StudentId));
    }

    [Fact]
    public void StreamKey_rejects_empty_id()
    {
        Assert.Throws<ArgumentException>(() => StudentPlanAggregate.StreamKey(""));
        Assert.Throws<ArgumentException>(() => StudentPlanAggregate.StreamKey("   "));
    }

    [Fact]
    public void Apply_ExamDateSet_updates_deadline_and_timestamp()
    {
        var agg = new StudentPlanAggregate();
        var deadline = DateTimeOffset.Parse("2026-07-01T08:00:00Z");
        var setAt = DateTimeOffset.Parse("2026-04-20T12:00:00Z");

        agg.Apply(new ExamDateSet_V1(StudentId, deadline, setAt));

        Assert.Equal(deadline, agg.State.DeadlineUtc);
        Assert.Null(agg.State.WeeklyBudget);
        Assert.Equal(setAt, agg.State.UpdatedAt);
    }

    [Fact]
    public void Apply_WeeklyTimeBudgetSet_updates_budget_and_timestamp()
    {
        var agg = new StudentPlanAggregate();
        var budget = TimeSpan.FromHours(8);
        var setAt = DateTimeOffset.Parse("2026-04-20T12:00:00Z");

        agg.Apply(new WeeklyTimeBudgetSet_V1(StudentId, budget, setAt));

        Assert.Null(agg.State.DeadlineUtc);
        Assert.Equal(budget, agg.State.WeeklyBudget);
        Assert.Equal(setAt, agg.State.UpdatedAt);
    }

    [Fact]
    public void LastWriteWins_on_deadline()
    {
        var agg = new StudentPlanAggregate();
        var first = DateTimeOffset.Parse("2026-07-01T08:00:00Z");
        var second = DateTimeOffset.Parse("2026-08-15T08:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-04-25T10:00:00Z");

        agg.Apply(new ExamDateSet_V1(StudentId, first, t1));
        agg.Apply(new ExamDateSet_V1(StudentId, second, t2));

        Assert.Equal(second, agg.State.DeadlineUtc);
        Assert.Equal(t2, agg.State.UpdatedAt);
    }

    [Fact]
    public void LastWriteWins_on_weekly_budget()
    {
        var agg = new StudentPlanAggregate();
        var t1 = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-04-25T10:00:00Z");

        agg.Apply(new WeeklyTimeBudgetSet_V1(StudentId, TimeSpan.FromHours(5), t1));
        agg.Apply(new WeeklyTimeBudgetSet_V1(StudentId, TimeSpan.FromHours(12), t2));

        Assert.Equal(TimeSpan.FromHours(12), agg.State.WeeklyBudget);
        Assert.Equal(t2, agg.State.UpdatedAt);
    }

    [Fact]
    public void Mixed_event_replay_folds_both_fields()
    {
        var events = new object[]
        {
            new ExamDateSet_V1(StudentId,
                DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
                DateTimeOffset.Parse("2026-04-20T09:00:00Z")),
            new WeeklyTimeBudgetSet_V1(StudentId,
                TimeSpan.FromHours(10),
                DateTimeOffset.Parse("2026-04-20T09:30:00Z")),
            new WeeklyTimeBudgetSet_V1(StudentId,
                TimeSpan.FromHours(15),
                DateTimeOffset.Parse("2026-04-21T09:00:00Z")),
        };

        var agg = StudentPlanAggregate.ReplayFrom(events);

        Assert.Equal(DateTimeOffset.Parse("2026-07-01T08:00:00Z"), agg.State.DeadlineUtc);
        Assert.Equal(TimeSpan.FromHours(15), agg.State.WeeklyBudget);
        Assert.Equal(DateTimeOffset.Parse("2026-04-21T09:00:00Z"), agg.State.UpdatedAt);
    }

    [Fact]
    public void Unknown_event_types_are_silently_ignored()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new { not_a_known_event = true }); // POCO
        Assert.Null(agg.State.DeadlineUtc);
        Assert.Null(agg.State.WeeklyBudget);
        Assert.Null(agg.State.UpdatedAt);
    }

    [Fact]
    public void ToConfig_projects_state_into_VO()
    {
        var agg = new StudentPlanAggregate();
        var deadline = DateTimeOffset.Parse("2026-07-01T08:00:00Z");
        var setAt = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        agg.Apply(new ExamDateSet_V1(StudentId, deadline, setAt));
        agg.Apply(new WeeklyTimeBudgetSet_V1(StudentId, TimeSpan.FromHours(8), setAt));

        var config = agg.ToConfig(StudentId);

        Assert.Equal(StudentId, config.StudentAnonId);
        Assert.Equal(deadline, config.DeadlineUtc);
        Assert.Equal(TimeSpan.FromHours(8), config.WeeklyBudget);
        Assert.Equal(setAt, config.UpdatedAt);
    }

    [Fact]
    public async Task Store_returns_empty_aggregate_for_unknown_student()
    {
        var store = new InMemoryStudentPlanAggregateStore();

        var agg = await store.LoadAsync("never-seen-student");

        Assert.Null(agg.State.DeadlineUtc);
        Assert.Null(agg.State.WeeklyBudget);
        Assert.Null(agg.State.UpdatedAt);
    }

    [Fact]
    public async Task Store_appends_and_replays_ordered()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var t1 = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var deadline = DateTimeOffset.Parse("2026-07-01T08:00:00Z");

        await store.AppendAsync(StudentId, new ExamDateSet_V1(StudentId, deadline, t1));
        await store.AppendAsync(StudentId, new WeeklyTimeBudgetSet_V1(StudentId, TimeSpan.FromHours(6), t2));

        var agg = await store.LoadAsync(StudentId);

        Assert.Equal(deadline, agg.State.DeadlineUtc);
        Assert.Equal(TimeSpan.FromHours(6), agg.State.WeeklyBudget);
        Assert.Equal(t2, agg.State.UpdatedAt);
    }

    [Fact]
    public async Task Store_rejects_empty_student_id()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendAsync("", new ExamDateSet_V1("x", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task ConfigService_returns_projected_config()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var service = new StudentPlanInputsService(store);
        var t = DateTimeOffset.Parse("2026-04-20T10:00:00Z");
        var deadline = DateTimeOffset.Parse("2026-07-01T08:00:00Z");

        await store.AppendAsync(StudentId, new ExamDateSet_V1(StudentId, deadline, t));
        await store.AppendAsync(StudentId, new WeeklyTimeBudgetSet_V1(StudentId, TimeSpan.FromHours(12), t));

        var config = await service.GetAsync(StudentId);

        Assert.Equal(StudentId, config.StudentAnonId);
        Assert.Equal(deadline, config.DeadlineUtc);
        Assert.Equal(TimeSpan.FromHours(12), config.WeeklyBudget);
    }

    [Fact]
    public async Task ConfigService_returns_empty_for_new_student()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var service = new StudentPlanInputsService(store);

        var config = await service.GetAsync("new-student");

        Assert.Null(config.DeadlineUtc);
        Assert.Null(config.WeeklyBudget);
        Assert.Null(config.UpdatedAt);
    }
}
