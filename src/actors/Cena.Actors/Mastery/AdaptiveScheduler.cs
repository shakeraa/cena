// =============================================================================
// Cena Platform — Adaptive Scheduler (RDY-073 Phase 1A)
//
// Given a student's per-topic ability estimates + their deadline +
// their weekly time budget, produces a week-by-week plan that
// prioritises (weakness × topic-weight × prerequisite-urgency).
//
// Dr. Nadia's motivation-safe framing is baked into the public API:
// every plan entry carries a rationale string assembled from the
// same components the scheduler used to prioritise. The student UI
// (phase 1B) shows the rationale verbatim so the plan feels
// explained, not prescribed.
//
// Phase 1A ships the priority formula + plan-envelope types; the
// full week-by-week packing algorithm is Phase 1B (it needs the
// topic prerequisite DAG and the per-item time estimates that are
// tracked separately on the question bank).
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>Inputs to one scheduler run.</summary>
public sealed record SchedulerInputs(
    string StudentAnonId,
    IReadOnlyDictionary<string, AbilityEstimate> PerTopicEstimates,
    DateTimeOffset? DeadlineUtc,
    TimeSpan WeeklyTimeBudget,
    MotivationProfile MotivationProfile,
    DateTimeOffset NowUtc);

/// <summary>
/// Student's self-reported stance toward surfacing weaknesses, from
/// RDY-057 onboarding self-assessment. The scheduler phrases + orders
/// its output differently for each.
/// </summary>
public enum MotivationProfile
{
    /// <summary>
    /// Default. Neutral framing; celebrate strengths before surfacing
    /// weaknesses. Safe for unsigned-profile students.
    /// </summary>
    Neutral = 0,

    /// <summary>
    /// Confident learner (Daniel-type). Shows "60% already mastered,
    /// 40% to drill" framing. Direct, percentage-based, data-forward.
    /// </summary>
    Confident = 1,

    /// <summary>
    /// Anxious learner (Yael-type per RDY-057 + ADR-0037). Diagnostic
    /// is OPT-IN, never auto-launched. Framing shifts to "here's
    /// where we'll start" — no percentages, no weakness-forward copy.
    /// </summary>
    Anxious = 2
}

/// <summary>
/// One entry in the output plan. The rationale string is generated
/// server-side from the priority components so student / parent
/// surfaces do not author their own explanation copy (same discipline
/// as MasteryTrajectoryProjection.CurrentCaption).
/// </summary>
public sealed record PlanEntry(
    string TopicSlug,
    int WeekIndex,
    TimeSpan AllocatedTime,
    double PriorityScore,
    double WeaknessComponent,
    double TopicWeightComponent,
    double PrerequisiteComponent,
    string Rationale);

/// <summary>
/// Full plan envelope. Carries the motivation profile used so the
/// downstream rendering layer can pick the correct copy template
/// without re-deriving the decision.
/// </summary>
public sealed record CompressedPlan(
    string StudentAnonId,
    ImmutableArray<PlanEntry> Entries,
    MotivationProfile MotivationProfile,
    DateTimeOffset GeneratedAtUtc,
    int WeekCount);

/// <summary>
/// Priority-formula component breakdown for a single (topic, student)
/// pair. Weakness is derived from the gap between the student's θ and
/// a per-topic mastery target (default θ = +0.5 — top of the MEDIUM
/// bucket from RDY-071). TopicWeight comes from BagrutTopicWeights.
/// PrerequisiteUrgency is a 0..1 multiplier: 1.0 when the topic is a
/// prerequisite of something the student's plan will hit soon, 0.5
/// when it's a leaf, and values in between for intermediate nodes.
/// </summary>
public sealed record PriorityBreakdown(
    double Weakness,
    double TopicWeight,
    double PrerequisiteUrgency)
{
    public double Score => Weakness * TopicWeight * PrerequisiteUrgency;
}

