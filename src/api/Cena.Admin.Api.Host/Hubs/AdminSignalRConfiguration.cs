// =============================================================================
// Cena Platform — Admin SignalR DI + endpoint wiring (RDY-060)
//
// Mirrors the student-side SignalRConfiguration but scoped to the admin
// hub. Exposed as two extension methods so Program.cs stays readable.
//
// Redis backplane: REDIS_PASSWORD env var (matches the existing
// AddStackExchangeRedis conventions) drives the backplane config.
// Absent password = in-process SignalR (single replica — acceptable
// for dev; production must set the env var).
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Host.Hubs;

public static class AdminSignalRConfiguration
{
    /// <summary>
    /// Register SignalR + the admin hub bridge + Redis backplane when
    /// configured. Call during service registration.
    /// </summary>
    public static IServiceCollection AddCenaAdminSignalR(
        this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.MaximumReceiveMessageSize = 64 * 1024;
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        // Redis backplane — horizontal scaling for multi-replica admin-api.
        // Absent connection string = single-replica in-process SignalR.
        // Non-dev environments should always provide the config.
        var redisConn = BuildRedisConnectionString(configuration);
        if (!string.IsNullOrEmpty(redisConn))
        {
            builder.AddStackExchangeRedis(redisConn, opts =>
            {
                opts.Configuration.ChannelPrefix =
                    StackExchange.Redis.RedisChannel.Literal("cena-admin-signalr");
            });
        }

        services.AddSingleton<AdminGroupManager>();
        services.AddSingleton<AdminSignalRMetrics>();
        services.AddHostedService<NatsAdminBridge>();

        return services;
    }

    /// <summary>
    /// Add a JwtBearer token-extraction pass tailored for the admin hub
    /// WebSocket upgrade. Browsers cannot set Authorization headers on
    /// WS upgrades; the client passes the token as `access_token` query
    /// string. Chains onto any existing OnMessageReceived handler.
    /// </summary>
    public static IServiceCollection AddAdminSignalRTokenExtraction(
        this IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var existing = options.Events?.OnMessageReceived;
                options.Events ??= new JwtBearerEvents();
                options.Events.OnMessageReceived = async context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) &&
                        path.StartsWithSegments("/admin-hub"))
                    {
                        context.Token = accessToken;
                    }

                    if (existing != null) await existing(context);
                };
            });
        return services;
    }

    /// <summary>
    /// Maps `/admin-hub/cena` (authorized, ModeratorOrAbove) and a
    /// `/health/admin-signalr` probe endpoint for connection-count visibility.
    /// </summary>
    public static WebApplication MapCenaAdminHub(this WebApplication app)
    {
        app.MapHub<CenaAdminHub>("/admin-hub/cena")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove);

        app.MapGet("/health/admin-signalr", (AdminGroupManager mgr) =>
        {
            return Results.Ok(new
            {
                status = "healthy",
                service = "cena-admin-signalr-hub",
                activeConnections = mgr.ConnectionCount,
                activeGroups = mgr.GroupCount,
            });
        });

        return app;
    }

    /// <summary>
    /// Build a StackExchange.Redis connection string from the config.
    /// Reads the same REDIS_* env vars as the rest of the stack, so dev
    /// + prod use one wiring convention. Always merges Redis:Password /
    /// REDIS_PASSWORD onto the connection string if one is configured
    /// separately, so `ConnectionStrings:Redis=host:port` + `Redis:Password=…`
    /// wiring (used by docker-compose) works without silently dropping auth.
    /// </summary>
    private static string? BuildRedisConnectionString(IConfiguration configuration)
    {
        var password = configuration["REDIS_PASSWORD"] ?? configuration["Redis:Password"];

        // Prefer the fully-qualified ConnectionStrings:Redis if set.
        var cs = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(cs))
        {
            if (string.IsNullOrEmpty(password) ||
                cs.Contains("password=", StringComparison.OrdinalIgnoreCase))
            {
                return cs;
            }
            return $"{cs},password={password}";
        }

        var host = configuration["REDIS_HOST"] ?? configuration["Redis:Host"];
        var port = configuration["REDIS_PORT"] ?? configuration["Redis:Port"] ?? "6379";

        if (string.IsNullOrEmpty(host)) return null;

        var baseConn = $"{host}:{port}";
        return string.IsNullOrEmpty(password)
            ? baseConn
            : $"{baseConn},password={password}";
    }
}
