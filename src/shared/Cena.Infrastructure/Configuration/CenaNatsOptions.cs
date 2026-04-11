// =============================================================================
// Cena Platform -- NATS Configuration Options
// FIND-sec-003: Eliminates hardcoded NATS credentials from source files.
// Development fallback ONLY active when IHostEnvironment.IsDevelopment() is true.
// Non-development environments throw if credentials are not configured.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Cena.Infrastructure.Configuration;

/// <summary>
/// Centralized NATS authentication resolution.
/// </summary>
public static class CenaNatsOptions
{
    /// <summary>
    /// Resolves NATS API credentials from configuration.
    /// Falls back to dev defaults ONLY in Development environment.
    /// Throws in non-Development if credentials are not configured.
    /// 
    /// Supports multiple configuration key patterns for backward compatibility:
    /// - NATS:ApiUsername / NATS:ApiPassword (preferred)
    /// - Nats:User / Nats:Password (legacy)
    /// </summary>
    /// <param name="config">Application configuration</param>
    /// <param name="env">Host environment</param>
    /// <returns>Tuple of (username, password)</returns>
    /// <exception cref="InvalidOperationException">Thrown in non-Development when credentials not configured</exception>
    public static (string Username, string Password) GetApiAuth(IConfiguration config, IHostEnvironment env)
    {
        var username = config["NATS:ApiUsername"]
            ?? config["Nats:User"]
            ?? Environment.GetEnvironmentVariable("NATS_API_USERNAME");

        var password = config["NATS:ApiPassword"]
            ?? config["Nats:Password"]
            ?? Environment.GetEnvironmentVariable("NATS_API_PASSWORD");

        // If both are configured, use them
        if (username is not null && password is not null)
            return (username, password);

        // In Development, allow fallback to dev defaults
        if (env.IsDevelopment())
        {
            return (
                username ?? "cena_api_user",
                password ?? "dev_api_pass"
            );
        }

        // In non-Development, require explicit configuration
        throw new InvalidOperationException(
            "NATS API credentials not configured. " +
            "Set NATS:ApiUsername and NATS:ApiPassword in appsettings, " +
            "or NATS_API_USERNAME and NATS_API_PASSWORD environment variables.");
    }
}
