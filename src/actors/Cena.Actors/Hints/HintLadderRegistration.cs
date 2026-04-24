// =============================================================================
// Cena Platform — Hint Ladder DI registration helper (prr-203)
//
// Keeps Program.cs compact while wiring the four collaborators the
// HintLadderOrchestrator needs:
//
//   - IL1TemplateHintGenerator       (no LLM; wraps StaticHintLadderFallback
//                                     + LdAnxiousHintGovernor)
//   - IL2HaikuHintGenerator          (tier-2 Haiku, ideation_l2_hint)
//   - IL3WorkedExampleHintGenerator  (tier-3 Sonnet, worked_example_l3_hint)
//   - IHintLadderOrchestrator        (composes the three)
//
// Assumes the following have already been registered by the host:
//   - IStaticHintLadderFallback      via AddTutorCostCaps()
//   - ISocraticCallBudget            via AddTutorCostCaps()
//   - ILdAnxiousHintGovernor         (host-local registration, prr-029)
//   - ILlmClient, IPiiPromptScrubber, ILlmCostMetric
//     (host-local + AddLlmCostMetric + AddPiiPromptScrubber)
//
// Idempotent — safe to call more than once.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Hints;

public static class HintLadderRegistration
{
    /// <summary>
    /// Register the prr-203 hint-ladder services.
    /// </summary>
    public static IServiceCollection AddHintLadder(this IServiceCollection services)
    {
        services.AddSingleton<IL1TemplateHintGenerator, L1TemplateHintGenerator>();
        services.AddSingleton<IL2HaikuHintGenerator, L2HaikuHintGenerator>();
        services.AddSingleton<IL3WorkedExampleHintGenerator, L3WorkedExampleHintGenerator>();
        services.AddSingleton<IHintLadderOrchestrator, HintLadderOrchestrator>();
        return services;
    }
}
