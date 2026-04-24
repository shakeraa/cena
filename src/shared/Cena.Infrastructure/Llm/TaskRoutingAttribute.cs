// =============================================================================
// Cena Platform — TaskRouting attribute (ADR-0026)
//
// Every class or method that constructs or consumes an LLM client must carry
// a [TaskRouting] attribute identifying:
//
//   1. The routing tier (tier1 | tier2 | tier3) per ADR-0026 §Tier selection
//      policy:
//        - tier1 = Agent Booster / deterministic transform — no LLM call
//        - tier2 = Haiku / cheap path (~$0.0002 per call, <30% complexity)
//        - tier3 = Sonnet or Opus (default) — complex reasoning only
//
//   2. The task name — must match a row in `contracts/llm/routing-config.yaml`
//      under `task_routing:` (e.g. "socratic_question", "error_classification",
//      "diagram_generation"). The scanner at
//      scripts/shipgate/llm-routing-scanner.mjs validates this correspondence.
//
// CI enforcement:
//   - scripts/shipgate/llm-routing-scanner.mjs fails the build on any LLM call
//     site missing [TaskRouting] unless explicitly allowlisted in
//     scripts/shipgate/llm-routing-allowlist.yml.
//
// Intentionally lives under Cena.Infrastructure so it is referenceable from
// every LLM-consuming project (Cena.Actors, Cena.Admin.Api, Cena.Student.Api,
// future Moonshot adapters) without a new dependency.
//
// See docs/adr/0026-llm-three-tier-routing.md.
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Declares the three-tier LLM routing classification for the annotated class
/// or method. Required by ADR-0026 on every call site that constructs or
/// consumes an LLM client (Anthropic, Moonshot, or future providers).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TaskRoutingAttribute : Attribute
{
    /// <summary>
    /// Routing tier per ADR-0026. Expected values: "tier1", "tier2", "tier3".
    /// </summary>
    public string Tier { get; }

    /// <summary>
    /// Task name; must match a row in contracts/llm/routing-config.yaml under
    /// task_routing (e.g. "socratic_question", "error_classification").
    /// </summary>
    public string TaskName { get; }

    public TaskRoutingAttribute(string tier, string taskName)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            throw new ArgumentException("Tier must not be empty.", nameof(tier));
        }
        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("TaskName must not be empty.", nameof(taskName));
        }
        if (tier is not ("tier1" or "tier2" or "tier3"))
        {
            throw new ArgumentException(
                $"Tier must be 'tier1', 'tier2', or 'tier3'; got '{tier}'.",
                nameof(tier));
        }
        Tier = tier;
        TaskName = taskName;
    }
}
