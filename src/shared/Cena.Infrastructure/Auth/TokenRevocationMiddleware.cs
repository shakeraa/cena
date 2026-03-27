// =============================================================================
// Cena Platform -- Token Revocation Check
// BKD-001.5: Redis-backed revocation list for banned users
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// Checks Redis for revoked user tokens. If revoked:{uid} exists, returns 401.
/// Bypasses health check endpoints.
/// </summary>
public sealed class TokenRevocationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/ready",
        "/health/live",
        "/metrics"
    };

    public TokenRevocationMiddleware(RequestDelegate next, ILogger<TokenRevocationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConnectionMultiplexer redis)
    {
        if (BypassPaths.Contains(context.Request.Path.Value ?? ""))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var uid = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? context.User.FindFirstValue("user_id");

            if (uid != null)
            {
                try
                {
                    var db = redis.GetDatabase();
                    if (await db.KeyExistsAsync($"revoked:{uid}"))
                    {
                        _logger.LogWarning("Revoked token used by UID {Uid}", uid);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = new { code = "CENA_AUTH_TOKEN_REVOKED", message = "User session has been revoked." }
                        });
                        return;
                    }
                }
                catch (RedisConnectionException ex)
                {
                    // Redis down — allow request through (fail-open for availability)
                    _logger.LogWarning(ex, "Redis unavailable for revocation check. Allowing request.");
                }
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Revoke a user's token. Sets a Redis key with 1-hour TTL (Firebase token lifetime).
    /// </summary>
    public static async Task RevokeUserAsync(IConnectionMultiplexer redis, string uid)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync($"revoked:{uid}", "1", TimeSpan.FromHours(1));
    }
}
