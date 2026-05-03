using Microsoft.Extensions.DependencyInjection;

namespace Cena.LlmAcl.Registration;

public static class LlmAclServiceRegistration
{
    public static IServiceCollection AddCenaLlmAcl(this IServiceCollection services)
    {
        services.AddSingleton<Tracking.GlobalRateLimiter>();
        return services;
    }
}
