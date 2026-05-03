// =============================================================================
// Cena Platform — Recommendation Service (HARDEN PlanEndpoints)
// Ranks subjects by weighted signal (review urgency, mastery gap, recency)
// using real event-sourced projections. No stubs, no literal data.
// =============================================================================

using Cena.Api.Contracts.Plan;

namespace Cena.Api.Host.Services;

public interface IRecommendationService
{
    /// <summary>
    /// Rank subjects for a student and return the top N recommendations.
    /// Scoring: 50% review-due urgency + 30% mastery gap + 20% recency.
    /// Each result includes a human-readable Reason citing the dominant signal.
    /// </summary>
    Task<RecommendedSession[]> RankForStudentAsync(
        string studentId,
        int maxResults,
        CancellationToken ct = default);

    /// <summary>
    /// Return the single highest-scoring subject as the next plan block.
    /// Estimated minutes = remainingGoalMinutes clamped to [10, 25].
    /// Null if the student has no subjects or no signal.
    /// </summary>
    Task<PlanBlock?> GetNextBlockAsync(
        string studentId,
        int remainingMinutes,
        CancellationToken ct = default);
}
