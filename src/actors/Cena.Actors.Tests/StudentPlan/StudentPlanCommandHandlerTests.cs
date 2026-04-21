// =============================================================================
// Cena Platform — StudentPlanCommandHandler tests (prr-218, ADR-0050 §5)
//
// Exhaustive invariant coverage for the command-surface gate that emits
// ExamTarget*_V1 events. Matches the Definition-of-Done "100% test
// coverage on invariant-violating inputs".
//
// Categories:
//   1. Add: shape validation + cap (5) + budget (40h) + uniqueness.
//   2. Update: target-exists + archived-is-terminal + re-check invariants
//      across the edited row.
//   3. Archive: terminal, idempotent on re-archive.
//   4. Complete: terminal, distinct semantic from archive.
//   5. Override: telemetry-only, no invariant checks beyond existence.
//   6. Source/enrollment consistency.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class StudentPlanCommandHandlerTests
{
    private const string StudentId = "stu-test-1";
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    private static readonly SittingCode Bagrut2026Summer = new(
        "תשפ״ו", SittingSeason.Summer, SittingMoed.A);
    private static readonly SittingCode Bagrut2026Winter = new(
        "תשפ״ו", SittingSeason.Winter, SittingMoed.A);

    private static (StudentPlanCommandHandler handler, InMemoryStudentPlanAggregateStore store) Build()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store, () => FixedNow);
        return (handler, store);
    }

    private static AddExamTargetCommand SampleAdd(
        int weeklyHours = 5,
        string examCode = "BAGRUT_MATH_5U",
        string? track = "5U",
        SittingCode? sitting = null,
        ExamTargetSource source = ExamTargetSource.Student,
        EnrollmentId? enrollmentId = null)
        => new(
            StudentAnonId: StudentId,
            Source: source,
            AssignedById: new UserId(StudentId),
            EnrollmentId: enrollmentId,
            ExamCode: new ExamCode(examCode),
            Track: track is null ? null : new TrackCode(track),
            Sitting: sitting ?? Bagrut2026Summer,
            WeeklyHours: weeklyHours,
            ReasonTag: null);

    // ── Add: happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task Add_first_target_initializes_stream_and_emits_added_event()
    {
        var (handler, store) = Build();

        var result = await handler.HandleAsync(SampleAdd());

        Assert.True(result.Success);
        Assert.NotNull(result.TargetId);
        var events = store.GetRawEvents(StudentId);
        Assert.Equal(2, events.Count);
        Assert.IsType<StudentPlanInitialized_V1>(events[0]);
        Assert.IsType<ExamTargetAdded_V1>(events[1]);
    }

    [Fact]
    public async Task Add_second_target_does_not_re_initialize_stream()
    {
        var (handler, store) = Build();
        await handler.HandleAsync(SampleAdd(weeklyHours: 5));
        await handler.HandleAsync(SampleAdd(
            examCode: "BAGRUT_ENGLISH", track: null, weeklyHours: 3));

        var events = store.GetRawEvents(StudentId);
        Assert.Single(events.OfType<StudentPlanInitialized_V1>());
        Assert.Equal(2, events.OfType<ExamTargetAdded_V1>().Count());
    }

    // ── Add: invariants (ADR-0050 §5) ────────────────────────────────────

    [Fact]
    public async Task Add_rejects_when_cap_of_5_active_targets_reached()
    {
        var (handler, _) = Build();
        for (int i = 0; i < StudentPlanCommandHandler.MaxActiveTargets; i++)
        {
            var r = await handler.HandleAsync(SampleAdd(
                examCode: $"EXAM_{i}", track: null, weeklyHours: 1));
            Assert.True(r.Success);
        }

        var overflow = await handler.HandleAsync(SampleAdd(
            examCode: "EXAM_OVER", track: null, weeklyHours: 1));

        Assert.False(overflow.Success);
        Assert.Equal(CommandError.ActiveTargetCapExceeded, overflow.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(41)]
    [InlineData(10000)]
    public async Task Add_rejects_weekly_hours_out_of_range(int weeklyHours)
    {
        var (handler, _) = Build();
        var r = await handler.HandleAsync(SampleAdd(weeklyHours: weeklyHours));
        Assert.False(r.Success);
        Assert.Equal(CommandError.WeeklyHoursOutOfRange, r.Error);
    }

    [Fact]
    public async Task Add_rejects_when_total_weekly_hours_would_exceed_40()
    {
        var (handler, _) = Build();
        await handler.HandleAsync(SampleAdd(examCode: "EX_A", track: null, weeklyHours: 30));
        await handler.HandleAsync(SampleAdd(examCode: "EX_B", track: null, weeklyHours: 9));

        var overflow = await handler.HandleAsync(SampleAdd(
            examCode: "EX_C", track: null, weeklyHours: 2));

        Assert.False(overflow.Success);
        Assert.Equal(CommandError.WeeklyBudgetExceeded, overflow.Error);
    }

    [Fact]
    public async Task Add_rejects_duplicate_examCode_sitting_track_tuple()
    {
        var (handler, _) = Build();
        await handler.HandleAsync(SampleAdd(weeklyHours: 5));

        var dup = await handler.HandleAsync(SampleAdd(weeklyHours: 3));

        Assert.False(dup.Success);
        Assert.Equal(CommandError.DuplicateTarget, dup.Error);
    }

    [Fact]
    public async Task Add_allows_same_examCode_with_different_sitting()
    {
        var (handler, _) = Build();
        await handler.HandleAsync(SampleAdd(sitting: Bagrut2026Summer, weeklyHours: 5));

        var second = await handler.HandleAsync(SampleAdd(sitting: Bagrut2026Winter, weeklyHours: 3));

        Assert.True(second.Success);
    }

    [Fact]
    public async Task Add_allows_reusing_examCode_track_after_archive()
    {
        var (handler, _) = Build();
        var first = await handler.HandleAsync(SampleAdd(weeklyHours: 5));
        Assert.True(first.Success);
        await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, first.TargetId!.Value, ArchiveReason.StudentDeclined));

        var second = await handler.HandleAsync(SampleAdd(weeklyHours: 5));

        Assert.True(second.Success);
    }

    // ── Add: source/enrollment consistency ───────────────────────────────

    [Fact]
    public async Task Add_Student_rejects_non_null_enrollment()
    {
        var (handler, _) = Build();
        var r = await handler.HandleAsync(SampleAdd(
            source: ExamTargetSource.Student,
            enrollmentId: new EnrollmentId("enrol-1")));
        Assert.False(r.Success);
        Assert.Equal(CommandError.SourceAssignmentMismatch, r.Error);
    }

    [Fact]
    public async Task Add_Classroom_rejects_null_enrollment()
    {
        var (handler, _) = Build();
        var r = await handler.HandleAsync(SampleAdd(
            source: ExamTargetSource.Classroom, enrollmentId: null));
        Assert.False(r.Success);
        Assert.Equal(CommandError.SourceAssignmentMismatch, r.Error);
    }

    [Fact]
    public async Task Add_Tenant_requires_enrollment()
    {
        var (handler, _) = Build();
        var r = await handler.HandleAsync(SampleAdd(
            source: ExamTargetSource.Tenant, enrollmentId: null));
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Add_Migration_allows_either_enrollment_state()
    {
        var (handler, _) = Build();
        var r1 = await handler.HandleAsync(SampleAdd(
            source: ExamTargetSource.Migration, enrollmentId: null));
        var r2 = await handler.HandleAsync(SampleAdd(
            source: ExamTargetSource.Migration,
            enrollmentId: new EnrollmentId("enrol-1"),
            examCode: "BAGRUT_ENGLISH", track: null));
        Assert.True(r1.Success);
        Assert.True(r2.Success);
    }

    // ── Update ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_existing_active_target_succeeds()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd(weeklyHours: 5));

        var up = await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentId, add.TargetId!.Value,
            new TrackCode("4U"), Bagrut2026Winter, 6, ReasonTag.Enrichment));

        Assert.True(up.Success);
    }

    [Fact]
    public async Task Update_non_existent_target_returns_not_found()
    {
        var (handler, _) = Build();

        var r = await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentId, new ExamTargetId("et-does-not-exist"),
            null, Bagrut2026Summer, 5, null));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }

    [Fact]
    public async Task Update_archived_target_returns_archived()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd(weeklyHours: 5));
        await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, add.TargetId!.Value, ArchiveReason.StudentDeclined));

        var r = await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentId, add.TargetId.Value, null, Bagrut2026Summer, 5, null));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetArchived, r.Error);
    }

    [Fact]
    public async Task Update_rejects_weekly_hours_out_of_range()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd(weeklyHours: 5));

        var r = await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentId, add.TargetId!.Value, null, Bagrut2026Summer, 0, null));

        Assert.False(r.Success);
        Assert.Equal(CommandError.WeeklyHoursOutOfRange, r.Error);
    }

    [Fact]
    public async Task Update_rejects_when_new_hours_exceed_total_budget()
    {
        var (handler, _) = Build();
        await handler.HandleAsync(SampleAdd(examCode: "EX_A", track: null, weeklyHours: 30));
        var add = await handler.HandleAsync(SampleAdd(examCode: "EX_B", track: null, weeklyHours: 5));

        var r = await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentId, add.TargetId!.Value,
            null, Bagrut2026Summer, /* new WeeklyHours */ 11, null));

        Assert.False(r.Success);
        Assert.Equal(CommandError.WeeklyBudgetExceeded, r.Error);
    }

    [Fact]
    public async Task Update_preserves_uniqueness_invariant()
    {
        var (handler, _) = Build();
        await handler.HandleAsync(SampleAdd(
            examCode: "EX_A", track: "4U", sitting: Bagrut2026Summer, weeklyHours: 2));
        var other = await handler.HandleAsync(SampleAdd(
            examCode: "EX_A", track: "5U", sitting: Bagrut2026Summer, weeklyHours: 2));

        // Editing "other" to collide with the first target's (examCode, sitting, track) triple.
        var r = await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentId, other.TargetId!.Value,
            new TrackCode("4U"), Bagrut2026Summer, 2, null));

        Assert.False(r.Success);
        Assert.Equal(CommandError.DuplicateTarget, r.Error);
    }

    // ── Archive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_active_target_succeeds()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd());

        var r = await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, add.TargetId!.Value, ArchiveReason.Completed));

        Assert.True(r.Success);
    }

    [Fact]
    public async Task Archive_non_existent_target_returns_not_found()
    {
        var (handler, _) = Build();

        var r = await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, new ExamTargetId("et-nope"), ArchiveReason.Completed));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }

    [Fact]
    public async Task Archive_already_archived_target_is_idempotent()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd());
        await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, add.TargetId!.Value, ArchiveReason.Completed));

        var second = await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, add.TargetId.Value, ArchiveReason.StudentDeclined));

        Assert.True(second.Success); // idempotent no-op
    }

    // ── Complete ───────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_active_target_succeeds()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd());

        var r = await handler.HandleAsync(new CompleteExamTargetCommand(
            StudentId, add.TargetId!.Value));

        Assert.True(r.Success);
    }

    [Fact]
    public async Task Complete_archived_target_is_rejected()
    {
        var (handler, _) = Build();
        var add = await handler.HandleAsync(SampleAdd());
        await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, add.TargetId!.Value, ArchiveReason.StudentDeclined));

        var r = await handler.HandleAsync(new CompleteExamTargetCommand(
            StudentId, add.TargetId.Value));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetArchived, r.Error);
    }

    // ── Override ───────────────────────────────────────────────────────

    [Fact]
    public async Task Override_records_event_for_existing_target()
    {
        var (handler, store) = Build();
        var add = await handler.HandleAsync(SampleAdd());

        var r = await handler.HandleAsync(new ApplyExamTargetOverrideCommand(
            StudentId, add.TargetId!.Value, "session-1"));

        Assert.True(r.Success);
        var events = store.GetRawEvents(StudentId);
        Assert.Single(events.OfType<ExamTargetOverrideApplied_V1>());
    }

    [Fact]
    public async Task Override_unknown_target_returns_not_found()
    {
        var (handler, _) = Build();
        var r = await handler.HandleAsync(new ApplyExamTargetOverrideCommand(
            StudentId, new ExamTargetId("et-nope"), "session-1"));
        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }

    // ── IsSourceAssignmentConsistent (internal invariant) ───────────────

    [Theory]
    [InlineData(ExamTargetSource.Student, false, true)]
    [InlineData(ExamTargetSource.Student, true, false)]
    [InlineData(ExamTargetSource.Classroom, true, true)]
    [InlineData(ExamTargetSource.Classroom, false, false)]
    [InlineData(ExamTargetSource.Tenant, true, true)]
    [InlineData(ExamTargetSource.Tenant, false, false)]
    [InlineData(ExamTargetSource.Migration, true, true)]
    [InlineData(ExamTargetSource.Migration, false, true)]
    public void IsSourceAssignmentConsistent_matrix(
        ExamTargetSource source, bool hasEnrollment, bool expected)
    {
        EnrollmentId? enrollment = hasEnrollment ? new EnrollmentId("e-1") : null;
        var ok = StudentPlanCommandHandler.IsSourceAssignmentConsistent(source, enrollment);
        Assert.Equal(expected, ok);
    }
}
