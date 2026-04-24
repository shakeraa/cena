// =============================================================================
// Cena Platform — Question Selector Tests
// Tests adaptive selection: concept priority, Bloom's progression,
// ZPD difficulty, focus adaptation, session goal routing.
// =============================================================================

using Cena.Actors.Serving;
using NSubstitute;

namespace Cena.Actors.Tests.Serving;

public class QuestionSelectorTests
{
    private readonly QuestionSelector _selector;

    public QuestionSelectorTests()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<QuestionSelector>>();
        _selector = new QuestionSelector(logger);
    }

    [Fact]
    public void SelectNext_WithAvailableQuestions_ReturnsSelection()
    {
        var pool = CreateMockPool(new[]
        {
            MakeQuestion("q-1", "derivatives", bloom: 3, difficulty: 0.5f),
            MakeQuestion("q-2", "derivatives", bloom: 4, difficulty: 0.6f),
            MakeQuestion("q-3", "algebra", bloom: 2, difficulty: 0.3f),
        });

        var ctx = MakeContext(mastery: new() { { "derivatives", 0.5 }, { "algebra", 0.9 } });
        var result = _selector.SelectNext(ctx, pool);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.ConceptId);
        Assert.NotNull(result.SelectedItem);
        Assert.NotEmpty(result.SelectionReason);
    }

    [Fact]
    public void SelectNext_NeverRepeatsWithinSession()
    {
        var questions = Enumerable.Range(1, 20)
            .Select(i => MakeQuestion($"q-{i}", "derivatives", bloom: 3, difficulty: 0.5f))
            .ToArray();

        var pool = CreateMockPool(questions);
        var seen = new HashSet<string>();
        var ctx = MakeContext(
            mastery: new() { { "derivatives", 0.5 } },
            itemsSeenThisSession: seen);

        for (int i = 0; i < 15; i++)
        {
            var result = _selector.SelectNext(ctx, pool);
            if (result is null) break;

            Assert.DoesNotContain(result.SelectedItem.ItemId, seen);
            seen.Add(result.SelectedItem.ItemId);
        }

        // Should have selected at least several unique items
        Assert.True(seen.Count >= 10, $"Expected at least 10 unique selections, got {seen.Count}");
    }

    [Fact]
    public void SelectNext_PracticeGoal_PrefersLearningZone()
    {
        var pool = CreateMockPool(new[]
        {
            MakeQuestion("q-mastered", "mastered_concept", bloom: 5, difficulty: 0.9f),
            MakeQuestion("q-learning", "learning_concept", bloom: 3, difficulty: 0.5f),
            MakeQuestion("q-new", "new_concept", bloom: 1, difficulty: 0.1f),
        });

        var ctx = MakeContext(
            mastery: new()
            {
                { "mastered_concept", 0.95 },
                { "learning_concept", 0.50 },
                { "new_concept", 0.05 }
            },
            goal: SessionGoal.Practice);

        // Run multiple times to check statistical tendency
        var conceptCounts = new Dictionary<string, int>();
        for (int i = 0; i < 100; i++)
        {
            var result = _selector.SelectNext(ctx, pool);
            if (result is null) continue;
            conceptCounts.TryGetValue(result.ConceptId, out var count);
            conceptCounts[result.ConceptId] = count + 1;
        }

        // Learning zone concept (0.5 mastery) should be selected most often
        Assert.True(conceptCounts.GetValueOrDefault("learning_concept", 0) >
                     conceptCounts.GetValueOrDefault("mastered_concept", 0),
            "Practice mode should prefer learning zone concepts over mastered ones");
    }

    [Fact]
    public void SelectNext_ReviewGoal_PrefersDueConcepts()
    {
        var pool = CreateMockPool(new[]
        {
            MakeQuestion("q-recent", "recent_concept", bloom: 2, difficulty: 0.4f),
            MakeQuestion("q-due", "due_concept", bloom: 2, difficulty: 0.4f),
        });

        var ctx = MakeContext(
            mastery: new()
            {
                { "recent_concept", 0.8 },
                { "due_concept", 0.8 }
            },
            lastPracticed: new()
            {
                { "recent_concept", DateTimeOffset.UtcNow.AddHours(-2) },  // Recently practiced
                { "due_concept", DateTimeOffset.UtcNow.AddDays(-10) }      // Due for review
            },
            goal: SessionGoal.Review);

        var conceptCounts = new Dictionary<string, int>();
        for (int i = 0; i < 100; i++)
        {
            var result = _selector.SelectNext(ctx, pool);
            if (result is null) continue;
            conceptCounts.TryGetValue(result.ConceptId, out var count);
            conceptCounts[result.ConceptId] = count + 1;
        }

        Assert.True(conceptCounts.GetValueOrDefault("due_concept", 0) >
                     conceptCounts.GetValueOrDefault("recent_concept", 0),
            "Review mode should prefer concepts due for spaced repetition");
    }

    [Fact]
    public void SelectNext_DegradingFocus_ReducesDifficulty()
    {
        // Create a range of difficulties
        var questions = new[]
        {
            MakeQuestion("q-easy", "derivatives", bloom: 2, difficulty: 0.2f),
            MakeQuestion("q-medium", "derivatives", bloom: 3, difficulty: 0.5f),
            MakeQuestion("q-hard", "derivatives", bloom: 4, difficulty: 0.8f),
        };
        var pool = CreateMockPool(questions);

        // With degrading focus, should prefer easier questions
        var ctx = MakeContext(
            mastery: new() { { "derivatives", 0.5 } },
            focus: FocusState.Degrading);

        var difficulties = new List<float>();
        for (int i = 0; i < 50; i++)
        {
            var result = _selector.SelectNext(ctx, pool);
            if (result is not null)
                difficulties.Add(result.SelectedItem.Difficulty);
        }

        var avgDifficulty = difficulties.Average();
        Assert.True(avgDifficulty < 0.6f,
            $"With degrading focus, avg difficulty should be < 0.6, got {avgDifficulty:F2}");
    }

    [Fact]
    public void SelectNext_CriticalFocus_AddsReasonSuffix()
    {
        var pool = CreateMockPool(new[]
        {
            MakeQuestion("q-1", "derivatives", bloom: 2, difficulty: 0.3f),
        });

        var ctx = MakeContext(
            mastery: new() { { "derivatives", 0.5 } },
            focus: FocusState.Critical);

        var result = _selector.SelectNext(ctx, pool);
        Assert.NotNull(result);
        Assert.Contains("focus_adapted", result!.SelectionReason);
    }

    [Fact]
    public void SelectNext_EmptyPool_ReturnsNull()
    {
        var pool = CreateMockPool(Array.Empty<PublishedQuestion>());
        var ctx = MakeContext(mastery: new() { { "derivatives", 0.5 } });

        var result = _selector.SelectNext(ctx, pool);
        Assert.Null(result);
    }

    // ── Helpers ──

    private static PublishedQuestion MakeQuestion(
        string id, string concept, int bloom = 3, float difficulty = 0.5f) =>
        new(
            ItemId: id,
            Subject: "math",
            ConceptIds: new[] { concept },
            BloomLevel: bloom,
            Difficulty: difficulty,
            QualityScore: 85,
            Language: "he",
            StemPreview: $"Test question {id}",
            SourceType: "authored",
            PublishedAt: DateTimeOffset.UtcNow.AddDays(-7),
            Explanation: null);

    private static StudentContext MakeContext(
        Dictionary<string, double>? mastery = null,
        Dictionary<string, DateTimeOffset>? lastPracticed = null,
        HashSet<string>? itemsSeenThisSession = null,
        FocusState focus = FocusState.Stable,
        SessionGoal goal = SessionGoal.Practice) =>
        new(
            StudentId: "student-1",
            PreferredLanguage: "he",
            DepthUnit: 5,
            ConceptMastery: mastery ?? new() { { "derivatives", 0.5 } },
            LastPracticed: lastPracticed ?? new(),
            ItemsSeenThisSession: itemsSeenThisSession ?? new(),
            ItemsSeenLast7Days: new(),
            CurrentFocus: focus,
            Goal: goal);

    private static IQuestionPool CreateMockPool(PublishedQuestion[] questions)
    {
        var pool = Substitute.For<IQuestionPool>();

        var conceptIndex = questions
            .SelectMany(q => q.ConceptIds.Select(c => (concept: c, question: q)))
            .GroupBy(x => x.concept)
            .ToDictionary(g => g.Key, g => g.Select(x => x.question).ToList());

        pool.GetAvailableConcepts().Returns(conceptIndex.Keys.ToList());

        pool.GetForConcept(Arg.Any<string>()).Returns(callInfo =>
        {
            var conceptId = callInfo.ArgAt<string>(0);
            return conceptIndex.TryGetValue(conceptId, out var list)
                ? list : new List<PublishedQuestion>();
        });

        pool.GetFiltered(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<float>(), Arg.Any<float>()).Returns(callInfo =>
        {
            var conceptId = callInfo.ArgAt<string>(0);
            var minBloom = callInfo.ArgAt<int>(1);
            var maxBloom = callInfo.ArgAt<int>(2);
            var minDiff = callInfo.ArgAt<float>(3);
            var maxDiff = callInfo.ArgAt<float>(4);

            if (!conceptIndex.TryGetValue(conceptId, out var list))
                return new List<PublishedQuestion>();

            return list.Where(q =>
                q.BloomLevel >= minBloom && q.BloomLevel <= maxBloom &&
                q.Difficulty >= minDiff && q.Difficulty <= maxDiff
            ).ToList();
        });

        return pool;
    }
}
