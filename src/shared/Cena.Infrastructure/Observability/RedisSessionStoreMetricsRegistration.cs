// =============================================================================
// Cena Platform — RedisSessionStoreMetrics DI registration (prr-020)
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Infrastructure.Observability;

public static class RedisSessionStoreMetricsRegistration
{
    /// <summary>
    /// Register the Redis session-store metrics emitter. Expects an
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/> to already
    /// be registered (by the host's Redis wiring).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Optional — binds
    /// <c>RedisSessionMetrics</c> section to the options. Omit to use
    /// defaults.</param>
    public static IServiceCollection AddCenaRedisSessionStoreMetrics(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var section = configuration?.GetSection("RedisSessionMetrics");
        if (section is not null && section.Exists())
            services.Configure<RedisSessionStoreMetricsOptions>(section);
        else
            services.Configure<RedisSessionStoreMetricsOptions>(_ => { });

        services.AddHostedService<RedisSessionStoreMetricsService>();
        return services;
    }
}
