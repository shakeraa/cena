// =============================================================================
// RDY-073 Phase 1A — Compression-diagnostic domain tests.
//
// Covers:
//   * BagrutTopicWeights 5-unit table sums to ~1.0
//   * Every weight row has a non-empty citation
//   * Weights flagged ExpertJudgment surface in the scheduler rationale
//   * DiagnosticRun lifecycle transitions + HasSufficientEvidence gate
//   * AdaptiveScheduler prioritises by (weakness × topic-weight), honours
//     motivation-safe framing rules per profile
//   * Scheduler rationale strings never contain the banned RDY-071
//     forward-extrapolation phrases (shipgate cross-check)
// =============================================================================

using Cena.Actors.Mastery;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public class BagrutTopicWeightsTests
{
    [Fact]
    public void FiveUnit_weights_sum_to_one()
    {
        var total = BagrutTopicWeights.TotalWeight(BagrutTopicWeights.FiveUnit);
        Assert.InRange(total, 0.999, 1.001);
    }

    [Fact]
    public void Every_FiveUnit_row_has_a_citation()
    {
        foreach (var w in BagrutTopicWeights.FiveUnit)
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Citation),
                $"Topic {w.TopicSlug} is missing a citation; Dr. Rami's "
                + "Round 4 demand is that every weight cites its source.");
        }
    }

    [Fact]
    public void ForFiveUnit_returns_null_for_unknown_topic()
    {
        Assert.Null(BagrutTopicWeights.ForFiveUnit("quantum-mechanics"));
    }

    [Fact]
    public void ForFiveUnit_lookup_matches_known_topic()
    {
        var w = BagrutTopicWeights.ForFiveUnit("derivatives");
        Assert.NotNull(w);
        Assert.Equal("derivatives", w!.TopicSlug);
        Assert.True(w.Weight > 0);
    }
}

