// =============================================================================
// MST-010 Tests: Elo scoring and item selector
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class EloScoringTests
{
    [Fact]
    public void ExpectedCorrectness_EqualRatings_Returns50Percent()
    {
        var expected = EloScoring.ExpectedCorrectness(1200f, 1200f);
        Assert.InRange(expected, 0.49f, 0.51f);
    }

    [Fact]
    public void ExpectedCorrectness_StrongerStudent_ReturnsHigh()
    {
        var expected = EloScoring.ExpectedCorrectness(1500f, 1200f);
        Assert.True(expected > 0.80f, $"Expected > 0.80, got {expected}");
    }

    [Fact]
    public void ExpectedCorrectness_WeakerStudent_ReturnsLow()
    {
        var expected = EloScoring.ExpectedCorrectness(1000f, 1400f);
        Assert.True(expected < 0.30f, $"Expected < 0.30, got {expected}");
    }

    [Fact]
    public void UpdateRatings_CorrectAnswer_StudentGoesUp_ItemGoesDown()
    {
        var (newTheta, newDiff) = EloScoring.UpdateRatings(
            1200f, 1200f, isCorrect: true, studentK: 40f, itemK: 10f);

        Assert.True(newTheta > 1200f, "Student rating should increase after correct");
        // Item difficulty decreases: student beat it, so it's easier than thought
        Assert.True(newDiff < 1200f, "Item difficulty should decrease (student beat it)");
    }

    [Fact]
    public void UpdateRatings_IncorrectAnswer_StudentGoesDown_ItemGoesUp()
    {
        var (newTheta, newDiff) = EloScoring.UpdateRatings(
            1200f, 1200f, isCorrect: false, studentK: 40f, itemK: 10f);

        Assert.True(newTheta < 1200f, "Student rating should decrease after incorrect");
        // Item difficulty increases: it stumped the student, so it's harder than thought
        Assert.True(newDiff > 1200f, "Item difficulty should increase (student failed it)");
    }

    [Fact]
    public void StudentKFactor_NewStudent_High()
    {
        Assert.Equal(40f, EloScoring.StudentKFactor(5));
    }

    [Fact]
    public void StudentKFactor_ExperiencedStudent_Low()
    {
        Assert.Equal(10f, EloScoring.StudentKFactor(100));
    }

    [Fact]
    public void SelectNext_PicksItemClosestTo85Percent()
    {
        var frontier = new List<FrontierConcept>
        {
            new("quadratics", "Quadratics", "algebra", 0.95f, 0.30f, 0.5f, 0.0f, 0.85f)
        };
        var items = new List<ItemCandidate>
        {
            new("item-1", "quadratics", 3, 1100f, 0f),
            new("item-2", "quadratics", 3, 1300f, 0f),
            new("item-3", "quadratics", 4, 1600f, 0f),
        };
        // Precompute expected correctness
        for (int i = 0; i < items.Count; i++)
            items[i] = items[i] with
            {
                ExpectedCorrectness = EloScoring.ExpectedCorrectness(1350f, items[i].DifficultyElo)
            };

        var selected = ItemSelector.SelectNext(frontier, 1350f, items,
            lastConceptId: null, interleavingProbability: 0f);

        Assert.NotNull(selected);
        // item-1 (Elo 1100): expected = 1/(1+10^(-250/400)) ~ 0.81 -> |0.81-0.85| = 0.04
        // item-2 (Elo 1300): expected = 1/(1+10^(-50/400)) ~ 0.53 -> |0.53-0.85| = 0.32
        // item-3 (Elo 1600): expected = 1/(1+10^(250/400)) ~ 0.19 -> |0.19-0.85| = 0.66
        Assert.Equal("item-1", selected.ItemId);
    }

    [Fact]
    public void SelectNext_EmptyFrontier_ReturnsNull()
    {
        var selected = ItemSelector.SelectNext(
            new List<FrontierConcept>(), 1200f,
            new List<ItemCandidate>(), null);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectNext_InterleavingForced_SwitchesConcept()
    {
        var frontier = new List<FrontierConcept>
        {
            new("quadratics", "Quadratics", "algebra", 0.95f, 0.30f, 0.5f, 0.0f, 0.90f),
            new("triangles", "Triangles", "geometry", 0.90f, 0.25f, 0.4f, 0.0f, 0.80f)
        };
        var items = new List<ItemCandidate>
        {
            new("item-a", "quadratics", 3, 1200f, 0.85f),
            new("item-b", "triangles", 3, 1200f, 0.85f),
        };

        var selected = ItemSelector.SelectNext(frontier, 1200f, items,
            lastConceptId: "quadratics", interleavingProbability: 1.0f);

        Assert.NotNull(selected);
        Assert.Equal("triangles", selected.ConceptId);
    }
}
