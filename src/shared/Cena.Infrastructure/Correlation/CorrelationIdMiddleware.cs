using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Correlation;

/// <summary>
/// Reads X-Correlation-Id from the request header (or generates a new GUID),
/// stores it in CorrelationContext, tags the current Activity, and echoes it
/// in the response header.  Must be registered before other middleware so that
/// all downstream log entries carry the correlation ID.
/// ERR-001.4
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private const int MaxCorrelationIdLength = 256;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        // Store in AsyncLocal for use by downstream code (services, actors, NATS publishers)
        CorrelationContext.Current = correlationId;

        // Tag the current OpenTelemetry Activity so it appears in distributed traces
        Activity.Current?.SetTag("correlation.id", correlationId);

        // Echo back in response header so the caller can correlate
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["CorrelationId"] = correlationId
               }))
        {
            await _next(context);
        }
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            var id = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(id))
            {
                if (id.Length > MaxCorrelationIdLength)
                {
                    _logger.LogWarning(
                        "Correlation ID from client exceeds {Max} characters and was truncated. Original length: {Length}",
                        MaxCorrelationIdLength, id.Length);
                    id = id[..MaxCorrelationIdLength];
                }
                return id;
            }
        }

        return Guid.NewGuid().ToString();
    }
}
