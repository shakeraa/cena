// =============================================================================
// MST-009 Tests: Learning frontier calculator and PSI
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class LearningFrontierTests
{
    [Fact]
    public void PSI_AllPrereqsMastered_ReturnsHigh()
    {
        var graphCache = new FakeGraphCache(prerequisites: new()
        {
            ["concept-C"] = new()
            {
                new MasteryPrerequisiteEdge("concept-A", "concept-C", 1.0f),
                new MasteryPrerequisiteEdge("concept-B", "concept-C", 1.0f)
            }
        });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["concept-A"] = new() { MasteryProbability = 0.95f, HalfLifeHours = 200f, LastInteraction = DateTimeOffset.UtcNow },
            ["concept-B"] = new() { MasteryProbability = 0.88f, HalfLifeHours = 150f, LastInteraction = DateTimeOffset.UtcNow }
        };

        var psi = PrerequisiteSatisfactionIndex.Compute("concept-C", overlay, graphCache);

        // avg(0.95, 0.88) ~ 0.915
        Assert.InRange(psi, 0.91f, 0.92f);
    }

    [Fact]
    public void PSI_MissingPrereq_DragsDown()
    {
        var graphCache = new FakeGraphCache(prerequisites: new()
        {
            ["concept-B"] = new()
            {
                new MasteryPrerequisiteEdge("concept-A", "concept-B", 1.0f)
            }
        });
        var overlay = new Dictionary<string, ConceptMasteryState>();

        var psi = PrerequisiteSatisfactionIndex.Compute("concept-B", overlay, graphCache);

        Assert.Equal(0.0f, psi);
    }

    [Fact]
    public void PSI_NoPrerequisites_ReturnsOne()
    {
        var graphCache = new FakeGraphCache();
        var overlay = new Dictionary<string, ConceptMasteryState>();

        var psi = PrerequisiteSatisfactionIndex.Compute("root-concept", overlay, graphCache);

        Assert.Equal(1.0f, psi);
    }

    [Fact]
    public void ComputeFrontier_ReadyConcepts_Included()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["basics"] = new("basics", "Basics", "math", "algebra", 1, 0.3f, 0.5f, 4),
                ["intermediate"] = new("intermediate", "Intermediate", "math", "algebra", 2, 0.5f, 0.6f, 5),
                ["advanced"] = new("advanced", "Advanced", "math", "calculus", 3, 0.8f, 0.8f, 6),
            },
            prerequisites: new()
            {
                ["intermediate"] = new() { new("basics", "intermediate", 1.0f) },
                ["advanced"] = new() { new("intermediate", "advanced", 1.0f) }
            });

        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["basics"] = new()
            {
                MasteryProbability = 0.95f, HalfLifeHours = 200f,
                LastInteraction = now.AddHours(-1), AttemptCount = 20
            },
            ["intermediate"] = new()
            {
                MasteryProbability = 0.40f, HalfLifeHours = 72f,
                LastInteraction = now.AddHours(-5), AttemptCount = 5
            }
        };

        var frontier = LearningFrontierCalculator.ComputeFrontier(overlay, graphCache, now);

        // "intermediate": PSI = mastery(basics) = 0.95 >= 0.8, mastery = 0.40 < 0.90 -> in frontier
        Assert.Contains(frontier, f => f.ConceptId == "intermediate");
        // "advanced": PSI = mastery(intermediate) = 0.40 < 0.8 -> not in frontier
        Assert.DoesNotContain(frontier, f => f.ConceptId == "advanced");
        // "basics": mastery = 0.95 >= 0.90 -> already mastered, not in frontier
        Assert.DoesNotContain(frontier, f => f.ConceptId == "basics");
    }

    [Fact]
    public void ComputeFrontier_NotStartedConceptWithReadyPrereqs_InFrontier()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["root"] = new("root", "Root", "math", "algebra", 1, 0.2f, 0.5f, 3),
                ["next"] = new("next", "Next", "math", "algebra", 2, 0.4f, 0.6f, 4),
            },
            prerequisites: new()
            {
                ["next"] = new() { new("root", "next", 1.0f) }
            });

        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["root"] = new()
            {
                MasteryProbability = 0.92f, HalfLifeHours = 200f,
                LastInteraction = now.AddHours(-1), AttemptCount = 15
            }
            // "next" not in overlay — never encountered
        };

        var frontier = LearningFrontierCalculator.ComputeFrontier(overlay, graphCache, now);

        // "next": PSI = 0.92 >= 0.8, mastery = 0.0 < 0.90 -> in frontier
        Assert.Contains(frontier, f => f.ConceptId == "next");
    }

    [Fact]
    public void ComputeFrontier_RankedByCompositeScore()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["fresh"] = new("fresh", "Fresh Concept", "math", "geometry", 1, 0.4f, 0.5f, 4),
                ["reviewed"] = new("reviewed", "Reviewed Concept", "math", "algebra", 1, 0.3f, 0.5f, 3),
            },
            prerequisites: new());

        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["fresh"] = new()
            {
                MasteryProbability = 0.20f, HalfLifeHours = 48f,
                LastInteraction = now.AddHours(-1), AttemptCount = 1
            },
            ["reviewed"] = new()
            {
                MasteryProbability = 0.60f, HalfLifeHours = 100f,
                LastInteraction = now.AddHours(-24), AttemptCount = 12
            }
        };

        var frontier = LearningFrontierCalculator.ComputeFrontier(overlay, graphCache, now);

        // "fresh" has higher information gain (fewer attempts), should rank first
        Assert.True(frontier.Count >= 2);
        Assert.Equal("fresh", frontier[0].ConceptId);
    }

    [Fact]
    public void ComputeFrontier_EmptyGraph_ReturnsEmpty()
    {
        var graphCache = new FakeGraphCache();
        var overlay = new Dictionary<string, ConceptMasteryState>();

        var frontier = LearningFrontierCalculator.ComputeFrontier(overlay, graphCache, DateTimeOffset.UtcNow);

        Assert.Empty(frontier);
    }

    [Fact]
    public void ComputeFrontier_InterleavingBonus_FavorsDifferentCluster()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["algebra1"] = new("algebra1", "Algebra 1", "math", "algebra", 1, 0.3f, 0.5f, 3),
                ["geometry1"] = new("geometry1", "Geometry 1", "math", "geometry", 1, 0.3f, 0.5f, 3),
            },
            prerequisites: new());

        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["algebra1"] = new()
            {
                MasteryProbability = 0.40f, HalfLifeHours = 72f,
                LastInteraction = now.AddHours(-2), AttemptCount = 5
            },
            ["geometry1"] = new()
            {
                MasteryProbability = 0.40f, HalfLifeHours = 72f,
                LastInteraction = now.AddHours(-2), AttemptCount = 5
            }
        };

        // Student last studied algebra — geometry should get interleaving bonus
        var frontier = LearningFrontierCalculator.ComputeFrontier(
            overlay, graphCache, now, lastTopicCluster: "algebra");

        Assert.Equal("geometry1", frontier[0].ConceptId);
    }
}
