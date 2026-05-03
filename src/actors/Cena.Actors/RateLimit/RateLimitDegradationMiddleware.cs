// =============================================================================
// Cena Platform — Rate Limit Degradation Middleware (RATE-001)
// Applies distributed token-bucket limits and graceful degradation.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.RateLimit;

/// <summary>
/// Middleware that enforces distributed rate limits and triggers graceful degradation.
/// </summary>
public sealed class RateLimitDegradationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitService _rateLimit;
    private readonly ICostCircuitBreaker _costBreaker;
    private readonly ILogger<RateLimitDegradationMiddleware> _logger;

    public RateLimitDegradationMiddleware(
        RequestDelegate next,
        IRateLimitService rateLimit,
        ICostCircuitBreaker costBreaker,
        ILogger<RateLimitDegradationMiddleware> logger)
    {
        _next = next;
        _rateLimit = rateLimit;
        _costBreaker = costBreaker;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var studentId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? "anonymous";
        var schoolId = context.User.FindFirstValue("school_id") ?? "no-school";
        var path = context.Request.Path.Value ?? "";

        // ── Tier 1: Photo upload limit (10/hour per student) ──
        if (IsPhotoEndpoint(path))
        {
            var photoResult = await _rateLimit.TryAcquireAsync(
                studentId, "photo", capacity: 10, refillRatePerSecond: 0);
            if (!photoResult.Allowed)
            {
                _logger.LogWarning("Photo rate limit exceeded for student {StudentId}", studentId);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = photoResult.RetryAfter.HasValue
                    ? ((photoResult.RetryAfter.Value - DateTimeOffset.UtcNow).TotalSeconds).ToString("F0")
                    : "3600";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Photo upload rate limit exceeded. Please try again later.",
                    degraded = true,
                    photoInputDisabled = true
                });
                return;
            }
        }

        // ── Tier 2: Classroom aggregate limit (500 req/min per school) ──
        // Skip health checks and static assets
        if (!path.StartsWith("/health") && !path.StartsWith("/swagger"))
        {
            var classroomResult = await _rateLimit.TryAcquireAsync(
                schoolId, "classroom", capacity: 500, refillRatePerSecond: 8); // 500/min ≈ 8/sec
            if (!classroomResult.Allowed)
            {
                _logger.LogWarning("Classroom rate limit exceeded for school {SchoolId}", schoolId);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = "60";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Classroom rate limit exceeded. Please try again later.",
                    degraded = true,
                    useCachedContent = true
                });
                return;
            }
        }

        // ── Tier 4: Global cost circuit breaker ──
        // Stamp the request so downstream handlers know to avoid LLM/CAS calls.
        if (await _costBreaker.IsOpenAsync())
        {
            context.Items["Cena:CostCircuitBreakerOpen"] = true;
            _logger.LogInformation("Cost circuit breaker is open — LLM/CAS calls disabled for this request");
        }

        await _next(context);
    }

    private static bool IsPhotoEndpoint(string path)
    {
        return path.Contains("/photo", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/upload", StringComparison.OrdinalIgnoreCase);
    }
}
