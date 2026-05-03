// =============================================================================
// Cena Platform — RecommendationService Scoring Tests (HARDEN PlanEndpoints)
// Exercises the weighted scoring logic over real projection shapes.
// Weights: 50% review urgency + 30% mastery gap + 20% recency.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Api.Host.Services; // FIND-arch-001: Now from Cena.Student.Api.Host via InternalsVisibleTo

namespace Cena.Actors.Tests.Services;

public class RecommendationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 11, 12, 0, 0, TimeSpan.Zero);

    private static StudentProfileSnapshot NewProfile(params string[] subjects) =>
        new()
        {
            StudentId = "student-1",
            Subjects = subjects,
            DailyTimeGoalMinutes = 30,
            CreatedAt = Now.AddDays(-30),
        };

    private static SubjectMasteryTimeline NewTimeline(
        string subject,
        double mastery,
        int attempted,
        int mastered,
        DateTime asOf,
        double accuracy = 0.5)
    {
        return new SubjectMasteryTimeline
        {
            Id = $"student-1:{subject}",
            StudentId = "student-1",
            Subject = subject,
            Snapshots = new List<MasterySnapshot>
            {
                new()
                {
                    Date = asOf,
                    AverageMastery = mastery,
                    ConceptsAttempted = attempted,
                    ConceptsMastered = mastered,
                    Accuracy = accuracy,
                },
            },
        };
    }

    [Fact]
    public void ScoreSubject_NoTimeline_ReturnsMaxGapAndRecency()
    {
        var profile = NewProfile("math");

        var scored = RecommendationService.ScoreSubject("math", timeline: null, profile, Now);

        Assert.Equal(0.0, scored.ReviewUrgency);
        Assert.Equal(1.0, scored.MasteryGap);
        Assert.Equal(1.0, scored.Recency);
        // 0.0*0.5 + 1.0*0.3 + 1.0*0.2 = 0.5
        Assert.Equal(0.5, scored.TotalScore, precision: 3);
        Assert.Null(scored.LastAttemptedAt);
    }

    [Fact]
    public void ScoreSubject_HighOverdueFraction_DrivesReviewUrgency()
    {
        var profile = NewProfile("math");
        var timeline = NewTimeline(
            subject: "math",
            mastery: 0.6,
            attempted: 10,
            mastered: 2,             // 8/10 still unmastered
            asOf: Now.UtcDateTime);

        var scored = RecommendationService.ScoreSubject("math", timeline, profile, Now);

        Assert.Equal(0.8, scored.ReviewUrgency, precision: 3);
        Assert.Equal(8, scored.OverdueCount);
        Assert.Equal(0.4, scored.MasteryGap, precision: 3);
        Assert.Equal(0.0, scored.Recency, precision: 3);
        // 0.8*0.5 + 0.4*0.3 + 0.0*0.2 = 0.52
        Assert.Equal(0.52, scored.TotalScore, precision: 3);
    }

    [Fact]
    public void ScoreSubject_OldSnapshot_ClampsRecencyAtOne()
    {
        var profile = NewProfile("science");
        var asOf = Now.UtcDateTime.AddDays(-7); // 168h > 72h ceiling
        var timeline = NewTimeline("science", mastery: 0.7, attempted: 4, mastered: 4, asOf: asOf);

        var scored = RecommendationService.ScoreSubject("science", timeline, profile, Now);

        Assert.Equal(1.0, scored.Recency, precision: 3);
        Assert.Equal(0.0, scored.ReviewUrgency, precision: 3);
        Assert.Equal(0.3, scored.MasteryGap, precision: 3);
    }

    [Fact]
    public void ScoreSubject_ZeroAttempted_HasNoReviewUrgency()
    {
        var profile = NewProfile("english");
        var timeline = NewTimeline("english", mastery: 0.0, attempted: 0, mastered: 0, asOf: Now.UtcDateTime);

        var scored = RecommendationService.ScoreSubject("english", timeline, profile, Now);

        Assert.Equal(0.0, scored.ReviewUrgency);
        Assert.Equal(0, scored.OverdueCount);
        Assert.Equal(1.0, scored.MasteryGap);
    }

    [Fact]
    public void BuildReason_OverdueSingular_UsesSingularPhrase()
    {
        var scored = new RecommendationService.ScoredSubject(
            Subject: "math", TotalScore: 0.5,
            ReviewUrgency: 1.0, OverdueCount: 1, TotalConcepts: 1,
            MasteryGap: 0.0, AverageMastery: 1.0,
            Recency: 0.0, LastAttemptedAt: Now.AddHours(-1));

        Assert.Equal("1 concept needs review", RecommendationService.BuildReason(scored));
    }

    [Fact]
    public void BuildReason_OverduePlural_UsesCountPhrase()
    {
        var scored = new RecommendationService.ScoredSubject(
            Subject: "math", TotalScore: 0.5,
            ReviewUrgency: 1.0, OverdueCount: 5, TotalConcepts: 5,
            MasteryGap: 0.0, AverageMastery: 1.0,
            Recency: 0.0, LastAttemptedAt: Now.AddHours(-1));

        Assert.Equal("5 concepts need review", RecommendationService.BuildReason(scored));
    }

    [Fact]
    public void BuildReason_LargeMasteryGap_CitesPercentage()
    {
        var scored = new RecommendationService.ScoredSubject(
            Subject: "math", TotalScore: 0.4,
            ReviewUrgency: 0.0, OverdueCount: 0, TotalConcepts: 0,
            MasteryGap: 0.55, AverageMastery: 0.45,
            Recency: 0.0, LastAttemptedAt: Now.AddHours(-1));

        Assert.Equal("55% below mastery target", RecommendationService.BuildReason(scored));
    }

    [Fact]
    public void BuildReason_NeverAttempted_CitesFreshStart()
    {
        var scored = new RecommendationService.ScoredSubject(
            Subject: "history", TotalScore: 0.2,
            ReviewUrgency: 0.0, OverdueCount: 0, TotalConcepts: 0,
            MasteryGap: 0.0, AverageMastery: 1.0,
            Recency: 1.0, LastAttemptedAt: null);

        Assert.Equal("You haven't practiced this yet", RecommendationService.BuildReason(scored));
    }

    [Fact]
    public void BuildReason_TwoDayDrought_CitesDays()
    {
        var last = Now.AddHours(-60);
        var scored = new RecommendationService.ScoredSubject(
            Subject: "science", TotalScore: 0.1,
            ReviewUrgency: 0.0, OverdueCount: 0, TotalConcepts: 4,
            MasteryGap: 0.05, AverageMastery: 0.95,
            Recency: 0.8, LastAttemptedAt: last);

        var reason = RecommendationService.BuildReason(scored);

        Assert.StartsWith("No practice in ", reason);
        Assert.Contains("days", reason);
    }

    [Fact]
    public void ToRecommendation_LowMastery_EasyDifficulty()
    {
        var scored = new RecommendationService.ScoredSubject(
            Subject: "math", TotalScore: 0.5,
            ReviewUrgency: 0.0, OverdueCount: 0, TotalConcepts: 0,
            MasteryGap: 0.8, AverageMastery: 0.2,
            Recency: 0.5, LastAttemptedAt: null);

        var rec = RecommendationService.ToRecommendation(scored);

        Assert.Equal("easy", rec.Difficulty);
        Assert.Equal("math", rec.Subject);
        Assert.Equal(15, rec.EstimatedMinutes);
        Assert.False(string.IsNullOrWhiteSpace(rec.Reason));
    }

    [Fact]
    public void ToRecommendation_HighMastery_HardDifficulty()
    {
        var scored = new RecommendationService.ScoredSubject(
            Subject: "math", TotalScore: 0.2,
            ReviewUrgency: 0.0, OverdueCount: 0, TotalConcepts: 10,
            MasteryGap: 0.1, AverageMastery: 0.9,
            Recency: 0.3, LastAttemptedAt: Now.AddHours(-10));

        var rec = RecommendationService.ToRecommendation(scored);

        Assert.Equal("hard", rec.Difficulty);
    }
}