public class DiagnosticRunTests
{
    private static DiagnosticRun NewRun() => new(
        runId: "run-1",
        studentAnonId: "stu-anon-1",
        priorSource: ColdStartPriorSource.SyllabusWeighted,
        studentDeadlineUtc: DateTimeOffset.UtcNow.AddDays(90),
        startedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void Constructor_starts_in_progress()
    {
        var r = NewRun();
        Assert.Equal(DiagnosticRunStatus.InProgress, r.Status);
    }

    [Fact]
    public void Complete_transitions_to_completed()
    {
        var r = NewRun();
        r.Complete(DateTimeOffset.UtcNow);
        Assert.Equal(DiagnosticRunStatus.Completed, r.Status);
        Assert.NotNull(r.CompletedAtUtc);
    }

    [Fact]
    public void Abort_transitions_to_aborted_with_reason()
    {
        var r = NewRun();
        r.Abort("student quit", DateTimeOffset.UtcNow);
        Assert.Equal(DiagnosticRunStatus.Aborted, r.Status);
        Assert.Equal("student quit", r.AbortReason);
    }

    [Fact]
    public void Cannot_record_attempt_after_completion()
    {
        var r = NewRun();
        r.Complete(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            r.RecordAttempt(new DiagnosticItemAttempt(
                ItemId: "item-1",
                TopicSlug: "algebra-review",
                IsCorrect: true,
                TimeSpent: TimeSpan.FromSeconds(90),
                ItemDifficulty: 0.0,
                ItemDiscrimination: 1.0,
                AttemptedAtUtc: DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void HasSufficientEvidence_false_with_few_checkpoints()
    {
        var r = NewRun();
        r.RecordCheckpoint(new DiagnosticCheckpoint(
            "derivatives", 0.3, 0.2, 8, DateTimeOffset.UtcNow));
        Assert.False(r.HasSufficientEvidence());
    }

    [Fact]
    public void HasSufficientEvidence_true_when_most_checkpoints_within_target_SE()
    {
        var r = NewRun();
        // 7 checkpoints, 6 within SE target 0.3, 1 above
        for (var i = 0; i < 6; i++)
        {
            r.RecordCheckpoint(new DiagnosticCheckpoint(
                TopicSlug: $"topic-{i}",
                Theta: 0.0,
                StandardError: 0.25,
                SampleSize: 10,
                CheckpointAtUtc: DateTimeOffset.UtcNow));
        }
        r.RecordCheckpoint(new DiagnosticCheckpoint(
            TopicSlug: "topic-6",
            Theta: 0.0,
            StandardError: 0.5,   // above target
            SampleSize: 10,
            CheckpointAtUtc: DateTimeOffset.UtcNow));
        // 6 of 7 well-estimated → 85.7% ≥ 70% threshold
        Assert.True(r.HasSufficientEvidence());
    }
}

public class AdaptiveSchedulerTests
{
    private static AbilityEstimate EstFor(string topic, double theta, int n = 60)
        => new(
            StudentAnonId: "stu-anon-1",
            TopicSlug: topic,
            Theta: theta,
            StandardError: 0.2,
            SampleSize: n,
            ComputedAtUtc: DateTimeOffset.UtcNow,
            ObservationWindowWeeks: 4);

    private static SchedulerInputs NewInputs(MotivationProfile profile, params (string topic, double theta)[] topics)
    {
        var dict = topics.ToDictionary(t => t.topic, t => EstFor(t.topic, t.theta));
        return new SchedulerInputs(
            StudentAnonId: "stu-anon-1",
            PerTopicEstimates: dict,
            DeadlineUtc: DateTimeOffset.UtcNow.AddDays(90),
            WeeklyTimeBudget: TimeSpan.FromHours(5),
            MotivationProfile: profile,
            NowUtc: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Prioritize_orders_by_weakness_times_weight()
    {
        // derivatives (weight 0.13) weakness 0.5
        //   → score 0.5 × 0.13 × 1.0 = 0.065
        // algebra-review (weight 0.10) weakness 1.0
        //   → score 1.0 × 0.10 × 1.0 = 0.10
        // algebra-review should outrank derivatives despite smaller
        // weight because the weakness gap is 2× bigger.
        var inputs = NewInputs(
            MotivationProfile.Neutral,
            ("derivatives", 0.0),
            ("algebra-review", -0.5));
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        Assert.Equal(2, plan.Length);
        Assert.Equal("algebra-review", plan[0].TopicSlug);
        Assert.Equal("derivatives", plan[1].TopicSlug);
    }

    [Fact]
    public void Unknown_topic_is_skipped_not_defaulted()
    {
        var inputs = NewInputs(
            MotivationProfile.Neutral,
            ("derivatives", 0.0),
            ("quantum-mechanics", -2.0)); // not in BagrutTopicWeights
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        Assert.Single(plan);
        Assert.Equal("derivatives", plan[0].TopicSlug);
    }

    [Fact]
    public void At_or_above_mastery_target_yields_zero_weakness_score()
    {
        var inputs = NewInputs(
            MotivationProfile.Neutral,
            ("derivatives", 1.0)); // theta 1.0 > target 0.5
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        Assert.Single(plan);
        Assert.Equal(0.0, plan[0].PriorityScore);
    }

    [Fact]
    public void Rationale_framing_is_anxious_safe_for_anxious_profile()
    {
        var inputs = NewInputs(MotivationProfile.Anxious, ("derivatives", 0.0));
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        var rationale = plan[0].Rationale;
        // Anxious framing: strengths-forward opener, never
        // percentage-forward weakness naming.
        Assert.Contains("here's where we'll start", rationale, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gap", rationale, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weakness", rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rationale_framing_is_percentage_forward_for_confident_profile()
    {
        var inputs = NewInputs(MotivationProfile.Confident, ("derivatives", 0.0));
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        var rationale = plan[0].Rationale;
        Assert.Contains("gap", rationale);
        Assert.Contains("%", rationale);
    }

    [Fact]
    public void Rationale_for_expert_judgment_weight_flags_the_source()
    {
        var inputs = NewInputs(MotivationProfile.Neutral, ("derivatives", 0.0));
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        // Neutral profile + ExpertJudgment source emits a pending-Ministry flag.
        Assert.Contains("expert-judgment", plan[0].Rationale);
    }

    [Theory]
    [InlineData(MotivationProfile.Neutral)]
    [InlineData(MotivationProfile.Anxious)]
    [InlineData(MotivationProfile.Confident)]
    public void Rationale_never_contains_RDY_071_banned_phrases(MotivationProfile profile)
    {
        var inputs = NewInputs(profile,
            ("algebra-review", -1.0),
            ("derivatives", 0.0),
            ("integrals", 0.3));
        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        foreach (var e in plan)
        {
            Assert.DoesNotContain("predicted bagrut", e.Rationale, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("your bagrut score", e.Rationale, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("will score", e.Rationale, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("grade prediction", e.Rationale, StringComparison.OrdinalIgnoreCase);
        }
    }
}
