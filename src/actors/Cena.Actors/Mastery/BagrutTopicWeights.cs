// =============================================================================
// Cena Platform — Bagrut Topic Weights (RDY-073 Phase 1A)
//
// Per-topic exam-weight assignments used by the adaptive-scheduler
// prioritization formula:
//
//     priority(topic, student) = weakness(θ_topic)
//                              × examWeight(topic)
//                              × prerequisiteUrgency(topic, plan)
//
// Dr. Rami's demand from Round 4 of the panel review: weights MUST cite
// their source. Every entry below carries a <see cref="WeightSource"/>
// enum value and a free-text citation string. Entries without a Ministry
// citation are explicitly flagged as <see cref="WeightSource.ExpertJudgment"/>
// so the scheduler can expose them to Prof. Amjad for review and to
// students in the "why is this on my plan?" rationale text.
//
// Weights are declared as fractions summing to 1.0 within a track. A
// CI test (see BagrutTopicWeightsTests.cs) asserts the sum invariant so
// a copy-paste typo doesn't silently inflate one topic's priority.
//
// Source layering (from most to least authoritative):
//   1. Ministry-published exam-weight tables (when released)
//   2. Ministry guidance documents + historical exam-content analysis
//      produced by bagrut-reference-analyzer.py (ADR-0033 compliant —
//      structure only, not content re-publication)
//   3. Expert-judgment from curriculum panel (Prof. Amjad) — explicitly
//      labelled so it is not mistaken for a Ministry-sourced weight
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>Provenance of a single topic weight.</summary>
public enum WeightSource
{
    /// <summary>
    /// Directly sourced from a Ministry-published exam-weight table.
    /// Highest confidence; stable across exam cycles unless Ministry
    /// re-publishes.
    /// </summary>
    MinistryPublished = 0,

    /// <summary>
    /// Derived from Ministry guidance docs + multi-year exam-content
    /// frequency analysis (reference-only per ADR-0033 — we analyse
    /// structure, we do not re-publish exam content).
    /// </summary>
    HistoricalAnalysis = 1,

    /// <summary>
    /// Expert-judgment placeholder awaiting Ministry / Prof. Amjad
    /// confirmation. MUST be visible in the "why is this on my plan?"
    /// rationale UI so students know the weight is not canonical.
    /// </summary>
    ExpertJudgment = 2
}

/// <summary>One topic's weight within a track.</summary>
public sealed record BagrutTopicWeight(
    string TopicSlug,
    double Weight,
    WeightSource Source,
    string Citation);

/// <summary>
/// Static catalogue of topic weights per Bagrut track. The weights in
/// this file are explicitly phase-1A placeholders; Ministry-published
/// weights for the 2026 exam cycle supersede these once Prof. Amjad
/// confirms them in writing.
/// </summary>
public static class BagrutTopicWeights
{
    /// <summary>
    /// 5-unit Bagrut (805 track) topic weights.
    ///
    /// Current shipping values are ExpertJudgment placeholders aligned
    /// with the `config/syllabi/math-bagrut-5unit.yaml` 10-chapter
    /// structure + the post-2019 Ministry guidance that gave analysis
    /// (functions + calculus) ~45% of the written exam, algebra ~20%,
    /// probability + sequences ~15%, trigonometry + geometry ~20%.
    /// These map onto the 10 chapters as shown. Adjust once Ministry
    /// publishes new weights.
    /// </summary>
    public static readonly ImmutableArray<BagrutTopicWeight> FiveUnit =
        ImmutableArray.Create(
            new BagrutTopicWeight(
                TopicSlug: "algebra-review",
                Weight: 0.10,
                Source: WeightSource.ExpertJudgment,
                Citation: "Prof. Amjad judgment aligning with post-2019 Ministry guidance (algebra ~20% of written exam; split across algebra-review + analytic-geometry + inequalities chapters)."),

            new BagrutTopicWeight(
                TopicSlug: "functions-and-graphs",
                Weight: 0.14,
                Source: WeightSource.ExpertJudgment,
                Citation: "Same Ministry guidance — functions + analysis ~45% of exam; here split across functions-and-graphs + derivatives + integrals + applications-of-derivatives."),

            new BagrutTopicWeight(
                TopicSlug: "derivatives",
                Weight: 0.13,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance (analysis cluster)."),

            new BagrutTopicWeight(
                TopicSlug: "integrals",
                Weight: 0.10,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance (analysis cluster)."),

            new BagrutTopicWeight(
                TopicSlug: "applications-of-derivatives",
                Weight: 0.08,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance (analysis cluster); applied optimization + rate-of-change problems."),

            new BagrutTopicWeight(
                TopicSlug: "trigonometry",
                Weight: 0.10,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance — trig + geometry combined ~20%; 5-unit exam historically leans trig-heavy."),

            new BagrutTopicWeight(
                TopicSlug: "analytic-geometry",
                Weight: 0.08,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance (geometry cluster)."),

            new BagrutTopicWeight(
                TopicSlug: "sequences",
                Weight: 0.07,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance — probability + sequences ~15% combined."),

            new BagrutTopicWeight(
                TopicSlug: "probability",
                Weight: 0.10,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance — probability question typically 1-2 of the 10 written items on 5-unit exam."),

            new BagrutTopicWeight(
                TopicSlug: "inequalities",
                Weight: 0.10,
                Source: WeightSource.ExpertJudgment,
                Citation: "Ministry guidance (algebra cluster); inequalities appear in both algebra and analysis questions.")
        );

    /// <summary>
    /// Lookup weight for a topic slug. Returns null when the slug is
    /// unknown — callers should treat unknown topics as zero-weight
    /// (don't schedule them) rather than defaulting to an average.
    /// </summary>
    public static BagrutTopicWeight? ForFiveUnit(string topicSlug)
        => FiveUnit.FirstOrDefault(w => w.TopicSlug == topicSlug);

    /// <summary>
    /// Sum of weights for a track. Must be 1.0 (within floating-point
    /// tolerance) — enforced by the CI test suite so a weight edit
    /// cannot accidentally inflate or deflate the normalisation.
    /// </summary>
    public static double TotalWeight(IReadOnlyCollection<BagrutTopicWeight> weights)
        => weights.Sum(w => w.Weight);
}
