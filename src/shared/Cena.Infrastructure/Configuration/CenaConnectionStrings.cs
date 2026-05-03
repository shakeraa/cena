// =============================================================================
// Cena Platform -- Centralized Connection String Resolution
// REV-010: Eliminates hardcoded credentials from source files.
// Development fallback ONLY active when IHostEnvironment.IsDevelopment() is true.
// Non-development environments throw if connection strings are not configured.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Cena.Infrastructure.Configuration;

public static class CenaConnectionStrings
{
    /// <summary>
    /// Resolves PostgreSQL connection string from configuration.
    /// Falls back to dev default ONLY in Development environment.
    /// Throws in non-Development if not configured.
    /// </summary>
    public static string GetPostgres(IConfiguration config, IHostEnvironment env)
    {
        var connectionString = config.GetConnectionString("PostgreSQL")
            ?? config["ConnectionStrings:Marten"]
            ?? Environment.GetEnvironmentVariable("CENA_POSTGRES_CONNECTION");

        if (connectionString is not null)
            return connectionString;

        if (env.IsDevelopment())
            return "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

        throw new InvalidOperationException(
            "PostgreSQL connection string not configured. " +
            "Set ConnectionStrings:PostgreSQL in appsettings or CENA_POSTGRES_CONNECTION env var.");
    }

    /// <summary>
    /// Resolves Redis connection string from configuration.
    /// Falls back to dev default ONLY in Development environment.
    /// Throws in non-Development if not configured.
    /// </summary>
    public static string GetRedis(IConfiguration config, IHostEnvironment env)
    {
        var connectionString = config.GetConnectionString("Redis")
            ?? Environment.GetEnvironmentVariable("CENA_REDIS_CONNECTION");

        if (connectionString is not null)
            return connectionString;

        if (env.IsDevelopment())
            return "localhost:6380,abortConnect=false,connectRetry=3";

        throw new InvalidOperationException(
            "Redis connection string not configured. " +
            "Set ConnectionStrings:Redis in appsettings or CENA_REDIS_CONNECTION env var.");
    }
}
