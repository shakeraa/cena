// =============================================================================
// Cena Platform — RetakeRetrievalStrengthBias (prr-238)
//
// When a session runs against a target with ReasonTag=Retake, this bias
// modulator increases the spaced-retrieval weighting relative to worked-
// example / re-teaching weighting.
//
// WHY (ADR-0049 research citation + ADR-0050 §1 ReasonTag semantics):
//
//   Retake candidates have been through the material before — they don't
//   need concept re-teaching, they need retrieval practice to stabilise
//   shaky memory traces. The retrieval-practice effect (Karpicke &
//   Roediger 2008, "The critical importance of retrieval for learning",
//   Science 319, pp. 966–968) measures d ≈ 0.50 advantage over restudy
//   at 1-week delay. That is the research we cite here; we intentionally
//   do NOT cite the cherry-picked Rohrer d ≈ 1.05 figure per the
//   "Honest not complimentary" memory rule (persona-cogsci's hard note
//   on ADR-0050 §Risks accepted).
//
// NO LLM on this path. This is a pure multiplier; scheduler code-path
// discipline (SchedulerNoLlmCallTest) continues to apply.
//
// The bias is SCHEDULER-INTERNAL. No student-facing copy mentions "retake
// mode" or "you're in retake prep" — the retake surface framing lives in
// the `onboarding.retakeBanner.*` i18n keys + student views, not here.
// =============================================================================

using Cena.Actors.StudentPlan;

namespace Cena.Actors.Mastery;

/// <summary>
/// Pure modulator: given a ReasonTag, returns a (retrievalWeight,
/// workedExampleWeight) pair the scheduler can multiply into its item-
/// selection priorities. Default (non-Retake) returns (1.0, 1.0) — no
/// change. Retake returns (1.25, 0.80) — biasing toward retrieval
/// practice without eliminating worked examples entirely.
/// </summary>
public static class RetakeRetrievalStrengthBias
{
    /// <summary>
    /// Retrieval-practice weight when <see cref="ReasonTag.Retake"/>.
    /// Calibrated so retrieval-practice items are ~50% more likely to be
    /// selected than at default (1.25 / 0.80 = 1.5625 ratio). Slightly
    /// below Karpicke's d ≈ 0.50 because our selector is not pure
    /// retrieval; some worked-example scaffolding remains.
    /// </summary>
    public const double RetakeRetrievalWeight = 1.25d;

    /// <summary>
    /// Worked-example weight when <see cref="ReasonTag.Retake"/>. Reduced
    /// from 1.0 to 0.80 — we don't zero it because some retake students
    /// still need an occasional scaffold pass, but the mix tilts toward
    /// retrieval.
    /// </summary>
    public const double RetakeWorkedExampleWeight = 0.80d;

    /// <summary>
    /// Default retrieval weight for non-Retake ReasonTags (NewSubject,
    /// ReviewOnly, Enrichment, SafetyFlag) and for unknown-reason
    /// targets.
    /// </summary>
    public const double DefaultRetrievalWeight = 1.0d;

    /// <summary>
    /// Default worked-example weight.
    /// </summary>
    public const double DefaultWorkedExampleWeight = 1.0d;

    /// <summary>
    /// Look up the (retrieval, worked-example) weighting pair for the
    /// given ReasonTag. Null input returns the default pair.
    /// </summary>
    public static (double RetrievalWeight, double WorkedExampleWeight) ForReason(ReasonTag? reason)
    {
        return reason switch
        {
            ReasonTag.Retake => (RetakeRetrievalWeight, RetakeWorkedExampleWeight),
            _ => (DefaultRetrievalWeight, DefaultWorkedExampleWeight),
        };
    }

    /// <summary>
    /// True iff the given ReasonTag triggers the retrieval-strength bias.
    /// Exposed for the arch test so it can assert every callsite that
    /// reads ReasonTag honours this rule.
    /// </summary>
    public static bool AppliesTo(ReasonTag? reason) => reason == ReasonTag.Retake;
}
