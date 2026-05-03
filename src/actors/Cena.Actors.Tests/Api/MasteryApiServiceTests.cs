// =============================================================================
// MST-017 Tests: Mastery API service layer
// =============================================================================

using Cena.Actors.Api;
using Cena.Actors.Mastery;
using Cena.Actors.Tests.Mastery;

namespace Cena.Actors.Tests.Api;

public sealed class MasteryApiServiceTests
{
    [Fact]
    public void BuildStudentMastery_ReturnsAllConcepts()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["quadratics"] = new()
            {
                MasteryProbability = 0.85f, HalfLifeHours = 168f,
                LastInteraction = now.AddHours(-24),
                AttemptCount = 15, CorrectCount = 13, BloomLevel = 4
            },
            ["derivatives"] = new()
            {
                MasteryProbability = 0.45f, HalfLifeHours = 72f,
                LastInteraction = now.AddHours(-48),
                AttemptCount = 8, CorrectCount = 4, BloomLevel = 2
            }
        };

        var response = MasteryApiService.BuildStudentMastery(
            "student-1", overlay, null, now);

        Assert.Equal("student-1", response.StudentId);
        Assert.Equal(2, response.TotalConcepts);
        Assert.Equal(2, response.Concepts.Count);
        Assert.Contains(response.Concepts, c => c.ConceptId == "quadratics");
        Assert.Contains(response.Concepts, c => c.ConceptId == "derivatives");
    }

    [Fact]
    public void BuildStudentMastery_ComputesRecallAtRequestTime()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["concept-a"] = new()
            {
                MasteryProbability = 0.92f, HalfLifeHours = 168f,
                LastInteraction = now.AddHours(-168) // at half-life
            }
        };

        var response = MasteryApiService.BuildStudentMastery("s1", overlay, null, now);

        var dto = response.Concepts[0];
        Assert.InRange(dto.RecallProbability, 0.49f, 0.51f); // at half-life -> ~0.50
    }

    [Fact]
    public void BuildStudentMastery_CountsMastered()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["mastered"] = new() { MasteryProbability = 0.95f, HalfLifeHours = 200f, LastInteraction = now },
            ["developing"] = new() { MasteryProbability = 0.50f, HalfLifeHours = 72f, LastInteraction = now },
            ["also-mastered"] = new() { MasteryProbability = 0.92f, HalfLifeHours = 168f, LastInteraction = now }
        };

        var response = MasteryApiService.BuildStudentMastery("s1", overlay, null, now);

        Assert.Equal(2, response.MasteredCount); // 0.95 and 0.92 >= 0.90
    }

    [Fact]
    public void BuildStudentMastery_EmptyOverlay_ReturnsEmpty()
    {
        var response = MasteryApiService.BuildStudentMastery(
            "s1", new Dictionary<string, ConceptMasteryState>(), null, DateTimeOffset.UtcNow);

        Assert.Empty(response.Concepts);
        Assert.Equal(0, response.TotalConcepts);
        Assert.Equal(0f, response.OverallMastery);
    }

    [Fact]
    public void BuildStudentMastery_SubjectFilter_FiltersCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(concepts: new()
        {
            ["math-1"] = new("math-1", "Algebra", "math", "algebra", 1, 0.3f, 0.5f, 4),
            ["phys-1"] = new("phys-1", "Mechanics", "physics", "mechanics", 1, 0.5f, 0.6f, 4),
        });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["math-1"] = new() { MasteryProbability = 0.80f, HalfLifeHours = 168f, LastInteraction = now },
            ["phys-1"] = new() { MasteryProbability = 0.60f, HalfLifeHours = 100f, LastInteraction = now }
        };

        var response = MasteryApiService.BuildStudentMastery(
            "s1", overlay, graphCache, now, subjectFilter: "math");

        Assert.Single(response.Concepts);
        Assert.Equal("math-1", response.Concepts[0].ConceptId);
    }

    [Fact]
    public void BuildTopicProgress_AggregatesCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(concepts: new()
        {
            ["a1"] = new("a1", "Concept A1", "math", "algebra", 1, 0.3f, 0.5f, 3),
            ["a2"] = new("a2", "Concept A2", "math", "algebra", 1, 0.4f, 0.5f, 4),
            ["g1"] = new("g1", "Geometry 1", "math", "geometry", 1, 0.3f, 0.5f, 3),
        });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["a1"] = new() { MasteryProbability = 0.95f, HalfLifeHours = 200f, LastInteraction = now },
            ["a2"] = new() { MasteryProbability = 0.50f, HalfLifeHours = 100f, LastInteraction = now }
        };

        var progress = MasteryApiService.BuildTopicProgress(overlay, graphCache, "algebra", now);

        Assert.Equal("algebra", progress.TopicClusterId);
        Assert.Equal(2, progress.ConceptCount);
        Assert.Equal(1, progress.MasteredCount); // only a1 >= 0.90
        Assert.NotNull(progress.WeakestConcept);
        Assert.Equal("a2", progress.WeakestConcept.ConceptId);
    }

    [Fact]
    public void BuildFrontier_ReturnsDtos()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["ready"] = new("ready", "Ready Concept", "math", "algebra", 1, 0.3f, 0.5f, 3),
            },
            prerequisites: new());
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["ready"] = new()
            {
                MasteryProbability = 0.40f, HalfLifeHours = 72f,
                LastInteraction = now.AddHours(-2), AttemptCount = 3
            }
        };

        var frontier = MasteryApiService.BuildFrontier(overlay, graphCache, now, 10);

        Assert.Single(frontier);
        Assert.Equal("ready", frontier[0].ConceptId);
        Assert.Equal("Ready Concept", frontier[0].Name);
    }

    [Fact]
    public void BuildDecayAlerts_ReturnsSortedByPriority()
    {
        var now = DateTimeOffset.UtcNow;
        var graphCache = new FakeGraphCache(
            concepts: new()
            {
                ["decayed"] = new("decayed", "Decayed", "math", "alg", 1, 0.3f, 0.5f, 3),
                ["fresh"] = new("fresh", "Fresh", "math", "alg", 1, 0.3f, 0.5f, 3),
            });
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["decayed"] = new()
            {
                MasteryProbability = 0.92f, HalfLifeHours = 72f,
                LastInteraction = now.AddDays(-7) // heavily decayed
            },
            ["fresh"] = new()
            {
                MasteryProbability = 0.90f, HalfLifeHours = 168f,
                LastInteraction = now.AddMinutes(-5) // just practiced
            }
        };

        var alerts = MasteryApiService.BuildDecayAlerts(overlay, graphCache, now);

        Assert.Single(alerts); // only "decayed" should appear
        Assert.Equal("decayed", alerts[0].ConceptId);
        Assert.True(alerts[0].ReviewPriority > 0);
    }
}
