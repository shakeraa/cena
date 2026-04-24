// =============================================================================
// Cena Platform — QuestionPaperAggregate tests (prr-243, ADR-0050 §1)
//
// Covers the four PRR-243 event-fold branches on StudentPlanState:
//   - QuestionPaperAdded_V1
//   - QuestionPaperRemoved_V1
//   - PerPaperSittingOverrideSet_V1
//   - PerPaperSittingOverrideCleared_V1
//
// Also covers deterministic replay through StudentPlanAggregate.ReplayFrom
// so a subscriber that boots cold sees identical state to one that ate
// the events live.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class QuestionPaperAggregateTests
{
    private const string StudentId = "stu-paper-1";
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
    private static readonly SittingCode Summer = new("תשפ״ו", SittingSeason.Summer, SittingMoed.A);
    private static readonly SittingCode Winter = new("תשפ״ו", SittingSeason.Winter, SittingMoed.A);
    private static readonly SittingCode SummerB = new("תשפ״ו", SittingSeason.Summer, SittingMoed.B);

    private static ExamTarget MakeBagrutTarget(params string[] papers) => new(
        Id: new ExamTargetId("et-1"),
        Source: ExamTargetSource.Student,
        AssignedById: new UserId(StudentId),
        EnrollmentId: null,
        ExamCode: new ExamCode("BAGRUT_MATH_5U"),
        Track: new TrackCode("5U"),
        QuestionPaperCodes: papers,
        Sitting: Summer,
        PerPaperSittingOverride: null,
        WeeklyHours: 5,
        ReasonTag: null,
        CreatedAt: T0,
        ArchivedAt: null);

    // ── QuestionPaperAdded_V1 fold ───────────────────────────────────────

    [Fact]
    public void Apply_QuestionPaperAdded_appends_code_to_target()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581"), T0));

        agg.Apply(new QuestionPaperAdded_V1(
            StudentId, new ExamTargetId("et-1"), "035582", null, T0.AddMinutes(1)));

        var target = agg.State.Targets[0];
        Assert.Equal(new[] { "035581", "035582" }, target.QuestionPaperCodes);
        Assert.Null(target.PerPaperSittingOverride);
    }

    [Fact]
    public void Apply_QuestionPaperAdded_with_sitting_override_sets_map_entry()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581"), T0));

        agg.Apply(new QuestionPaperAdded_V1(
            StudentId, new ExamTargetId("et-1"), "035582", Winter, T0.AddMinutes(1)));

        var target = agg.State.Targets[0];
        Assert.NotNull(target.PerPaperSittingOverride);
        Assert.Equal(Winter, target.PerPaperSittingOverride!["035582"]);
    }

    [Fact]
    public void Apply_QuestionPaperAdded_is_idempotent_for_duplicates()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581"), T0));

        agg.Apply(new QuestionPaperAdded_V1(
            StudentId, new ExamTargetId("et-1"), "035581", null, T0.AddMinutes(1)));

        // Duplicate fold: still only one entry.
        Assert.Single(agg.State.Targets[0].QuestionPaperCodes);
    }

    [Fact]
    public void Apply_QuestionPaperAdded_ignores_archived_target()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581"), T0));
        agg.Apply(new ExamTargetArchived_V1(
            StudentId, new ExamTargetId("et-1"), T0.AddDays(1), ArchiveReason.Completed));

        agg.Apply(new QuestionPaperAdded_V1(
            StudentId, new ExamTargetId("et-1"), "035582", null, T0.AddDays(2)));

        // Archived — append silently skipped.
        Assert.Single(agg.State.Targets[0].QuestionPaperCodes);
    }

    // ── QuestionPaperRemoved_V1 fold ─────────────────────────────────────

    [Fact]
    public void Apply_QuestionPaperRemoved_strips_code_and_keeps_others()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(
            StudentId, MakeBagrutTarget("035581", "035582", "035583"), T0));

        agg.Apply(new QuestionPaperRemoved_V1(
            StudentId, new ExamTargetId("et-1"), "035582", T0.AddMinutes(1)));

        Assert.Equal(new[] { "035581", "035583" }, agg.State.Targets[0].QuestionPaperCodes);
    }

    [Fact]
    public void Apply_QuestionPaperRemoved_clears_matching_override_entry()
    {
        var target = MakeBagrutTarget("035581", "035582") with
        {
            PerPaperSittingOverride = new Dictionary<string, SittingCode>
            {
                ["035582"] = Winter,
            },
        };
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, T0));

        agg.Apply(new QuestionPaperRemoved_V1(
            StudentId, new ExamTargetId("et-1"), "035582", T0.AddMinutes(1)));

        // Override map is normalised back to null when emptied.
        Assert.Null(agg.State.Targets[0].PerPaperSittingOverride);
    }

    [Fact]
    public void Apply_QuestionPaperRemoved_preserves_other_overrides()
    {
        var target = MakeBagrutTarget("035581", "035582", "035583") with
        {
            PerPaperSittingOverride = new Dictionary<string, SittingCode>
            {
                ["035582"] = Winter,
                ["035583"] = SummerB,
            },
        };
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, T0));

        agg.Apply(new QuestionPaperRemoved_V1(
            StudentId, new ExamTargetId("et-1"), "035582", T0.AddMinutes(1)));

        var overrides = agg.State.Targets[0].PerPaperSittingOverride!;
        Assert.Single(overrides);
        Assert.Equal(SummerB, overrides["035583"]);
    }

    // ── PerPaperSittingOverrideSet_V1 fold ───────────────────────────────

    [Fact]
    public void Apply_PerPaperSittingOverrideSet_creates_entry()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581", "035582"), T0));

        agg.Apply(new PerPaperSittingOverrideSet_V1(
            StudentId, new ExamTargetId("et-1"), "035582", Winter, T0.AddMinutes(1)));

        var overrides = agg.State.Targets[0].PerPaperSittingOverride!;
        Assert.Equal(Winter, overrides["035582"]);
    }

    [Fact]
    public void Apply_PerPaperSittingOverrideSet_replaces_existing_entry()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581", "035582"), T0));
        agg.Apply(new PerPaperSittingOverrideSet_V1(
            StudentId, new ExamTargetId("et-1"), "035582", Winter, T0.AddMinutes(1)));

        agg.Apply(new PerPaperSittingOverrideSet_V1(
            StudentId, new ExamTargetId("et-1"), "035582", SummerB, T0.AddMinutes(2)));

        Assert.Equal(SummerB, agg.State.Targets[0].PerPaperSittingOverride!["035582"]);
    }

    [Fact]
    public void Apply_PerPaperSittingOverrideSet_ignores_unknown_paper_code()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581"), T0));

        agg.Apply(new PerPaperSittingOverrideSet_V1(
            StudentId, new ExamTargetId("et-1"), "035999", Winter, T0.AddMinutes(1)));

        Assert.Null(agg.State.Targets[0].PerPaperSittingOverride);
    }

    // ── PerPaperSittingOverrideCleared_V1 fold ───────────────────────────

    [Fact]
    public void Apply_PerPaperSittingOverrideCleared_removes_entry()
    {
        var target = MakeBagrutTarget("035581", "035582") with
        {
            PerPaperSittingOverride = new Dictionary<string, SittingCode>
            {
                ["035582"] = Winter,
            },
        };
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, target, T0));

        agg.Apply(new PerPaperSittingOverrideCleared_V1(
            StudentId, new ExamTargetId("et-1"), "035582", T0.AddMinutes(1)));

        Assert.Null(agg.State.Targets[0].PerPaperSittingOverride);
    }

    [Fact]
    public void Apply_PerPaperSittingOverrideCleared_is_idempotent_on_missing_key()
    {
        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, MakeBagrutTarget("035581"), T0));

        // Clearing an override that was never set: no-op.
        agg.Apply(new PerPaperSittingOverrideCleared_V1(
            StudentId, new ExamTargetId("et-1"), "035581", T0.AddMinutes(1)));

        Assert.Null(agg.State.Targets[0].PerPaperSittingOverride);
    }

    // ── Replay determinism ─────────────────────────────────────────────

    [Fact]
    public void Replay_produces_identical_state_to_live_fold()
    {
        var target = MakeBagrutTarget("035581");
        var events = new object[]
        {
            new StudentPlanInitialized_V1(StudentId, T0),
            new ExamTargetAdded_V1(StudentId, target, T0.AddMinutes(1)),
            new QuestionPaperAdded_V1(StudentId, target.Id, "035582", Winter, T0.AddMinutes(2)),
            new QuestionPaperAdded_V1(StudentId, target.Id, "035583", null, T0.AddMinutes(3)),
            new PerPaperSittingOverrideSet_V1(StudentId, target.Id, "035583", SummerB, T0.AddMinutes(4)),
            new QuestionPaperRemoved_V1(StudentId, target.Id, "035582", T0.AddMinutes(5)),
        };

        var live = new StudentPlanAggregate();
        foreach (var e in events) live.Apply(e);

        var replayed = StudentPlanAggregate.ReplayFrom(events);

        Assert.Equal(live.State.Targets[0].QuestionPaperCodes, replayed.State.Targets[0].QuestionPaperCodes);
        Assert.Equal(
            (IReadOnlyDictionary<string, SittingCode>?)live.State.Targets[0].PerPaperSittingOverride,
            (IReadOnlyDictionary<string, SittingCode>?)replayed.State.Targets[0].PerPaperSittingOverride);
    }
}
