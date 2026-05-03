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
/// RDY-017: TLS configuration for NATS connections.
/// </summary>
public sealed class CenaNatsTlsOptions
{
    /// <summary>Enable TLS for NATS connections (default: false for dev).</summary>
    public bool Enabled { get; set; }

    /// <summary>Path to CA certificate file (PEM) for server verification.</summary>
    public string? CaCertPath { get; set; }

    /// <summary>Path to client certificate file (PEM) for mutual TLS.</summary>
    public string? ClientCertPath { get; set; }

    /// <summary>Path to client private key file (PEM) for mutual TLS.</summary>
    public string? ClientKeyPath { get; set; }

    /// <summary>Skip server certificate verification (dev only, never in production).</summary>
    public bool InsecureSkipVerify { get; set; }
}

/// <summary>
/// Centralized NATS authentication resolution.
/// </summary>
public static class CenaNatsOptions
{
    /// <summary>
    /// RDY-017: Resolve TLS configuration from IConfiguration.
    /// Returns null if TLS is not configured (dev plaintext fallback).
    /// </summary>
    public static CenaNatsTlsOptions? GetTlsOptions(IConfiguration config)
    {
        var section = config.GetSection("NATS:TLS");
        if (!section.Exists()) return null;

        var opts = new CenaNatsTlsOptions();
        section.Bind(opts);
        return opts.Enabled ? opts : null;
    }
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

    /// <summary>
    /// Resolves NATS Actor host credentials from configuration.
    /// Falls back to dev defaults ONLY in Development environment.
    /// Throws in non-Development if credentials are not configured.
    /// 
    /// Supports multiple configuration key patterns for backward compatibility:
    /// - NATS:ActorUsername / NATS:ActorPassword (preferred)
    /// - Nats:User / Nats:Password (legacy)
    /// </summary>
    /// <param name="config">Application configuration</param>
    /// <param name="env">Host environment</param>
    /// <returns>Tuple of (username, password)</returns>
    /// <exception cref="InvalidOperationException">Thrown in non-Development when credentials not configured</exception>
    public static (string Username, string Password) GetActorAuth(IConfiguration config, IHostEnvironment env)
    {
        var username = config["NATS:ActorUsername"]
            ?? config["Nats:User"]
            ?? Environment.GetEnvironmentVariable("NATS_ACTOR_USERNAME");

        var password = config["NATS:ActorPassword"]
            ?? config["Nats:Password"]
            ?? Environment.GetEnvironmentVariable("NATS_ACTOR_PASSWORD");

        // If both are configured, use them
        if (username is not null && password is not null)
            return (username, password);

        // In Development, allow fallback to dev defaults
        if (env.IsDevelopment())
        {
            return (
                username ?? "actor-host",
                password ?? "dev_actor_pass"
            );
        }

        // In non-Development, require explicit configuration
        throw new InvalidOperationException(
            "NATS Actor credentials not configured. " +
            "Set NATS:ActorUsername and NATS:ActorPassword in appsettings, " +
            "or NATS_ACTOR_USERNAME and NATS_ACTOR_PASSWORD environment variables.");
    }
}