/// <summary>
/// Phase 1A scheduler surface. Computes priorities per topic but
/// leaves the week-by-week packing to phase 1B. Callers get a sorted
/// list of (topic, priority, rationale) that's useful on its own for
/// "what should I work on today?" surfaces while the full packer is
/// still being built.
/// </summary>
public static class AdaptiveScheduler
{
    private const double MasteryTargetTheta = 0.5;

    /// <summary>
    /// Score every topic the student has an ability estimate for.
    /// Returns entries sorted by priority descending. Topics without a
    /// BagrutTopicWeights entry are skipped (zero-weight) — we never
    /// schedule a topic we can't justify the weight of.
    /// </summary>
    public static ImmutableArray<PlanEntry> PrioritizeTopics(SchedulerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var scored = new List<(PlanEntry entry, double score)>();
        foreach (var (topic, estimate) in inputs.PerTopicEstimates)
        {
            var weight = BagrutTopicWeights.ForFiveUnit(topic);
            if (weight is null) continue;

            var breakdown = ComputeBreakdown(estimate, weight);
            var rationale = BuildRationale(topic, estimate, weight, breakdown, inputs.MotivationProfile);

            var entry = new PlanEntry(
                TopicSlug: topic,
                WeekIndex: 0,                  // populated by phase-1B packer
                AllocatedTime: TimeSpan.Zero,  // populated by phase-1B packer
                PriorityScore: breakdown.Score,
                WeaknessComponent: breakdown.Weakness,
                TopicWeightComponent: breakdown.TopicWeight,
                PrerequisiteComponent: breakdown.PrerequisiteUrgency,
                Rationale: rationale);
            scored.Add((entry, breakdown.Score));
        }

        return scored
            .OrderByDescending(s => s.score)
            .Select(s => s.entry)
            .ToImmutableArray();
    }

    internal static PriorityBreakdown ComputeBreakdown(
        AbilityEstimate estimate,
        BagrutTopicWeight weight)
    {
        // Weakness: normalised gap between current θ and the mastery
        // target. Clamped to [0, 2] so a far-below student doesn't
        // dominate scheduling entirely; we still need to show them
        // attainable topics first.
        var raw = MasteryTargetTheta - estimate.Theta;
        var weakness = Math.Clamp(raw, 0, 2);

        // PrerequisiteUrgency is a phase-1A placeholder: 1.0 for every
        // topic since we haven't wired the prerequisite DAG here yet.
        // Phase 1B reads the SyllabusDocument prerequisite links and
        // computes the real urgency.
        const double prerequisiteUrgency = 1.0;

        return new PriorityBreakdown(
            Weakness: weakness,
            TopicWeight: weight.Weight,
            PrerequisiteUrgency: prerequisiteUrgency);
    }

    internal static string BuildRationale(
        string topic,
        AbilityEstimate estimate,
        BagrutTopicWeight weight,
        PriorityBreakdown breakdown,
        MotivationProfile profile)
    {
        // Motivation-safe framing — Dr. Nadia's hard requirement. The
        // anxious profile gets a strengths-forward opener; the
        // confident profile gets a percentage-directed nudge; neutral
        // splits the difference.
        return profile switch
        {
            MotivationProfile.Anxious =>
                $"Here's where we'll start with {topic} — this is a "
                + $"foundation step that pays off across multiple exam "
                + $"question types.",

            MotivationProfile.Confident =>
                $"Targeting {topic} because your answers suggest this is "
                + $"a gap, and Ministry guidance gives it "
                + $"{weight.Weight:P0} of the 5-unit exam weight.",

            _ /* Neutral */ =>
                $"Focusing on {topic}: you're still building mastery here, "
                + $"and it carries {weight.Weight:P0} of the 5-unit exam "
                + $"weight per Ministry guidance."
                + (weight.Source == WeightSource.ExpertJudgment
                    ? " (weight is an expert-judgment estimate pending Ministry confirmation)"
                    : string.Empty)
        };
    }
}
