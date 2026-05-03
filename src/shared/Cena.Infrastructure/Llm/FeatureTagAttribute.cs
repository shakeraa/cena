// =============================================================================
// Cena Platform — FeatureTag attribute (prr-046)
//
// Declares the product-facing cost-center (e.g. "socratic", "hint-l2",
// "hint-l3", "explanation-l3", "explanation-l2", "classification",
// "question-generation", "quality-gate", "figure-generation",
// "content-segmentation", "stuck-classification") for every LLM call site.
//
// Separation of concerns vs. [TaskRouting]:
//   [TaskRouting(tier, task_name)] is the routing contract — which model,
//   which fallback chain, which cost ceiling. It drives
//   contracts/llm/routing-config.yaml §task_routing.
//
//   [FeatureTag(feature)] is the finops cost-center overlay. Two services
//   can share a routing row but bill to different cost-centers — e.g. both
//   L3ExplanationGenerator and ExplanationGenerator use the
//   `full_explanation` routing row, but finops wants to see
//   "explanation-l3" vs "explanation-l2" spend separately so over-use of
//   L3 (a scaffolding regression signal — ADR-0045 §3 rationale) is visible
//   in the cost dashboard.
//
// Usage: apply at the class level alongside [TaskRouting]. The feature
// value flows into the `feature` label on cena_llm_call_cost_usd_total.
//
// CI enforcement:
//   scripts/shipgate/llm-routing-scanner.mjs flags any class carrying
//   [TaskRouting] but missing [FeatureTag]. The cost dashboard's
//   per-feature panels silently empty out without this label, so the
//   scanner treats it the same as a missing tier.
//
// See docs/adr/0026-llm-three-tier-routing.md and
//     docs/adr/0045-hint-and-llm-tier-selection.md.
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Product-facing finops cost-center tag for an LLM-consuming service.
/// Distinct from <see cref="TaskRoutingAttribute.TaskName"/> (the routing
/// contract) — see file header for the separation rationale.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class FeatureTagAttribute : Attribute
{
    /// <summary>
    /// Cost-center name. Lowercase kebab-case, bounded vocabulary:
    /// socratic | hint-l1 | hint-l2 | hint-l3 | explanation-l2 | explanation-l3
    /// | classification | question-generation | quality-gate | figure-generation
    /// | content-segmentation | stuck-classification
    ///
    /// Unrecognised values are allowed (the attribute is the open surface)
    /// but must also be added to the configured feature ceilings in
    /// contracts/llm/routing-config.yaml §feature_monthly_ceiling_usd or
    /// the projected-spend alert fires on a missing entry.
    /// </summary>
    public string Feature { get; }

    public FeatureTagAttribute(string feature)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            throw new ArgumentException("Feature must not be empty.", nameof(feature));
        }
        // Enforce lowercase kebab-case: no uppercase, no underscores, no spaces.
        // This keeps the Prometheus label cardinality bounded and the dashboard
        // legend readable. A typo like "Socratic" would create a phantom
        // feature in the time-series DB that never matches the ceiling config.
        foreach (var c in feature)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
            if (!ok)
            {
                throw new ArgumentException(
                    $"Feature must be lowercase kebab-case (a-z, 0-9, '-'); got '{feature}'.",
                    nameof(feature));
            }
        }
        Feature = feature;
    }
}

/// <summary>
/// Marks a [TaskRouting]-tagged service whose LLM call is delegated to
/// another [TaskRouting]-tagged service (which emits the cost metric).
/// Prevents double-counting without requiring the outer wrapper to lie
/// about having its own call site.
///
/// Example: TutorMessageService wraps ITutorLlmService (ClaudeTutorLlmService).
/// The inner service is the one calling Anthropic — so it records the cost.
/// TutorMessageService carries [DelegatesLlmCost("ClaudeTutorLlmService")]
/// to document the indirection and satisfy the cost-emission architecture
/// test (CostMetricEmittedTest).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DelegatesLlmCostAttribute : Attribute
{
    /// <summary>
    /// Name of the inner service that actually emits the cost. Human-readable
    /// documentation for PR review; not used at runtime.
    /// </summary>
    public string InnerService { get; }

    public DelegatesLlmCostAttribute(string innerService)
    {
        if (string.IsNullOrWhiteSpace(innerService))
        {
            throw new ArgumentException("InnerService must not be empty.", nameof(innerService));
        }
        InnerService = innerService;
    }
}
