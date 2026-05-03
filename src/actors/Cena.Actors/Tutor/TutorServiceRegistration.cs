// =============================================================================
// Cena Platform — Tutor service DI registration helpers (prr-012, prr-105)
//
// Keeps the Program.cs composition root compact (ADR-0012 LOC gate) while
// wiring the collaborators ClaudeTutorLlmService now requires:
//   - ISocraticCallBudget          (3-call Socratic LLM cap per session)
//   - IStaticHintLadderFallback    (no-LLM degradation path)
//   - IDailyTutorTimeBudget        (30-min/day per-student rest cap)
//   - ITutorTurnBudget             (per-session turn cap from ADR-0002, prr-105)
// =============================================================================

using Cena.Actors.RateLimit;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Tutor;

public static class TutorServiceRegistration
{
    /// <summary>
    /// Registers the prr-012 cost-cap collaborators + the prr-105 turn-budget
    /// + the prr-143 activity propagator used by ClaudeTutorLlmService.
    /// Idempotent — safe to call more than once.
    /// </summary>
    public static IServiceCollection AddTutorCostCaps(this IServiceCollection services)
    {
        services.TryAddSingleton<ISocraticCallBudget, SocraticCallBudget>();
        services.TryAddSingleton<IStaticHintLadderFallback, StaticHintLadderFallback>();
        services.TryAddSingleton<IDailyTutorTimeBudget, DailyTutorTimeBudget>();
        services.TryAddSingleton<ITutorTurnBudget, TutorTurnBudget>();
        services.TryAddSingleton<IActivityPropagator, ActivityPropagator>();
        return services;
    }
}
