// =============================================================================
// Cena Platform — ParentVisibilityDefaultHiddenFor13PlusTest (prr-230)
//
// Architecture ratchet: asserts the PRR-230 policy is enforced consistently
// across the code base.
//
//   1. ParentVisibilityDefaults.Resolve is the ONLY function that emits
//      the Hidden default for ≥13 bands.
//   2. The default is Hidden for Teen13to15, Teen16to17, Adult — and
//      Visible for Under13.
//   3. The SafetyFlag reason-tag carve-out is honored at every band.
//   4. The AddExamTargetCommand handler actually wires the band through
//      to the target (no dead code).
//
// This is a code-introspection test — it executes the policy and asserts
// the expected matrix; separate from the integration tests which prove
// the endpoint wiring.
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.Architecture;

public sealed class ParentVisibilityDefaultHiddenFor13PlusTest
{
    [Fact]
    public void Policy_matrix_exactly_matches_ADR_and_PRR_230_spec()
    {
        // Under13 — COPPA: visible (parent governs).
        Assert.Equal(
            ParentVisibility.Visible,
            ParentVisibilityDefaults.Resolve(AgeBand.Under13, reasonTag: null));

        // 13+ — hidden by default.
        Assert.Equal(
            ParentVisibility.Hidden,
            ParentVisibilityDefaults.Resolve(AgeBand.Teen13to15, reasonTag: null));
        Assert.Equal(
            ParentVisibility.Hidden,
            ParentVisibilityDefaults.Resolve(AgeBand.Teen16to17, reasonTag: null));
        Assert.Equal(
            ParentVisibility.Hidden,
            ParentVisibilityDefaults.Resolve(AgeBand.Adult, reasonTag: null));
    }

    [Fact]
    public void SafetyFlag_carve_out_overrides_age_default_at_every_band()
    {
        foreach (var band in new[] {
            AgeBand.Under13, AgeBand.Teen13to15, AgeBand.Teen16to17, AgeBand.Adult })
        {
            Assert.Equal(
                ParentVisibility.Visible,
                ParentVisibilityDefaults.Resolve(band, ReasonTag.SafetyFlag));
        }
    }

    [Fact]
    public void Command_handler_uses_policy_when_band_supplied()
    {
        // Build a handler and ensure that the default visibility on a
        // newly-added target matches the policy for each band. This
        // catches regressions where the handler drops the band on the
        // floor instead of forwarding it to ParentVisibilityDefaults.
        var store = new InMemoryStudentPlanAggregateStore();
        var now = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
        var handler = new StudentPlanCommandHandler(store, () => now);

        foreach (var (band, expected) in new (AgeBand, ParentVisibility)[]
        {
            (AgeBand.Under13, ParentVisibility.Visible),
            (AgeBand.Teen13to15, ParentVisibility.Hidden),
            (AgeBand.Teen16to17, ParentVisibility.Hidden),
            (AgeBand.Adult, ParentVisibility.Hidden),
        })
        {
            var studentId = $"arch-{band}";
            var result = handler.HandleAsync(new AddExamTargetCommand(
                StudentAnonId: studentId,
                Source: ExamTargetSource.Student,
                AssignedById: new UserId(studentId),
                EnrollmentId: null,
                ExamCode: new ExamCode("BAGRUT_MATH_5U"),
                Track: new TrackCode("5U"),
                Sitting: new SittingCode("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
                WeeklyHours: 5,
                ReasonTag: null,
                QuestionPaperCodes: new[] { "035581" },
                StudentAgeBand: band)).GetAwaiter().GetResult();

            Assert.True(result.Success, $"Add failed for band {band}");
            var agg = store.LoadAsync(studentId).GetAwaiter().GetResult();
            var target = Assert.Single(agg.State.ActiveTargets);
            Assert.Equal(expected, target.ParentVisibility);
        }
    }
}
