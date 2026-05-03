// =============================================================================
// Cena Platform — IfThenPlanner unit tests (prr-154 / F2)
//
// Covers plan-count sizing, plan selection, trigger lookup, and the
// defensive contracts. Everything is deterministic on inputs so no mocks
// are required.
// =============================================================================

using System;
using System.Linq;
using Cena.Actors.Pedagogy;
using Xunit;

namespace Cena.Actors.Tests.Pedagogy;

public sealed class IfThenPlannerTests
{
    private static readonly IfThenPlannerContext ShortSession =
        new(QuestionsPlanned: 5, EstimatedMinutes: 10, HasPreExamWindow: false);

    private static readonly IfThenPlannerContext MediumSession =
        new(QuestionsPlanned: 12, EstimatedMinutes: 20, HasPreExamWindow: false);

    private static readonly IfThenPlannerContext LongSession =
        new(QuestionsPlanned: 20, EstimatedMinutes: 45, HasPreExamWindow: false);

    private static readonly IfThenPlannerContext PreExamSession =
        new(QuestionsPlanned: 20, EstimatedMinutes: 45, HasPreExamWindow: true);

    // ── Plan count sizing (Gollwitzer 2014: 2–4) ──────────────────────────

    [Fact]
    public void ShortSession_GetsTwoPlans()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(ShortSession);
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public void MediumSession_GetsThreePlans()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(MediumSession);
        Assert.Equal(3, plans.Count);
    }

    [Fact]
    public void LongSession_GetsFourPlans_NeverMore()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);
        Assert.Equal(IfThenPlanner.MaxPlansPerSession, plans.Count);
    }

    [Fact]
    public void VeryLongSession_IsCappedAtFour()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(new IfThenPlannerContext(
            QuestionsPlanned: 100,
            EstimatedMinutes: 240,
            HasPreExamWindow: false));
        Assert.Equal(IfThenPlanner.MaxPlansPerSession, plans.Count);
    }

    // ── Plan selection ─────────────────────────────────────────────────────

    [Fact]
    public void AllSessions_IncludeFoundationalCheckWorkPlan()
    {
        var sut = new IfThenPlanner();

        foreach (var ctx in new[] { ShortSession, MediumSession, LongSession, PreExamSession })
        {
            var plans = sut.BuildSessionPlans(ctx);
            Assert.Contains(plans, p =>
                p.Trigger == IfThenTrigger.FinishedProblemCorrect &&
                p.Action == IfThenAction.CheckWorkBeforeMoving);
        }
    }

    [Fact]
    public void MediumSession_IncludesStuckRecoveryPlan()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(MediumSession);
        Assert.Contains(plans, p =>
            p.Trigger == IfThenTrigger.StuckOnCurrent &&
            p.Action == IfThenAction.RequestHintAfterOneMinute);
    }

    [Fact]
    public void NonPreExamLongSession_UsesShortBreakAtBlockBoundary()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);
        Assert.Contains(plans, p =>
            p.Trigger == IfThenTrigger.FinishedBlock &&
            p.Action == IfThenAction.TakeShortBreak);
    }

    [Fact]
    public void PreExamLongSession_UsesWarmUpAtBlockBoundary_NotBreak()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(PreExamSession);
        Assert.Contains(plans, p =>
            p.Trigger == IfThenTrigger.FinishedBlock &&
            p.Action == IfThenAction.SwitchToWarmUp);
        Assert.DoesNotContain(plans, p =>
            p.Trigger == IfThenTrigger.FinishedBlock &&
            p.Action == IfThenAction.TakeShortBreak);
    }

    [Fact]
    public void LongSession_IncludesSessionStartPreview()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);
        Assert.Contains(plans, p =>
            p.Trigger == IfThenTrigger.SessionStart &&
            p.Action == IfThenAction.PreviewTopicList);
    }

    [Fact]
    public void ShortSession_DoesNotIncludeBlockBoundaryPlan()
    {
        // Short sessions only get the two highest-evidence plans.
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(ShortSession);
        Assert.DoesNotContain(plans, p => p.Trigger == IfThenTrigger.FinishedBlock);
    }

    // ── Determinism ────────────────────────────────────────────────────────

    [Fact]
    public void BuildSessionPlans_IsDeterministic()
    {
        var sut = new IfThenPlanner();
        var a = sut.BuildSessionPlans(LongSession);
        var b = sut.BuildSessionPlans(LongSession);

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Trigger, b[i].Trigger);
            Assert.Equal(a[i].Action, b[i].Action);
            Assert.Equal(a[i].PreviewCopy, b[i].PreviewCopy);
        }
    }

    // ── Preview lookup ─────────────────────────────────────────────────────

    [Fact]
    public void PreviewFor_ReturnsMatchingPlan()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);

        var preview = sut.PreviewFor(IfThenTrigger.SessionStart, plans);

        Assert.NotNull(preview);
        Assert.Equal(IfThenTrigger.SessionStart, preview!.Trigger);
        Assert.Equal(IfThenAction.PreviewTopicList, preview.Action);
    }

    [Fact]
    public void PreviewFor_ReturnsNull_WhenTriggerNotInPlanSet()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(ShortSession);

        // Short session has no block-boundary plan.
        var preview = sut.PreviewFor(IfThenTrigger.FinishedBlock, plans);
        Assert.Null(preview);
    }

    // ── Defensive contracts ────────────────────────────────────────────────

    [Fact]
    public void BuildSessionPlans_ThrowsOnNullContext()
    {
        var sut = new IfThenPlanner();
        Assert.Throws<ArgumentNullException>(() => sut.BuildSessionPlans(null!));
    }

    [Fact]
    public void PreviewFor_ThrowsOnNullPlans()
    {
        var sut = new IfThenPlanner();
        Assert.Throws<ArgumentNullException>(() =>
            sut.PreviewFor(IfThenTrigger.SessionStart, null!));
    }

    // ── Copy safety (no dark-pattern / therapeutic phrasing) ──────────────

    [Fact]
    public void AllPreviewCopy_AvoidsDarkPatternTerms()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);

        // Banned terms from ADR-0048 / ADR-0054 / shipgate rulepacks.
        string[] bannedSubstrings =
        {
            "streak", "don't break", "don't lose", "keep your streak",
            "you'll lose", "hurry", "don't fail",
            // therapeutic-claim moves (prr-073 scanner)
            "i understand your", "your anxiety", "your stress", "overwhelmed",
        };

        foreach (var plan in plans)
        {
            foreach (var banned in bannedSubstrings)
            {
                Assert.DoesNotContain(
                    banned,
                    plan.PreviewCopy,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AllPreviewCopy_MatchesIfThenShape()
    {
        // Implementation-intentions are defined by the "if…then…" grammar
        // (Gollwitzer 1999). Enforce the shape at runtime so future edits
        // don't accidentally ship plain-goal copy.
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);

        foreach (var plan in plans)
        {
            Assert.StartsWith("If ", plan.PreviewCopy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(", then ", plan.PreviewCopy, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AllPreviewCopy_IsNonEmpty()
    {
        var sut = new IfThenPlanner();
        var plans = sut.BuildSessionPlans(LongSession);
        Assert.All(plans, p => Assert.False(string.IsNullOrWhiteSpace(p.PreviewCopy)));
    }
}
