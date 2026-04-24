// =============================================================================
// Cena Platform — Adaptive Question Selector
// Multi-criteria selection: concept priority → Bloom's progression →
// ZPD difficulty → item freshness. Focus-aware adaptation.
// Target: <10ms per selection (in-memory only).
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Serving;

/// <summary>Student context for question selection.</summary>
public sealed record StudentContext(
    string StudentId,
    string PreferredLanguage,
    int DepthUnit,
    Dictionary<string, double> ConceptMastery,
    Dictionary<string, DateTimeOffset> LastPracticed,
    HashSet<string> ItemsSeenThisSession,
    HashSet<string> ItemsSeenLast7Days,
    FocusState CurrentFocus,
    SessionGoal Goal);

public enum FocusState { Strong, Stable, Declining, Degrading, Critical }
public enum SessionGoal { Practice, Review, Challenge, Diagnostic, ExamPrep }

/// <summary>Result of question selection with explanation.</summary>
public sealed record SelectionResult(
    PublishedQuestion SelectedItem,
    string ConceptId,
    string SelectionReason);

public interface IQuestionSelector
{
    SelectionResult? SelectNext(StudentContext context, IQuestionPool pool);
}

public sealed class QuestionSelector : IQuestionSelector
{
    private readonly ILogger<QuestionSelector> _logger;
    private static readonly Random Rng = new();

    private const double ExplorationRate = 0.10;
    private const double OptimalCorrectProbability = 0.70;

    public QuestionSelector(ILogger<QuestionSelector> logger)
    {
        _logger = logger;
    }

    public SelectionResult? SelectNext(StudentContext ctx, IQuestionPool pool)
    {
        // Step 1: Select concept
        var conceptId = SelectConcept(ctx, pool);
        if (conceptId is null)
        {
            _logger.LogDebug("No eligible concept found for student {Student}", ctx.StudentId);
            return null;
        }

        // Step 2: Determine Bloom's range based on mastery phase
        var mastery = ctx.ConceptMastery.GetValueOrDefault(conceptId, 0.0);
        var (minBloom, maxBloom) = GetBloomRange(mastery, ctx.Goal);

        // Step 3: Determine difficulty range (ZPD) with focus adaptation
        var (minDifficulty, maxDifficulty) = GetDifficultyRange(mastery, ctx.CurrentFocus);

        // Step 4: Filter available questions
        var candidates = pool.GetFiltered(conceptId, minBloom, maxBloom, minDifficulty, maxDifficulty);

        // Remove already-seen items
        candidates = candidates
            .Where(q => !ctx.ItemsSeenThisSession.Contains(q.ItemId))
            .ToList();

        if (candidates.Count == 0)
        {
            // Widen filters: try any bloom level for this concept
            candidates = pool.GetForConcept(conceptId)
                .Where(q => !ctx.ItemsSeenThisSession.Contains(q.ItemId))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug("No unseen questions for concept {Concept}, student {Student}",
                conceptId, ctx.StudentId);
            return null;
        }

        // Step 5: Select the best item
        var selected = SelectItem(candidates, ctx);
        var reason = BuildReason(conceptId, mastery, ctx.Goal, ctx.CurrentFocus);

        return new SelectionResult(selected, conceptId, reason);
    }

    /// <summary>
    /// Select the best concept to practice based on mastery, prerequisites, and session goal.
    /// </summary>
    private string? SelectConcept(StudentContext ctx, IQuestionPool pool)
    {
        var available = pool.GetAvailableConcepts();
        if (available.Count == 0) return null;

        var scored = new List<(string conceptId, double score)>();

        foreach (var conceptId in available)
        {
            var mastery = ctx.ConceptMastery.GetValueOrDefault(conceptId, 0.0);
            var lastPracticed = ctx.LastPracticed.GetValueOrDefault(conceptId, DateTimeOffset.MinValue);
            var hoursSincePractice = (DateTimeOffset.UtcNow - lastPracticed).TotalHours;

            double score = ctx.Goal switch
            {
                // Practice: prioritize concepts in the learning zone (0.3-0.7 mastery)
                SessionGoal.Practice => LearningGainScore(mastery) * 2.0
                    + SpacingBonus(hoursSincePractice) * 0.5,

                // Review: prioritize mastered concepts due for spaced repetition
                SessionGoal.Review => mastery > 0.6 && hoursSincePractice > 48
                    ? SpacingBonus(hoursSincePractice) * 3.0
                    : 0.0,

                // Challenge: prioritize concepts just above current frontier
                SessionGoal.Challenge => mastery is > 0.5 and < 0.9
                    ? (1.0 - mastery) * 2.0
                    : 0.0,

                // Diagnostic: uniform sampling across all concepts
                SessionGoal.Diagnostic => 1.0 + Rng.NextDouble() * 0.5,

                // Exam prep: weight by Bagrut exam frequency (approximated by how many questions exist)
                SessionGoal.ExamPrep => pool.GetForConcept(conceptId).Count * 0.01
                    + LearningGainScore(mastery) * 1.0,

                _ => LearningGainScore(mastery)
            };

            if (score > 0)
                scored.Add((conceptId, score));
        }

        if (scored.Count == 0) return available[Rng.Next(available.Count)];

        // Softmax-like selection: higher scores are more likely but not deterministic
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        // Exploration: 10% chance of random selection
        if (Rng.NextDouble() < ExplorationRate)
            return scored[Rng.Next(scored.Count)].conceptId;

        // Exploitation: weighted random from top 5
        var topN = scored.Take(5).ToList();
        var totalScore = topN.Sum(s => s.score);
        var roll = Rng.NextDouble() * totalScore;
        var cumulative = 0.0;
        foreach (var (conceptId, score) in topN)
        {
            cumulative += score;
            if (roll <= cumulative) return conceptId;
        }

        return topN[0].conceptId;
    }

