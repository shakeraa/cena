// =============================================================================
// Cena Platform -- Token Revocation Check
// BKD-001.5: Redis-backed revocation list for banned users
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// Checks Redis for revoked user tokens. If revoked:{uid} exists, returns 401.
/// Fails closed when Redis is unavailable unless the UID was recently verified (local cache).
/// Bypasses health check and metrics endpoints.
/// </summary>
public sealed class TokenRevocationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    // Local cache of recently-verified (non-revoked) UIDs for resilience during Redis outages
    private static readonly MemoryCache _recentlyVerified = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });

    private static readonly MemoryCacheEntryOptions _cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        Size = 1
    };

    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
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
        var path = context.Request.Path.Value ?? "";
        if (BypassPaths.Contains(path) || path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase))
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
                        _recentlyVerified.Remove(uid);
                        _logger.LogWarning("Revoked token used by UID {Uid}", uid);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = new { code = "CENA_AUTH_TOKEN_REVOKED", message = "User session has been revoked." }
                        });
                        return;
                    }

                    // UID verified as not-revoked — cache it
                    _recentlyVerified.Set(uid, true, _cacheOptions);
                }
                catch (RedisConnectionException ex)
                {
                    _logger.LogError(ex, "Redis unavailable for revocation check on UID {Uid}", uid);

                    // Check local cache — if recently verified as not-revoked, allow
                    if (_recentlyVerified.TryGetValue(uid, out _))
                    {
                        _logger.LogWarning("Redis down, allowing UID {Uid} from local cache (verified < 5min ago)", uid);
                        // Fall through to next middleware
                    }
                    else
                    {
                        // Redis down AND UID not in cache — fail closed
                        _logger.LogError("Redis down and UID {Uid} not in local cache. Denying request.", uid);
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(
                            "{\"error\":\"Authentication service temporarily unavailable. Please try again.\"}");
                        return;
                    }
                }
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Revoke a user's token. Sets a Redis key with 1-hour TTL (Firebase token lifetime).
    /// Also evicts the UID from the local verification cache.
    /// </summary>
    public static async Task RevokeUserAsync(IConnectionMultiplexer redis, string uid)
    {
        _recentlyVerified.Remove(uid);
        var db = redis.GetDatabase();
        await db.StringSetAsync($"revoked:{uid}", "1", TimeSpan.FromHours(1));
    }
}
