using System.Net;
using System.Text.Json;
using Cena.Infrastructure.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Errors;

/// <summary>
/// Catches all unhandled exceptions, maps them to RFC 9457-compatible JSON
/// (wrapped in the Cena ErrorResponse envelope), adds the X-Correlation-Id
/// response header, and logs structured context.
///
/// Register before other middleware so the handler wraps the full pipeline.
/// ERR-001.2
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = CorrelationContext.Current ?? Guid.NewGuid().ToString();

        // Ensure the response header is set even when the handler runs
        if (!context.Response.HasStarted)
            context.Response.Headers["X-Correlation-Id"] = correlationId;

        var (statusCode, cenaError) = MapException(exception, correlationId);

        _logger.LogError(
            exception,
            "Unhandled exception. ErrorCode={ErrorCode} Category={Category} CorrelationId={CorrelationId} UserId={UserId}",
            cenaError.Code,
            cenaError.Category,
            correlationId,
            context.User?.FindFirst("uid")?.Value ?? "anonymous");

        if (context.Response.HasStarted)
        {
            // Headers already sent — cannot change status code
            _logger.LogWarning(
                "Response already started for CorrelationId={CorrelationId}; cannot write error body.",
                correlationId);
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        // In development, attach the stack trace in a debug-only field
        ErrorResponse response;
        if (_environment.IsDevelopment() && exception is not CenaException)
        {
            var details = cenaError.Details is not null
                ? new Dictionary<string, object>(cenaError.Details)
                : new Dictionary<string, object>();

            details["__debug_stacktrace"] = exception.ToString();
            response = new ErrorResponse(cenaError with { Details = details });
        }
        else
        {
            response = new ErrorResponse(cenaError);
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private (int statusCode, CenaError error) MapException(Exception exception, string correlationId)
    {
        // Structured CenaExceptions carry their own status code and error code
        if (exception is CenaException cena)
        {
            return (cena.StatusCode, cena.ToCenaError(correlationId));
        }

        // Map common BCL / ASP.NET exceptions to appropriate status codes
        var (code, category, status, message) = exception switch
        {
            Microsoft.AspNetCore.Http.BadHttpRequestException e =>
                (ErrorCodes.CENA_INTERNAL_VALIDATION, ErrorCategory.Validation, 400, e.Message),

            UnauthorizedAccessException =>
                (ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE, ErrorCategory.Authorization, 403, "Forbidden"),

            KeyNotFoundException =>
                (ErrorCodes.CENA_INTERNAL_ERROR, ErrorCategory.NotFound, 404, "Resource not found"),

            OperationCanceledException =>
                (ErrorCodes.CENA_LLM_TIMEOUT, ErrorCategory.Timeout, 504, "Request timed out"),

            _ =>
                (ErrorCodes.CENA_INTERNAL_ERROR, ErrorCategory.Internal, 500, "Internal server error")
        };

        return (status, new CenaError(code, message, category, null, correlationId));
    }
}
