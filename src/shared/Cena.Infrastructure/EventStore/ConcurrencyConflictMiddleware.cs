// =============================================================================
// Cena Platform -- Concurrency Conflict Middleware
// Layer: Infrastructure | Runtime: .NET 9
//
// DATA-010: Catches Marten DcbConcurrencyException from event store appends
// and returns a structured HTTP 409 Conflict response. Logs the conflict
// for monitoring and alerting dashboards.
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Marten.Events.Dcb;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.EventStore;

/// <summary>
/// Middleware that intercepts Marten <see cref="DcbConcurrencyException"/> thrown
/// during event append operations and converts them to HTTP 409 Conflict
/// responses with a structured <see cref="CenaError"/> body.
///
/// Must be registered after <c>GlobalExceptionMiddleware</c> so it catches
/// concurrency errors before they hit the generic 500 handler.
/// DATA-010
/// </summary>
public sealed class ConcurrencyConflictMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConcurrencyConflictMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ConcurrencyConflictMiddleware(
        RequestDelegate next,
        ILogger<ConcurrencyConflictMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DcbConcurrencyException ex)
        {
            await HandleConcurrencyConflictAsync(context, ex);
        }
    }

    private async Task HandleConcurrencyConflictAsync(HttpContext context, DcbConcurrencyException ex)
    {
        var correlationId = CorrelationContext.Current ?? Guid.NewGuid().ToString();

        _logger.LogWarning(
            ex,
            "Optimistic concurrency conflict detected. " +
            "CorrelationId={CorrelationId} Path={Path} UserId={UserId}",
            correlationId,
            context.Request.Path.Value,
            context.User?.FindFirst("uid")?.Value ?? "anonymous");

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Response already started for CorrelationId={CorrelationId}; " +
                "cannot write concurrency conflict body.",
                correlationId);
            return;
        }

        var error = new CenaError(
            ErrorCodes.CENA_ACTOR_VERSION_CONFLICT,
            "A concurrent modification conflict occurred. Please retry your request.",
            ErrorCategory.Conflict,
            new Dictionary<string, object>
            {
                ["hint"] = "The resource was modified by another request. " +
                           "Re-read the current state and retry.",
            },
            correlationId);

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        var response = new ErrorResponse(error);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
