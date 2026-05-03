// =============================================================================
// Cena Platform — DiagnosisHintLevelAdjuster (RDY-063 Phase 2b)
//
// Rule-based hint-level adjustment driven by the 7-category stuck-type
// ontology (ADR-0036). Each rule is conservative — prefer NO change to
// an uncertain change.
//
// Rules (fire only if diagnosis primary-confidence ≥ minConfidence):
//
//   MetaStuck          → clamp to 1     ("step way back; regroup" — no
//                                       escalating scaffolds)
//   Motivational       → clamp to 1     (cognitive scaffolding won't
//                                       fix an engagement problem)
//   Misconception      → max(req, 2)    (a nudge is rarely enough; need
//                                       an explicit contradiction/work)
//   Strategic + req=3  → 2              (give decomposition prompt, not
//                                       a worked example; don't give
//                                       away strategy)
//   Encoding / Recall / Procedural → keep req (existing ladder fits)
//   Unknown            → keep req (no signal)
//
// ReasonCode is short + machine-readable so adjustments can be
// dash-boarded and A/B'd over the pilot.
// =============================================================================

namespace Cena.Actors.Diagnosis;

public sealed class DiagnosisHintLevelAdjuster : IHintLevelAdjuster
{
    public HintLevelAdjustment Adjust(
        int requestedLevel, int maxLevel, StuckDiagnosis? diagnosis, float minConfidence)
    {
        var safeMax = Math.Max(1, maxLevel);
        var safeReq = Math.Clamp(requestedLevel, 1, safeMax);

        if (diagnosis is null ||
            diagnosis.Primary == StuckType.Unknown ||
            diagnosis.PrimaryConfidence < minConfidence)
        {
            return new HintLevelAdjustment(
                OriginalLevel: safeReq,
                AdjustedLevel: safeReq,
                Changed: false,
                ReasonCode: "adjuster.no_signal");
        }

        int adjusted;
        string reason;
        switch (diagnosis.Primary)
        {
            case StuckType.MetaStuck:
                adjusted = 1;
                reason = "adjuster.meta_stuck_clamp_to_1";
                break;

            case StuckType.Motivational:
                adjusted = 1;
                reason = "adjuster.motivational_clamp_to_1";
                break;

            case StuckType.Misconception:
                adjusted = Math.Max(safeReq, 2);
                reason = safeReq == adjusted
                    ? "adjuster.misconception_keep"
                    : "adjuster.misconception_bump_to_2";
                break;

            case StuckType.Strategic when safeReq == 3:
                adjusted = 2;
                reason = "adjuster.strategic_clamp_from_3_to_2";
                break;

            // Encoding, Recall, Procedural, Strategic-at-1/2 → keep
            default:
                adjusted = safeReq;
                reason = $"adjuster.keep_{diagnosis.Primary.ToString().ToLowerInvariant()}";
                break;
        }

        // Never exceed the scaffolding budget or go below 1.
        adjusted = Math.Clamp(adjusted, 1, safeMax);

        return new HintLevelAdjustment(
            OriginalLevel: safeReq,
            AdjustedLevel: adjusted,
            Changed: adjusted != safeReq,
            ReasonCode: reason);
    }
}
