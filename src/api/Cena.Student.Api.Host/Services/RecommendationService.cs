// =============================================================================
// Cena Platform — Recommendation Service Implementation
// Reads StudentProfileSnapshot + SubjectMasteryTimeline projections to compute
// weighted per-subject scores. HLR recall threshold: 0.7.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Api.Contracts.Plan;
using Marten;

namespace Cena.Api.Host.Services;

public sealed class RecommendationService : IRecommendationService
{
    private readonly IDocumentStore _store;

    public const double RecallReviewThreshold = 0.7;

    private const double WeightReviewUrgency = 0.50;
    private const double WeightMasteryGap = 0.30;
    private const double WeightRecency = 0.20;

    public RecommendationService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<RecommendedSession[]> RankForStudentAsync(
        string studentId,
        int maxResults,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(studentId) || maxResults <= 0)
            return Array.Empty<RecommendedSession>();

        await using var session = _store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId, ct);
        if (profile is null || profile.Subjects.Length == 0)
            return Array.Empty<RecommendedSession>();

        var timelines = await LoadSubjectTimelinesAsync(session, studentId, profile.Subjects, ct);
        var now = DateTimeOffset.UtcNow;

        var scored = new List<ScoredSubject>();
        foreach (var subject in profile.Subjects)
        {
            timelines.TryGetValue(subject, out var timeline);
            scored.Add(ScoreSubject(subject, timeline, profile, now));
        }

        return scored
            .OrderByDescending(s => s.TotalScore)
            .ThenBy(s => s.Subject, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(ToRecommendation)
            .ToArray();
    }

    public async Task<PlanBlock?> GetNextBlockAsync(
        string studentId,
        int remainingMinutes,
        CancellationToken ct = default)
    {
        var top = await RankForStudentAsync(studentId, 1, ct);
        if (top.Length == 0)
            return null;

        var minutes = Math.Clamp(remainingMinutes, 10, 25);
        return new PlanBlock(Subject: top[0].Subject, EstimatedMinutes: minutes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scoring logic
    // ─────────────────────────────────────────────────────────────────────────

    internal static ScoredSubject ScoreSubject(
        string subject,
        SubjectMasteryTimeline? timeline,
        StudentProfileSnapshot profile,
        DateTimeOffset now)
    {
        double reviewUrgency;
        double masteryGap;
        double recency;
        double averageMastery;
        int overdueCount;
        int totalAttempted;
        DateTimeOffset? lastAttemptedAt;

        if (timeline is not null && timeline.Snapshots.Count > 0)
        {
            var latest = timeline.Snapshots
                .OrderByDescending(s => s.Date)
                .First();

            averageMastery = Math.Clamp(latest.AverageMastery, 0.0, 1.0);
            totalAttempted = Math.Max(0, latest.ConceptsAttempted);
            var mastered = Math.Clamp(latest.ConceptsMastered, 0, totalAttempted);
            overdueCount = Math.Max(0, totalAttempted - mastered);

            reviewUrgency = totalAttempted == 0
                ? 0.0
                : (double)overdueCount / totalAttempted;

            masteryGap = 1.0 - averageMastery;

            var latestDate = DateTime.SpecifyKind(latest.Date, DateTimeKind.Utc);
            lastAttemptedAt = new DateTimeOffset(latestDate, TimeSpan.Zero);
            var hoursSince = (now - lastAttemptedAt.Value).TotalHours;
            recency = Math.Clamp(hoursSince / 72.0, 0.0, 1.0);
        }
        else
        {
            // No timeline yet — brand new subject. Maximum recency and gap, no dues.
            averageMastery = 0.0;
            totalAttempted = 0;
            overdueCount = 0;
            reviewUrgency = 0.0;
            masteryGap = 1.0;
            recency = 1.0;
            lastAttemptedAt = null;
        }

        var total = (reviewUrgency * WeightReviewUrgency)
                  + (masteryGap * WeightMasteryGap)
                  + (recency * WeightRecency);

        return new ScoredSubject(
            Subject: subject,
            TotalScore: total,
            ReviewUrgency: reviewUrgency,
            OverdueCount: overdueCount,
            TotalConcepts: totalAttempted,
            MasteryGap: masteryGap,
            AverageMastery: averageMastery,
            Recency: recency,
            LastAttemptedAt: lastAttemptedAt);
    }

    internal static RecommendedSession ToRecommendation(ScoredSubject s)
    {
        var difficulty = s.AverageMastery switch
        {
            < 0.4 => "easy",
            < 0.7 => "medium",
            _ => "hard"
        };

        var reason = BuildReason(s);
        var sessionId = $"rec:{s.Subject}:{DateTime.UtcNow:yyyyMMddHHmmss}";

        return new RecommendedSession(
            SessionId: sessionId,
            Subject: s.Subject,
            Reason: reason,
            Difficulty: difficulty,
            EstimatedMinutes: 15);
    }

    internal static string BuildReason(ScoredSubject s)
    {
        var weightedUrgency = s.ReviewUrgency * WeightReviewUrgency;
        var weightedGap = s.MasteryGap * WeightMasteryGap;
        var weightedRecency = s.Recency * WeightRecency;

        if (weightedUrgency >= weightedGap && weightedUrgency >= weightedRecency && s.OverdueCount > 0)
        {
            return s.OverdueCount == 1
                ? "1 concept needs review"
                : $"{s.OverdueCount} concepts need review";
        }

        if (weightedGap >= weightedRecency && s.MasteryGap > 0.1)
        {
            var pctGap = (int)Math.Round(s.MasteryGap * 100);
            return $"{pctGap}% below mastery target";
        }

        if (s.LastAttemptedAt is null)
            return "You haven't practiced this yet";

        var hours = (DateTimeOffset.UtcNow - s.LastAttemptedAt.Value).TotalHours;
        if (hours >= 48)
            return $"No practice in {(int)(hours / 24)} days";
        if (hours >= 24)
            return "No practice in over a day";
        return "Keep up your daily practice";
    }

    private static async Task<IReadOnlyDictionary<string, SubjectMasteryTimeline>> LoadSubjectTimelinesAsync(
        IQuerySession session,
        string studentId,
        string[] subjects,
        CancellationToken ct)
    {
        var ids = subjects.Select(s => $"{studentId}:{s}").ToList();
        var timelines = await session.Query<SubjectMasteryTimeline>()
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(ct);

        var map = new Dictionary<string, SubjectMasteryTimeline>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in timelines)
            map[t.Subject] = t;
        return map;
    }

    internal sealed record ScoredSubject(
        string Subject,
        double TotalScore,
        double ReviewUrgency,
        int OverdueCount,
        int TotalConcepts,
        double MasteryGap,
        double AverageMastery,
        double Recency,
        DateTimeOffset? LastAttemptedAt);
}
