// =============================================================================
// Cena Platform — ParentVisibility tests (prr-230)
//
// Covers:
//   1. ParentVisibilityDefaults.Resolve table (Under13, Teen13to15,
//      Teen16to17, Adult × SafetyFlag on/off).
//   2. AddExamTargetCommand honors StudentAgeBand (13+ Hidden by default).
//   3. AddExamTargetCommand SafetyFlag-tagged target is Visible at every band.
//   4. SetParentVisibilityCommand: happy path, no-op, safety-flag lock,
//      missing target, archived target.
//   5. Back-compat: ExamTargetAdded_V1 without ParentVisibility folds to
//      Visible (legacy streams behave as before).
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class ParentVisibilityTests
{
    private const string StudentId = "stu-prr230";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    private static readonly SittingCode Summer =
        new("תשפ״ו", SittingSeason.Summer, SittingMoed.A);

    private static (StudentPlanCommandHandler handler, InMemoryStudentPlanAggregateStore store) Build()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store, () => FixedNow);
        return (handler, store);
    }

    private static AddExamTargetCommand SampleAdd(
        AgeBand? band = null,
        ReasonTag? reason = null,
        int weeklyHours = 5)
        => new(
            StudentAnonId: StudentId,
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(StudentId),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: Summer,
            WeeklyHours: weeklyHours,
            ReasonTag: reason,
            QuestionPaperCodes: new[] { "035581" },
            StudentAgeBand: band);

    // ── Policy table ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(AgeBand.Under13, null, ParentVisibility.Visible)]
    [InlineData(AgeBand.Teen13to15, null, ParentVisibility.Hidden)]
    [InlineData(AgeBand.Teen16to17, null, ParentVisibility.Hidden)]
    [InlineData(AgeBand.Adult, null, ParentVisibility.Hidden)]
    // SafetyFlag carve-out at every band.
    [InlineData(AgeBand.Under13, ReasonTag.SafetyFlag, ParentVisibility.Visible)]
    [InlineData(AgeBand.Teen13to15, ReasonTag.SafetyFlag, ParentVisibility.Visible)]
    [InlineData(AgeBand.Teen16to17, ReasonTag.SafetyFlag, ParentVisibility.Visible)]
    [InlineData(AgeBand.Adult, ReasonTag.SafetyFlag, ParentVisibility.Visible)]
    // Non-safety reason tags don't change the age default.
    [InlineData(AgeBand.Teen13to15, ReasonTag.Retake, ParentVisibility.Hidden)]
    [InlineData(AgeBand.Under13, ReasonTag.Enrichment, ParentVisibility.Visible)]
    public void Defaults_resolve_per_band_and_reason(
        AgeBand band, ReasonTag? reason, ParentVisibility expected)
    {
        Assert.Equal(expected, ParentVisibilityDefaults.Resolve(band, reason));
    }

    [Theory]
    [InlineData(AgeBand.Under13, false)]
    [InlineData(AgeBand.Teen13to15, true)]
    [InlineData(AgeBand.Teen16to17, true)]
    [InlineData(AgeBand.Adult, true)]
    public void DefaultsToHiddenFor_matches_policy(AgeBand band, bool expectedHidden)
    {
        Assert.Equal(expectedHidden, ParentVisibilityDefaults.DefaultsToHiddenFor(band));
    }

    // ── Add with band drives default ────────────────────────────────────

    [Fact]
    public async Task Add_student_13plus_defaults_to_Hidden()
    {
        var (handler, store) = Build();
        var result = await handler.HandleAsync(SampleAdd(band: AgeBand.Teen13to15));
        Assert.True(result.Success);

        var agg = await store.LoadAsync(StudentId);
        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(ParentVisibility.Hidden, target.ParentVisibility);
    }

    [Fact]
    public async Task Add_student_under_13_defaults_to_Visible()
    {
        var (handler, store) = Build();
        var result = await handler.HandleAsync(SampleAdd(band: AgeBand.Under13));
        Assert.True(result.Success);

        var agg = await store.LoadAsync(StudentId);
        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(ParentVisibility.Visible, target.ParentVisibility);
    }

    [Fact]
    public async Task Add_safety_flag_target_is_Visible_even_at_16to17()
    {
        var (handler, store) = Build();
        var result = await handler.HandleAsync(
            SampleAdd(band: AgeBand.Teen16to17, reason: ReasonTag.SafetyFlag));
        Assert.True(result.Success);

        var agg = await store.LoadAsync(StudentId);
        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(ParentVisibility.Visible, target.ParentVisibility);
        Assert.True(target.IsSafetyFlagged);
    }

    [Fact]
    public async Task Add_no_band_supplied_falls_back_to_Visible_backcompat()
    {
        var (handler, store) = Build();
        var result = await handler.HandleAsync(SampleAdd(band: null));
        Assert.True(result.Success);

        var agg = await store.LoadAsync(StudentId);
        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(ParentVisibility.Visible, target.ParentVisibility);
    }

    // ── Toggle command ──────────────────────────────────────────────────

    [Fact]
    public async Task SetVisibility_student_can_opt_in_after_hidden_default()
    {
        var (handler, store) = Build();
        var addResult = await handler.HandleAsync(SampleAdd(band: AgeBand.Teen13to15));
        var targetId = addResult.TargetId!.Value;

        var setResult = await handler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: StudentId,
            TargetId: targetId,
            Visibility: ParentVisibility.Visible,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: StudentId,
            Reason: "student-opt-in"));

        Assert.True(setResult.Success);

        var agg = await store.LoadAsync(StudentId);
        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(ParentVisibility.Visible, target.ParentVisibility);
    }

    [Fact]
    public async Task SetVisibility_noop_when_already_at_requested_value()
    {
        var (handler, store) = Build();
        // Start Visible (no band = default Visible).
        var addResult = await handler.HandleAsync(SampleAdd(band: null));
        var targetId = addResult.TargetId!.Value;

        // Count events after add.
        var beforeCount = (await store.LoadAsync(StudentId)).State.Targets.Count;

        var setResult = await handler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: StudentId,
            TargetId: targetId,
            Visibility: ParentVisibility.Visible,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: StudentId,
            Reason: "noop"));

        Assert.True(setResult.Success);
        // State unchanged.
        var agg = await store.LoadAsync(StudentId);
        Assert.Equal(beforeCount, agg.State.Targets.Count);
        Assert.Equal(ParentVisibility.Visible, agg.State.ActiveTargets[0].ParentVisibility);
    }

    [Fact]
    public async Task SetVisibility_rejects_Hidden_on_safety_flagged_target()
    {
        var (handler, _) = Build();
        var addResult = await handler.HandleAsync(
            SampleAdd(band: AgeBand.Teen16to17, reason: ReasonTag.SafetyFlag));
        var targetId = addResult.TargetId!.Value;

        var setResult = await handler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: StudentId,
            TargetId: targetId,
            Visibility: ParentVisibility.Hidden,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: StudentId,
            Reason: "try-to-hide-safety"));

        Assert.False(setResult.Success);
        Assert.Equal(CommandError.ParentVisibilitySafetyFlagLocked, setResult.Error);
    }

    [Fact]
    public async Task SetVisibility_allows_resetting_safety_to_Visible_noop()
    {
        var (handler, _) = Build();
        var addResult = await handler.HandleAsync(
            SampleAdd(band: AgeBand.Teen16to17, reason: ReasonTag.SafetyFlag));
        var targetId = addResult.TargetId!.Value;

        var setResult = await handler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: StudentId,
            TargetId: targetId,
            Visibility: ParentVisibility.Visible,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: StudentId,
            Reason: "reset"));

        Assert.True(setResult.Success);
    }

    [Fact]
    public async Task SetVisibility_missing_target_returns_TargetNotFound()
    {
        var (handler, _) = Build();
        var result = await handler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: StudentId,
            TargetId: new ExamTargetId("et-does-not-exist"),
            Visibility: ParentVisibility.Hidden,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: StudentId,
            Reason: "missing"));

        Assert.False(result.Success);
        Assert.Equal(CommandError.TargetNotFound, result.Error);
    }

    [Fact]
    public async Task SetVisibility_archived_target_returns_TargetArchived()
    {
        var (handler, _) = Build();
        var addResult = await handler.HandleAsync(SampleAdd(band: AgeBand.Teen13to15));
        var targetId = addResult.TargetId!.Value;

        // Archive the target.
        await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, targetId, ArchiveReason.StudentDeclined));

        var setResult = await handler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: StudentId,
            TargetId: targetId,
            Visibility: ParentVisibility.Visible,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: StudentId,
            Reason: "after-archive"));

        Assert.False(setResult.Success);
        Assert.Equal(CommandError.TargetArchived, setResult.Error);
    }

    // ── Back-compat: legacy replay ─────────────────────────────────────

    [Fact]
    public void Legacy_ExamTargetAdded_event_folds_to_Visible()
    {
        // Build a target without specifying ParentVisibility — old events
        // on existing streams did not carry the field. The default in the
        // record ctor is Visible, which is what the fold should see.
        var legacyTarget = new ExamTarget(
            Id: ExamTargetId.New(),
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(StudentId),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            QuestionPaperCodes: new[] { "035581" },
            Sitting: Summer,
            PerPaperSittingOverride: null,
            WeeklyHours: 5,
            ReasonTag: null,
            CreatedAt: FixedNow,
            ArchivedAt: null);

        Assert.Equal(ParentVisibility.Visible, legacyTarget.ParentVisibility);

        var agg = new StudentPlanAggregate();
        agg.Apply(new ExamTargetAdded_V1(StudentId, legacyTarget, FixedNow));

        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(ParentVisibility.Visible, target.ParentVisibility);
    }

    [Fact]
    public async Task Update_preserves_ParentVisibility()
    {
        var (handler, store) = Build();
        var addResult = await handler.HandleAsync(SampleAdd(band: AgeBand.Teen13to15));
        var targetId = addResult.TargetId!.Value;

        // Update weekly hours.
        await handler.HandleAsync(new UpdateExamTargetCommand(
            StudentAnonId: StudentId,
            TargetId: targetId,
            Track: new TrackCode("5U"),
            Sitting: Summer,
            WeeklyHours: 8,
            ReasonTag: null));

        var agg = await store.LoadAsync(StudentId);
        var target = Assert.Single(agg.State.ActiveTargets);
        Assert.Equal(8, target.WeeklyHours);
        // Hidden must still be Hidden — Update does NOT emit a Visibility event.
        Assert.Equal(ParentVisibility.Hidden, target.ParentVisibility);
    }
}