    /// <summary>
    /// Select the best question from candidates, preferring fresh items and high quality.
    /// </summary>
    private PublishedQuestion SelectItem(IReadOnlyList<PublishedQuestion> candidates, StudentContext ctx)
    {
        // Prefer items NOT seen in last 7 days
        var fresh = candidates.Where(q => !ctx.ItemsSeenLast7Days.Contains(q.ItemId)).ToList();
        var pool = fresh.Count > 0 ? fresh : candidates.ToList();

        // Score: quality + recency bonus
        var scored = pool.Select(q =>
        {
            var qualityScore = q.QualityScore / 100.0;
            var recencyPenalty = ctx.ItemsSeenLast7Days.Contains(q.ItemId) ? 0.3 : 0.0;
            return (question: q, score: qualityScore - recencyPenalty + Rng.NextDouble() * 0.1);
        }).OrderByDescending(x => x.score).ToList();

        return scored[0].question;
    }

    /// <summary>
    /// Bloom's level range based on student mastery phase.
    /// </summary>
    private static (int min, int max) GetBloomRange(double mastery, SessionGoal goal)
    {
        if (goal == SessionGoal.Diagnostic)
            return (1, 6); // All levels for diagnostic

        return mastery switch
        {
            < 0.3 => (1, 2),   // remember, understand
            < 0.6 => (2, 4),   // understand, apply, analyze
            < 0.8 => (3, 5),   // apply, analyze, evaluate
            _ => (4, 6)        // analyze, evaluate, create
        };
    }

    /// <summary>
    /// Difficulty range (ZPD) with focus-aware adaptation.
    /// Target: P(correct) ≈ 0.65-0.75 (optimal challenge point).
    /// </summary>
    private static (float min, float max) GetDifficultyRange(double mastery, FocusState focus)
    {
        // Base ZPD: centered on mastery with asymmetric range
        var center = (float)mastery;
        var lower = Math.Max(0f, center - 0.15f);
        var upper = Math.Min(1f, center + 0.25f);

        // Focus adaptation
        var adjustment = focus switch
        {
            FocusState.Strong => 0.05f,     // Stretch slightly
            FocusState.Stable => 0f,
            FocusState.Declining => -0.10f,  // Reduce difficulty
            FocusState.Degrading => -0.20f,  // Significant reduction
            FocusState.Critical => -0.30f,   // Easy wins only
            _ => 0f
        };

        lower = Math.Max(0f, lower + adjustment);
        upper = Math.Max(lower + 0.1f, Math.Min(1f, upper + adjustment));

        return (lower, upper);
    }

    /// <summary>
    /// Learning gain score: maximum at P(mastery) ≈ 0.5 (highest learning rate in BKT).
    /// </summary>
    private static double LearningGainScore(double mastery)
    {
        // Bell curve centered at 0.5, width ~0.3
        var x = mastery - 0.5;
        return Math.Exp(-x * x / (2 * 0.09));
    }

    /// <summary>
    /// Spaced repetition bonus: increases with time since last practice.
    /// Logarithmic growth — diminishing returns after ~1 week.
    /// </summary>
    private static double SpacingBonus(double hoursSincePractice)
    {
        if (hoursSincePractice < 1) return 0;
        return Math.Log(1 + hoursSincePractice / 24.0) / Math.Log(8.0); // Peaks at ~1.0 after 7 days
    }

    private static string BuildReason(string conceptId, double mastery, SessionGoal goal, FocusState focus)
    {
        var goalReason = goal switch
        {
            SessionGoal.Practice => "practicing_new_concept",
            SessionGoal.Review => "spaced_review",
            SessionGoal.Challenge => "stretch_challenge",
            SessionGoal.Diagnostic => "diagnostic_sampling",
            SessionGoal.ExamPrep => "exam_preparation",
            _ => "adaptive_selection"
        };

        if (focus is FocusState.Declining or FocusState.Degrading or FocusState.Critical)
            goalReason += "_focus_adapted";

        return goalReason;
    }
}
