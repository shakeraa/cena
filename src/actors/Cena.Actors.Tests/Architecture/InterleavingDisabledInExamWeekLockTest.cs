// =============================================================================
// Cena Platform — prr-237 architecture guard
//
// Invariant: InterleavingPolicy.Plan MUST short-circuit to
// InterleavingDisabledReason.ExamWeekLock whenever lockedForExamWeek is
// true, REGARDLESS of the rest of the inputs. This preserves the wave-2
// ActiveExamTargetPolicy behaviour — when the student is inside the 14-day
// exam-week window (ADR-0050 §10, PRR-224 shipgate rule), the session
// stays pinned to a single target. Cross-target interleaving is silently
// disabled so students in exam week see a focused plan, not a mixed one.
//
// We assert via BEHAVIOUR, not via source scanning:
//   - For a grid of input shapes (1..4 targets, different deficits,
//     different weekly-hour splits, different candidate counts),
//     InterleavingPolicy.Plan(_, lockedForExamWeek: true) must return
//     Disabled = true AND DisabledReason = ExamWeekLock.
//   - The output Entries and Allocations must be empty so callers cannot
//     accidentally render an interleaved plan when locked.
//   - SessionPlanGenerator's RunInterleavingAsync path must not call the
//     inputs provider when locked — verified via the integration test's
//     Assert.Equal(0, provider.CallCount).
//
// If a future refactor introduces a "lock-aware but still partially
// interleaved" behaviour this test fails loudly. The failure message
// names the offending input so triage is one grep away.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Actors.Sessions;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.Architecture;

public sealed class InterleavingDisabledInExamWeekLockTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Plan_LockedForExamWeek_AlwaysShortCircuits(int targetCount)
    {
        // Deliberately varied inputs — different hours, different deficits,
        // different candidate counts — to force the test to fail if any
        // input-shape branch leaks through the lock gate.
        var inputs = new List<InterleavingTargetInput>();
        for (var i = 0; i < targetCount; i++)
        {
            var candidates = Enumerable
                .Range(0, (i % 3) + 1)  // 1..3 candidates per target
                .Select(j => NewPlanEntry($"t{i}-topic{j}"))
                .ToImmutableArray();
            inputs.Add(new InterleavingTargetInput(
                TargetId: new ExamTargetId($"et-lock-{i}"),
                Candidates: candidates,
                WeeklyHours: 3 + i,             // 3,4,5,6
                MasteryDeficit: 0.2 + (i * 0.2)));   // 0.2..0.8
        }

        var result = InterleavingPolicy.Plan(
            targets: inputs,
            lockedForExamWeek: true);

        Assert.True(result.Disabled,
            $"Interleaving must be DISABLED under exam-week lock (got Disabled={result.Disabled}, " +
            $"targets={targetCount}).");
        Assert.Equal(InterleavingDisabledReason.ExamWeekLock, result.DisabledReason);
        Assert.Empty(result.Entries);
        Assert.Empty(result.Allocations);
    }

    [Fact]
    public void Plan_LockedForExamWeek_WithRichInputs_StillShortCircuits()
    {
        // Rich-input case specifically: big candidate pools, large weights,
        // small deficits — exactly the shape that would tempt a buggy
        // implementation to "just do a little interleaving" under the
        // lock. It must not.
        var inputs = new[]
        {
            new InterleavingTargetInput(
                TargetId: new ExamTargetId("et-bigA"),
                Candidates: Enumerable.Range(0, 10)
                    .Select(i => NewPlanEntry($"A-{i}")).ToImmutableArray(),
                WeeklyHours: 20,
                MasteryDeficit: 0.99),
            new InterleavingTargetInput(
                TargetId: new ExamTargetId("et-bigB"),
                Candidates: Enumerable.Range(0, 10)
                    .Select(i => NewPlanEntry($"B-{i}")).ToImmutableArray(),
                WeeklyHours: 20,
                MasteryDeficit: 0.99),
        };

        var result = InterleavingPolicy.Plan(
            targets: inputs,
            lockedForExamWeek: true,
            slotCeiling: 16);

        Assert.True(result.Disabled);
        Assert.Equal(InterleavingDisabledReason.ExamWeekLock, result.DisabledReason);
    }

    private static PlanEntry NewPlanEntry(string topicSlug)
        => new(
            TopicSlug: topicSlug,
            WeekIndex: 0,
            AllocatedTime: TimeSpan.Zero,
            PriorityScore: 1.0,
            WeaknessComponent: 0.5,
            TopicWeightComponent: 0.1,
            PrerequisiteComponent: 1.0,
            Rationale: $"practice {topicSlug}");
}
