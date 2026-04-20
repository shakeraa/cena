// =============================================================================
// Cena Platform — Tutor service DI registration helpers (prr-012)
//
// Keeps the Program.cs composition root compact (ADR-0012 LOC gate) while
// wiring the three collaborators ClaudeTutorLlmService now requires:
//   - ISocraticCallBudget          (3-call Socratic LLM cap per session)
//   - IStaticHintLadderFallback    (no-LLM degradation path)
//   - IDailyTutorTimeBudget        (30-min/day per-student rest cap)
// =============================================================================

using Cena.Actors.RateLimit;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Tutor;

public static class TutorServiceRegistration
{
    /// <summary>
    /// Registers the prr-012 cost-cap collaborators used by
    /// ClaudeTutorLlmService. Idempotent — safe to call more than once.
    /// </summary>
    public static IServiceCollection AddTutorCostCaps(this IServiceCollection services)
    {
        services.AddSingleton<ISocraticCallBudget, SocraticCallBudget>();
        services.AddSingleton<IStaticHintLadderFallback, StaticHintLadderFallback>();
        services.AddSingleton<IDailyTutorTimeBudget, DailyTutorTimeBudget>();
        return services;
    }
}
