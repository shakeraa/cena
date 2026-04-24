// =============================================================================
// MST-008 Tests: Review priority calculator
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class ReviewPriorityCalculatorTests
{
    [Fact]
    public void ComputePriority_FoundationalConceptWithDecay_HighPriority()
    {
        var priority = ReviewPriorityCalculator.ComputePriority(
            recallProbability: 0.50f, descendantCount: 15);

        // (0.85 - 0.50) * (1 + log2(15)) = 0.35 * (1 + 3.91) = 0.35 * 4.91 ~ 1.72
        Assert.InRange(priority, 1.70f, 1.74f);
    }

    [Fact]
    public void ComputePriority_LeafConceptWithDecay_LowerPriority()
    {
        var priority = ReviewPriorityCalculator.ComputePriority(
            recallProbability: 0.50f, descendantCount: 0);

        // (0.85 - 0.50) * (1 + log2(1)) = 0.35 * (1 + 0) = 0.35
        Assert.InRange(priority, 0.34f, 0.36f);
    }

    [Fact]
    public void ComputePriority_NoDecay_ReturnsZero()
    {
        var priority = ReviewPriorityCalculator.ComputePriority(
            recallProbability: 0.90f, descendantCount: 10);

        Assert.Equal(0.0f, priority);
    }

    [Fact]
    public void ComputePriority_AtThreshold_ReturnsZero()
    {
        var priority = ReviewPriorityCalculator.ComputePriority(
            recallProbability: 0.85f, descendantCount: 10);

        Assert.Equal(0.0f, priority);
    }

    [Fact]
    public void RankReviewConcepts_OrdersByPriorityDescending()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["algebra-basics"] = new()
            {
                MasteryProbability = 0.92f, HalfLifeHours = 72f,
                LastInteraction = now.AddDays(-7)
            },
            ["quadratic-formula"] = new()
            {
                MasteryProbability = 0.88f, HalfLifeHours = 120f,
                LastInteraction = now.AddDays(-5)
            },
            ["calculus-intro"] = new()
            {
                MasteryProbability = 0.30f, HalfLifeHours = 48f,
                LastInteraction = now.AddDays(-3)
            } // low mastery — should be excluded
        };

        var graphCache = new FakeGraphCache(descendantCounts: new Dictionary<string, int>
        {
            ["algebra-basics"] = 20,
            ["quadratic-formula"] = 3,
            ["calculus-intro"] = 0
        });

        var ranked = ReviewPriorityCalculator.RankReviewConcepts(overlay, graphCache, now);

        Assert.True(ranked.Count >= 1);
        Assert.Equal("algebra-basics", ranked[0].ConceptId);
        Assert.DoesNotContain(ranked, r => r.ConceptId == "calculus-intro");
    }

    [Fact]
    public void RankReviewConcepts_RecentlyPracticed_Excluded()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["fresh"] = new()
            {
                MasteryProbability = 0.90f, HalfLifeHours = 168f,
                LastInteraction = now.AddMinutes(-5) // just practiced
            }
        };
        var graphCache = new FakeGraphCache();

        var ranked = ReviewPriorityCalculator.RankReviewConcepts(overlay, graphCache, now);

        Assert.Empty(ranked); // recall ~1.0 >= 0.85
    }

    [Fact]
    public void RankReviewConcepts_MaxResults_Respected()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>();
        for (int i = 0; i < 20; i++)
        {
            overlay[$"concept-{i}"] = new()
            {
                MasteryProbability = 0.90f,
                HalfLifeHours = 48f,
                LastInteraction = now.AddDays(-14)
            };
        }
        var graphCache = new FakeGraphCache();

        var ranked = ReviewPriorityCalculator.RankReviewConcepts(overlay, graphCache, now, maxResults: 5);

        Assert.Equal(5, ranked.Count);
    }
}
