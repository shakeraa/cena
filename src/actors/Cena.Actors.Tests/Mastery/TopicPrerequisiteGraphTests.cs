// =============================================================================
// RDY-073 Phase 1B — TopicPrerequisiteGraph tests.
// =============================================================================

using Cena.Actors.Mastery;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public class TopicPrerequisiteGraphTests
{
    [Fact]
    public void Empty_graph_treats_every_topic_as_leaf()
    {
        Assert.Equal(0.5, TopicPrerequisiteGraph.Empty.PrerequisiteUrgency("anything"));
        Assert.Equal(0, TopicPrerequisiteGraph.Empty.DirectDependentCount("anything"));
    }

    [Fact]
    public void Urgency_rises_with_dependent_count()
    {
        // algebra → { derivatives, integrals, analysis-apps }
        //   = 3 dependents → urgency 1.5
        // derivatives → { integrals }
        //   = 1 dependent  → urgency 1.0
        // integrals → {}
        //   = 0 dependents → urgency 0.5
        var graph = TopicPrerequisiteGraph.FromPrerequisites(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["derivatives"] = new[] { "algebra" },
                ["integrals"] = new[] { "algebra", "derivatives" },
                ["analysis-apps"] = new[] { "algebra" },
            });

        Assert.Equal(1.5, graph.PrerequisiteUrgency("algebra"));
        Assert.Equal(1.0, graph.PrerequisiteUrgency("derivatives"));
        Assert.Equal(0.5, graph.PrerequisiteUrgency("integrals"));
        Assert.Equal(0.5, graph.PrerequisiteUrgency("unknown-topic"));
    }

    [Fact]
    public void Self_loops_are_silently_dropped()
    {
        var graph = TopicPrerequisiteGraph.FromPrerequisites(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["algebra"] = new[] { "algebra", "foundations" },
            });
        // 'algebra' must not count itself as a dependent.
        Assert.Equal(0, graph.DirectDependentCount("algebra"));
        Assert.Equal(1, graph.DirectDependentCount("foundations"));
    }

    [Fact]
    public void Duplicate_edges_are_deduped()
    {
        var graph = TopicPrerequisiteGraph.FromPrerequisites(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["derivatives"] = new[] { "algebra", "algebra", "algebra" },
            });
        Assert.Equal(1, graph.DirectDependentCount("algebra"));
    }

    [Fact]
    public void Blank_topics_ignored()
    {
        var graph = TopicPrerequisiteGraph.FromPrerequisites(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                [""] = new[] { "algebra" },
                ["derivatives"] = new[] { "", "  ", "algebra" },
            });
        Assert.Equal(1, graph.DirectDependentCount("algebra"));
        Assert.Equal(0, graph.DirectDependentCount(""));
        Assert.Equal(0, graph.DirectDependentCount("   "));
    }
}

public class AdaptiveSchedulerWithDagTests
{
    private static AbilityEstimate EstFor(string topic, double theta)
        => new(
            StudentAnonId: "stu-anon-1",
            TopicSlug: topic,
            Theta: theta,
            StandardError: 0.2,
            SampleSize: 60,
            ComputedAtUtc: DateTimeOffset.UtcNow,
            ObservationWindowWeeks: 4);

    [Fact]
    public void Prerequisite_urgency_escalates_score_for_gating_topic()
    {
        // Without DAG: algebra-review (weight 0.10) with weakness 1.0 →
        // score 0.10. integrals (weight 0.10) with weakness 1.0 → 0.10.
        // Both are tied at Phase 1A urgency 1.0.
        // With DAG: algebra-review unblocks 3 analysis topics → urgency
        // 1.5, integrals is a leaf → urgency 0.5. algebra-review pulls
        // ahead.
        var dag = TopicPrerequisiteGraph.FromPrerequisites(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["derivatives"] = new[] { "algebra-review" },
                ["integrals"] = new[] { "algebra-review", "derivatives" },
                ["applications-of-derivatives"] = new[] { "algebra-review" },
            });

        var inputs = new SchedulerInputs(
            StudentAnonId: "stu-1",
            PerTopicEstimates: new Dictionary<string, AbilityEstimate>
            {
                ["algebra-review"] = EstFor("algebra-review", -0.5),
                ["integrals"] = EstFor("integrals", -0.5),
            },
            DeadlineUtc: DateTimeOffset.UtcNow.AddDays(90),
            WeeklyTimeBudget: TimeSpan.FromHours(5),
            MotivationProfile: MotivationProfile.Neutral,
            NowUtc: DateTimeOffset.UtcNow,
            PrerequisiteGraph: dag);

        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        Assert.Equal(2, plan.Length);
        // algebra-review should rank first thanks to 1.5× urgency.
        Assert.Equal("algebra-review", plan[0].TopicSlug);
        Assert.True(plan[0].PrerequisiteComponent > plan[1].PrerequisiteComponent);
    }

    [Fact]
    public void Null_graph_falls_back_to_empty_behaviour()
    {
        // Without a DAG argument, every topic is a leaf → urgency 0.5
        // for all. Priority ordering reduces to (weakness × weight).
        var inputs = new SchedulerInputs(
            StudentAnonId: "stu-1",
            PerTopicEstimates: new Dictionary<string, AbilityEstimate>
            {
                ["algebra-review"] = EstFor("algebra-review", -0.5),
                ["derivatives"] = EstFor("derivatives", 0.0),
            },
            DeadlineUtc: DateTimeOffset.UtcNow.AddDays(90),
            WeeklyTimeBudget: TimeSpan.FromHours(5),
            MotivationProfile: MotivationProfile.Neutral,
            NowUtc: DateTimeOffset.UtcNow);

        var plan = AdaptiveScheduler.PrioritizeTopics(inputs);
        foreach (var entry in plan)
            Assert.Equal(0.5, entry.PrerequisiteComponent);
    }
}
