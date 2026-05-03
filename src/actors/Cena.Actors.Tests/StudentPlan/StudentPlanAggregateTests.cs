// =============================================================================
// Cena Platform — StudentPlanAggregate tests (prr-218, supersedes prr-148)
//
// Covers the multi-target fold plus backward-compat tests for the legacy
// single-target events still on existing streams.
//
//   1. StreamKey construction + validation.
//   2. Multi-target event fold (Initialized + Added + Updated + Archived
//      + Completed + Override + Migrated + MigrationFailed).
//   3. Legacy event fold (ExamDateSet_V1 / WeeklyTimeBudgetSet_V1) still
//      populates the legacy-scalar projection path.
//   4. ToConfig() projection:
//        - Active targets present → WeeklyBudget = sum of hours, Deadline
//          deferred to catalog (null).
//        - No active targets, legacy scalars set → legacy values projected.
//        - Empty aggregate → all nulls.
//   5. InMemoryStudentPlanAggregateStore replay is ordered + idempotent
//      for unknown streams.
//   6. IStudentPlanInputsService + IStudentPlanReader surfaces.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class StudentPlanAggregateTests
{
    private const string StudentId = "stu-plan-1";

    private static readonly SittingCode Bagrut2026Summer = new(
        AcademicYear: "תשפ״ו",
        Season: SittingSeason.Summer,
        Moed: SittingMoed.A);

    private static readonly SittingCode Bagrut2026Winter = new(
        AcademicYear: "תשפ״ו",
        Season: SittingSeason.Winter,
        Moed: SittingMoed.A);

    private static ExamTarget NewTarget(
        string id = "et-001",
        string examCode = "BAGRUT_MATH_5U",
        string? track = "5U",
        int weeklyHours = 5,
        SittingCode? sitting = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? archivedAt = null,
        IReadOnlyList<string>? questionPaperCodes = null,
        IReadOnlyDictionary<string, SittingCode>? perPaperSittingOverride = null)
        => new(
            Id: new ExamTargetId(id),
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(StudentId),
            EnrollmentId: null,
            ExamCode: new ExamCode(examCode),
            Track: track is null ? null : new TrackCode(track),
            QuestionPaperCodes: questionPaperCodes
                ?? (examCode.StartsWith("BAGRUT_", StringComparison.Ordinal)
                        ? new[] { "035581" } // plausible default for tests
                        : Array.Empty<string>()),
            Sitting: sitting ?? Bagrut2026Summer,
            PerPaperSittingOverride: perPaperSittingOverride,
            WeeklyHours: weeklyHours,
            ReasonTag: null,
            CreatedAt: createdAt ?? DateTimeOffset.Parse("2026-04-21T10:00:00Z"),
            ArchivedAt: archivedAt);

    // ── Stream key ────────────────────────────────────────────────────────

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

    // ── Multi-target fold ────────────────────────────────────────────────

    [Fact]
    public void Apply_StudentPlanInitialized_sets_initialized_at()
    {
        var agg = new StudentPlanAggregate();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

        agg.Apply(new StudentPlanInitialized_V1(StudentId, t));

        Assert.Equal(t, agg.State.InitializedAt);
        Assert.Equal(t, agg.State.UpdatedAt);
        Assert.Empty(agg.State.Targets);
    }

    [Fact]
    public void Apply_ExamTargetAdded_appends_to_targets()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));

        Assert.Single(agg.State.Targets);
        Assert.Equal(target, agg.State.Targets[0]);
        Assert.Single(agg.State.ActiveTargets);
    }

    [Fact]
    public void Apply_ExamTargetUpdated_mutates_target()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));

        agg.Apply(new ExamTargetUpdated_V1(
            StudentId, target.Id,
            new TrackCode("4U"), Bagrut2026Winter, 8, ReasonTag.Retake,
            t.AddHours(1)));

        var updated = agg.State.Targets[0];
        Assert.Equal("4U", updated.Track?.Value);
        Assert.Equal(Bagrut2026Winter, updated.Sitting);
        Assert.Equal(8, updated.WeeklyHours);
        Assert.Equal(ReasonTag.Retake, updated.ReasonTag);
    }

    [Fact]
    public void Apply_ExamTargetArchived_sets_archived_at()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));

        agg.Apply(new ExamTargetArchived_V1(
            StudentId, target.Id, t.AddDays(1), ArchiveReason.StudentDeclined));

        Assert.False(agg.State.Targets[0].IsActive);
        Assert.Empty(agg.State.ActiveTargets);
    }

    [Fact]
    public void Apply_ExamTargetArchived_is_idempotent_on_already_archived()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));
        agg.Apply(new ExamTargetArchived_V1(StudentId, target.Id, t.AddDays(1), ArchiveReason.Completed));

        var firstArchive = agg.State.Targets[0].ArchivedAt;
        agg.Apply(new ExamTargetArchived_V1(StudentId, target.Id, t.AddDays(5), ArchiveReason.StudentDeclined));

        // Terminal: second archive event is ignored by the fold.
        Assert.Equal(firstArchive, agg.State.Targets[0].ArchivedAt);
    }

    [Fact]
    public void Apply_ExamTargetCompleted_terminates_target()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));

        agg.Apply(new ExamTargetCompleted_V1(StudentId, target.Id, t.AddDays(30)));

        Assert.False(agg.State.Targets[0].IsActive);
        Assert.Equal(t.AddDays(30), agg.State.Targets[0].ArchivedAt);
    }

    [Fact]
    public void Apply_ExamTargetUpdated_is_silently_ignored_for_archived()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));
        agg.Apply(new ExamTargetArchived_V1(StudentId, target.Id, t.AddDays(1), ArchiveReason.Completed));

        // An errant Updated after archival must NOT resurrect the target.
        agg.Apply(new ExamTargetUpdated_V1(
            StudentId, target.Id,
            new TrackCode("4U"), Bagrut2026Winter, 10, null,
            t.AddDays(2)));

        Assert.False(agg.State.Targets[0].IsActive);
        // WeeklyHours did not change (update was no-op).
        Assert.Equal(target.WeeklyHours, agg.State.Targets[0].WeeklyHours);
    }

    [Fact]
    public void Apply_telemetry_events_do_not_mutate_target_list()
    {
        var agg = new StudentPlanAggregate();
        var target = NewTarget();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, t));

        agg.Apply(new ExamTargetOverrideApplied_V1(StudentId, target.Id, "session-1", t.AddDays(1)));
        agg.Apply(new StudentPlanMigrationFailed_V1(
            StudentId, "tenant-a", "legacy-1", MigrationErrorCategory.Transient, "bus", 1, t));
        agg.Apply(new StudentPlanMigrated_V1(
            StudentId, "tenant-a", "legacy-1", target.Id, t.AddDays(1)));

        Assert.Single(agg.State.Targets);
        Assert.Equal(target, agg.State.Targets[0]);
    }

    // ── Legacy event fold (backward compat) ───────────────────────────────

    [Fact]
    public void Apply_legacy_ExamDateSet_sets_legacy_deadline()
    {
        var agg = new StudentPlanAggregate();
        var deadline = DateTimeOffset.Parse("2026-07-01T08:00:00Z");
        var setAt = DateTimeOffset.Parse("2026-04-20T12:00:00Z");

        agg.Apply(new ExamDateSet_V1(StudentId, deadline, setAt));

        Assert.Equal(deadline, agg.State.LegacyDeadlineUtc);
        Assert.Null(agg.State.LegacyWeeklyBudget);
        Assert.Equal(setAt, agg.State.UpdatedAt);
    }

    [Fact]
    public void Apply_legacy_WeeklyTimeBudgetSet_sets_legacy_weekly_budget()
    {
        var agg = new StudentPlanAggregate();
        var budget = TimeSpan.FromHours(8);
        var setAt = DateTimeOffset.Parse("2026-04-20T12:00:00Z");

        agg.Apply(new WeeklyTimeBudgetSet_V1(StudentId, budget, setAt));

        Assert.Equal(budget, agg.State.LegacyWeeklyBudget);
        Assert.Null(agg.State.LegacyDeadlineUtc);
        Assert.Equal(setAt, agg.State.UpdatedAt);
    }

    // ── Replay determinism ──────────────────────────────────────────────

    [Fact]
    public void ReplayFrom_folds_mixed_events_deterministically()
    {
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var target1 = NewTarget("et-001", examCode: "BAGRUT_MATH_5U", weeklyHours: 5);
        var target2 = NewTarget("et-002", examCode: "BAGRUT_ENGLISH", weeklyHours: 3);

        var events = new object[]
        {
            new StudentPlanInitialized_V1(StudentId, t),
            new ExamTargetAdded_V1(StudentId, target1, t.AddMinutes(1)),
            new ExamTargetAdded_V1(StudentId, target2, t.AddMinutes(2)),
            new ExamTargetUpdated_V1(StudentId, target1.Id, new TrackCode("4U"),
                Bagrut2026Summer, 6, ReasonTag.Enrichment, t.AddMinutes(3)),
            new ExamTargetArchived_V1(StudentId, target2.Id, t.AddMinutes(4), ArchiveReason.StudentDeclined),
        };

        var agg = StudentPlanAggregate.ReplayFrom(events);

        Assert.Equal(2, agg.State.Targets.Count);
        Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(6, agg.State.Targets[0].WeeklyHours);
        Assert.Equal("4U", agg.State.Targets[0].Track?.Value);
        Assert.False(agg.State.Targets[1].IsActive);
    }

    [Fact]
    public void Apply_Unknown_event_types_are_silently_ignored()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new { not_a_known_event = true });
        Assert.Null(agg.State.InitializedAt);
        Assert.Empty(agg.State.Targets);
    }

    // ── Projection ───────────────────────────────────────────────────────

    [Fact]
    public void ToConfig_with_active_targets_sums_hours_leaves_deadline_null()
    {
        var agg = new StudentPlanAggregate();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        agg.Apply(new ExamTargetAdded_V1(StudentId, NewTarget("et-1", weeklyHours: 5), t));
        agg.Apply(new ExamTargetAdded_V1(StudentId, NewTarget("et-2", examCode: "BAGRUT_ENGLISH", weeklyHours: 3), t));

        var config = agg.ToConfig(StudentId);

        Assert.Null(config.DeadlineUtc);
        Assert.Equal(TimeSpan.FromHours(8), config.WeeklyBudget);
    }

    [Fact]
    public void ToConfig_with_only_legacy_events_projects_legacy_scalars()
    {
        var agg = new StudentPlanAggregate();
        var deadline = DateTimeOffset.Parse("2026-07-01T08:00:00Z");
        var t = DateTimeOffset.Parse("2026-04-20T12:00:00Z");
        agg.Apply(new ExamDateSet_V1(StudentId, deadline, t));
        agg.Apply(new WeeklyTimeBudgetSet_V1(StudentId, TimeSpan.FromHours(10), t));

        var config = agg.ToConfig(StudentId);

        Assert.Equal(deadline, config.DeadlineUtc);
        Assert.Equal(TimeSpan.FromHours(10), config.WeeklyBudget);
    }

    [Fact]
    public void ToConfig_empty_aggregate_returns_all_nulls()
    {
        var agg = new StudentPlanAggregate();
        var config = agg.ToConfig(StudentId);

        Assert.Null(config.DeadlineUtc);
        Assert.Null(config.WeeklyBudget);
        Assert.Null(config.UpdatedAt);
    }

    // ── Store basics ────────────────────────────────────────────────────

    [Fact]
    public async Task Store_returns_empty_aggregate_for_unknown_student()
    {
        var store = new InMemoryStudentPlanAggregateStore();

        var agg = await store.LoadAsync("never-seen-student");

        Assert.Empty(agg.State.Targets);
        Assert.Null(agg.State.InitializedAt);
    }

    [Fact]
    public async Task Store_appends_and_replays_ordered()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var target = NewTarget();

        await store.AppendAsync(StudentId, new StudentPlanInitialized_V1(StudentId, t));
        await store.AppendAsync(StudentId, new ExamTargetAdded_V1(StudentId, target, t.AddMinutes(1)));

        var agg = await store.LoadAsync(StudentId);

        Assert.Equal(t, agg.State.InitializedAt);
        Assert.Single(agg.State.Targets);
    }

    [Fact]
    public async Task Store_rejects_empty_student_id()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendAsync("", new StudentPlanInitialized_V1("x", DateTimeOffset.UtcNow)));
    }

    // ── Read surfaces ────────────────────────────────────────────────────

    [Fact]
    public async Task InputsService_projects_through_ToConfig()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var service = new StudentPlanInputsService(store);
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

        await store.AppendAsync(StudentId, new ExamTargetAdded_V1(StudentId, NewTarget(weeklyHours: 6), t));

        var config = await service.GetAsync(StudentId);

        Assert.Equal(StudentId, config.StudentAnonId);
        Assert.Equal(TimeSpan.FromHours(6), config.WeeklyBudget);
    }

    [Fact]
    public async Task InputsService_returns_empty_for_new_student()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var service = new StudentPlanInputsService(store);

        var config = await service.GetAsync("new-student");

        Assert.Null(config.DeadlineUtc);
        Assert.Null(config.WeeklyBudget);
    }

    [Fact]
    public async Task Reader_hides_archived_by_default()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var reader = new StudentPlanReader(store);
        var t = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var target = NewTarget();
        await store.AppendAsync(StudentId, new ExamTargetAdded_V1(StudentId, target, t));
        await store.AppendAsync(StudentId, new ExamTargetArchived_V1(StudentId, target.Id, t.AddDays(1), ArchiveReason.StudentDeclined));

        var active = await reader.ListTargetsAsync(StudentId);
        var all = await reader.ListTargetsAsync(StudentId, includeArchived: true);

        Assert.Empty(active);
        Assert.Single(all);
    }

    [Fact]
    public async Task Reader_FindTarget_returns_null_for_unknown()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var reader = new StudentPlanReader(store);

        var result = await reader.FindTargetAsync(StudentId, new ExamTargetId("nope"));

        Assert.Null(result);
    }
}
