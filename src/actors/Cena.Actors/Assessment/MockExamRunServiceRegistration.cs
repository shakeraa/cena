// =============================================================================
// Cena Platform — DI registration for the mock-exam (Bagrut שאלון playbook)
// runner. Hooks the bounded-context's Marten registration into Marten's
// store-options builder and registers IMockExamRunService.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Assessment;

public static class MockExamRunServiceRegistration
{
    /// <summary>
    /// Activates the mock-exam runner: registers Marten doc/events for the
    /// bounded context and the IMockExamRunService scoped lifetime. Idempotent
    /// — safe to call from multiple host registrations.
    /// </summary>
    public static IServiceCollection AddMockExamRunner(this IServiceCollection services)
    {
        services.ConfigureMarten(opts => opts.RegisterMockExamRunContext());
        services.TryAddScoped<IMockExamRunService, MockExamRunService>();
        return services;
    }
}
