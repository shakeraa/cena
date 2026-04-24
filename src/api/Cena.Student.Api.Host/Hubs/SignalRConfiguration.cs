// =============================================================================
// Cena Platform -- SignalR Configuration (SES-001.3)
// Extension methods for registering SignalR services and hub endpoints.
// =============================================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;

namespace Cena.Api.Host.Hubs;

public static class SignalRConfiguration
{
    /// <summary>
    /// Adds SignalR services and the NatsSignalRBridge hosted service.
    /// Call during service registration (before Build).
    /// </summary>
    public static IServiceCollection AddCenaSignalR(this IServiceCollection services)
    {
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false; // Set true only in Development
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.MaximumReceiveMessageSize = 64 * 1024; // 64KB max message
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        // SignalRGroupManager is a singleton managing student→connection mappings
        services.AddSingleton<SignalRGroupManager>();

        // NatsSignalRBridge runs as a hosted service, pushing NATS events to SignalR
        services.AddHostedService<NatsSignalRBridge>();

        return services;
    }

    /// <summary>
    /// Configures JWT Bearer authentication to extract tokens from SignalR query strings.
    /// Call this AFTER AddFirebaseAuth so it can hook into the existing JwtBearer events.
    /// </summary>
    public static IServiceCollection AddSignalRTokenExtraction(this IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var existingOnMessageReceived = options.Events?.OnMessageReceived;

                options.Events ??= new JwtBearerEvents();
                options.Events.OnMessageReceived = async context =>
                {
                    // SignalR sends the access token as a query string parameter
                    // when using WebSockets (browsers cannot set headers on WS upgrade)
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                    {
                        context.Token = accessToken;
                    }

                    // Chain to any previously registered handler
                    if (existingOnMessageReceived != null)
                    {
                        await existingOnMessageReceived(context);
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// Maps the CenaHub SignalR endpoint and the health check endpoint.
    /// Call during endpoint mapping (after Build, after auth middleware).
    /// </summary>
    public static WebApplication MapCenaHub(this WebApplication app)
    {
        app.MapHub<CenaHub>("/hub/cena").RequireAuthorization();

        // SignalR health check endpoint
        app.MapGet("/health/signalr", (SignalRGroupManager groupManager) =>
        {
            return Results.Ok(new
            {
                status = "healthy",
                service = "cena-signalr-hub",
                activeConnections = groupManager.ConnectionCount,
                connectedStudents = groupManager.ConnectedStudentIds.Count
            });
        });

        return app;
    }
}
