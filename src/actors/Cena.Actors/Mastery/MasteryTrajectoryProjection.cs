// =============================================================================
// Cena Platform — Mastery Trajectory Projection (RDY-071 Phase 1A)
//
// Reduces a time-ordered list of AbilityEstimate snapshots into a
// display-ready trajectory for the student + parent views.
//
// The projection is honest-labeled by construction:
//   - Every returned point carries its bucket (HIGH/MEDIUM/LOW/Inconclusive)
//   - Every returned point carries its sample-size + CI so the caption
//     "based on N problems over M weeks" is data-driven, not hardcoded
//   - NEVER returns a numeric Ministry-exam scaled-score; that view is
//     gated by ConcordanceMapping.F8PointEstimateEnabled (RDY-080)
//
// Callers (student view, parent view, admin dashboard) consume the
// returned record and have no alternative prediction path — the
// shipgate banned-phrase list (RDY-071 honest-framing doc) guards
// against UI code constructing forward-extrapolation strings directly.
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>One point on the trajectory chart, render-ready.</summary>
public sealed record TrajectoryPoint(
    DateTimeOffset AtUtc,
    double Theta,
    double StandardError,
    int SampleSize,
    MasteryBucket Bucket);

/// <summary>
/// Full trajectory envelope for one student + topic (or overall
/// composite). <see cref="CurrentBucket"/> is always the last point's
/// bucket; <see cref="CurrentCaption"/> composes the honest caption
/// copy from <see cref="TotalSampleSize"/> + <see cref="WindowWeeks"/>.
/// </summary>
public sealed record MasteryTrajectory(
    string StudentAnonId,
    string TopicSlug,
    ImmutableArray<TrajectoryPoint> Points,
    int TotalSampleSize,
    int WindowWeeks)
{
    public MasteryBucket CurrentBucket
        => Points.IsDefaultOrEmpty
            ? MasteryBucket.Inconclusive
            : Points[^1].Bucket;

    /// <summary>
    /// Honest caption composed from the evidence actually available.
    /// The student/parent view MUST render this verbatim rather than
    /// authoring its own string — that keeps the numbers and the copy
    /// in lockstep and keeps the shipgate banned-phrase scan effective.
    /// </summary>
    public string CurrentCaption
    {
        get
        {
            if (CurrentBucket == MasteryBucket.Inconclusive)
                return $"Based on {TotalSampleSize} problems over "
                       + $"{WindowWeeks} weeks — we need more data for a "
                       + "clear read. Keep practicing.";

            return $"Mastery level: {CurrentBucket.ToString().ToUpperInvariant()} "
                   + $"(80% confidence, {TotalSampleSize} problems, "
                   + $"{WindowWeeks} weeks)";
        }
    }

    /// <summary>
    /// Build a trajectory from a time-ordered list of ability estimates.
    /// Callers are responsible for passing the estimates for a single
    /// (student, topic) tuple in chronological order.
    /// </summary>
    public static MasteryTrajectory FromEstimates(IReadOnlyList<AbilityEstimate> estimates)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        if (estimates.Count == 0)
            throw new ArgumentException(
                "Cannot build a trajectory from zero estimates.",
                nameof(estimates));

        var studentId = estimates[0].StudentAnonId;
        var topicSlug = estimates[0].TopicSlug;
        var points = estimates
            .Select(e => new TrajectoryPoint(
                AtUtc: e.ComputedAtUtc,
                Theta: e.Theta,
                StandardError: e.StandardError,
                SampleSize: e.SampleSize,
                Bucket: e.Bucket()))
            .ToImmutableArray();

        var totalSamples = estimates[^1].SampleSize;
        var windowWeeks = estimates[^1].ObservationWindowWeeks;

        return new MasteryTrajectory(
            StudentAnonId: studentId,
            TopicSlug: topicSlug,
            Points: points,
            TotalSampleSize: totalSamples,
            WindowWeeks: windowWeeks);
    }
}
