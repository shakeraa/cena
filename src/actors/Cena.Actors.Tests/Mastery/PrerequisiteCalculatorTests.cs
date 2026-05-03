// =============================================================================
// MST-004 Tests: Prerequisite support calculator
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class PrerequisiteCalculatorTests
{
    [Fact]
    public void ComputeSupport_NoPrerequisites_ReturnsOne()
    {
        var graphCache = new FakeGraphCache();
        var overlay = new Dictionary<string, ConceptMasteryState>();

        var support = PrerequisiteCalculator.ComputeSupport("concept-A", overlay, graphCache);

        Assert.Equal(1.0f, support);
    }

    [Fact]
    public void ComputeSupport_AllPrereqsMastered_ReturnsMinMastery()
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
            ["concept-A"] = new() { MasteryProbability = 0.95f },
            ["concept-B"] = new() { MasteryProbability = 0.80f }
        };

        var support = PrerequisiteCalculator.ComputeSupport("concept-C", overlay, graphCache);

        Assert.Equal(0.80f, support);
    }

    [Fact]
    public void ComputeSupport_MissingPrereq_ReturnsZero()
    {
        var graphCache = new FakeGraphCache(prerequisites: new()
        {
            ["concept-B"] = new()
            {
                new MasteryPrerequisiteEdge("concept-A", "concept-B", 1.0f)
            }
        });
        var overlay = new Dictionary<string, ConceptMasteryState>();

        var support = PrerequisiteCalculator.ComputeSupport("concept-B", overlay, graphCache);

        Assert.Equal(0.0f, support);
    }

    [Fact]
    public void ComputeSupport_ThreeConceptChain_DirectPrereqOnly()
    {
        var graphCache = new FakeGraphCache(prerequisites: new()
        {
            ["concept-B"] = new() { new MasteryPrerequisiteEdge("concept-A", "concept-B", 1.0f) },
            ["concept-C"] = new() { new MasteryPrerequisiteEdge("concept-B", "concept-C", 1.0f) }
        });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["concept-A"] = new() { MasteryProbability = 0.40f },
            ["concept-B"] = new() { MasteryProbability = 0.90f }
        };

        var supportC = PrerequisiteCalculator.ComputeSupport("concept-C", overlay, graphCache);

        Assert.Equal(0.90f, supportC); // direct prereq B at 0.90
    }

    [Fact]
    public void ComputeWeightedPenalty_AllAboveGate_ReturnsOne()
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
            ["concept-A"] = new() { MasteryProbability = 0.92f },
            ["concept-B"] = new() { MasteryProbability = 0.88f }
        };

        var penalty = PrerequisiteCalculator.ComputeWeightedPenalty("concept-C", overlay, graphCache);

        Assert.Equal(1.0f, penalty);
    }

    [Fact]
    public void ComputeWeightedPenalty_WeakPrereq_AppliesPenalty()
    {
        var graphCache = new FakeGraphCache(prerequisites: new()
        {
            ["concept-B"] = new() { new MasteryPrerequisiteEdge("concept-A", "concept-B", 1.0f) }
        });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["concept-A"] = new() { MasteryProbability = 0.42f }
        };

        var penalty = PrerequisiteCalculator.ComputeWeightedPenalty("concept-B", overlay, graphCache);

        // 0.42 / 0.85 ~ 0.494
        Assert.InRange(penalty, 0.49f, 0.50f);
    }

    [Fact]
    public void ComputeWeightedPenalty_NoPrereqs_ReturnsOne()
    {
        var graphCache = new FakeGraphCache();
        var overlay = new Dictionary<string, ConceptMasteryState>();

        var penalty = PrerequisiteCalculator.ComputeWeightedPenalty("concept-A", overlay, graphCache);

        Assert.Equal(1.0f, penalty);
    }

    [Fact]
    public void ComputeWeightedPenalty_MultipleWeakPrereqs_Compounds()
    {
        var graphCache = new FakeGraphCache(prerequisites: new()
        {
            ["concept-D"] = new()
            {
                new MasteryPrerequisiteEdge("concept-A", "concept-D", 1.0f),
                new MasteryPrerequisiteEdge("concept-B", "concept-D", 1.0f)
            }
        });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["concept-A"] = new() { MasteryProbability = 0.42f },
            ["concept-B"] = new() { MasteryProbability = 0.42f }
        };

        var penalty = PrerequisiteCalculator.ComputeWeightedPenalty("concept-D", overlay, graphCache);

        // (0.42/0.85) * (0.42/0.85) ~ 0.494 * 0.494 ~ 0.244
        Assert.InRange(penalty, 0.24f, 0.25f);
    }
}
